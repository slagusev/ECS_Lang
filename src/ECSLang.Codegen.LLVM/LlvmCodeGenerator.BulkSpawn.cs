using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef GenerateBulkSpawnExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        BulkSpawnExpressionNode node,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var targetVal = CompileExpression(context, module, builder, function, node.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

        bool isCommandsTarget = (node.Target is IdentifierExpression targetIdent &&
                                varTypes.TryGetValue(targetIdent.Name, out var tType) &&
                                tType == "Commands") ||
                                _typeChecker.GetNodeType(node.Target) == TypeSymbol.Commands;

        if (isCommandsTarget)
        {
            var cmdSpawnFunc = module.GetNamedFunction("world_cmd_spawn");
            var cmdSpawnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(cmdSpawnFunc);
            var cmdEVal = builder.BuildCall2(cmdSpawnType, cmdSpawnFunc, new[] { targetVal }, "cmd_e");

            foreach (var compExpr in node.Components)
            {
                if (compExpr is CallExpression call)
                {
                    string compName = call.Callee;
                    var cleanCallee = TypeSymbol.ToMonomorphizedIdentifier(compName);
                    var setFunc = module.GetNamedFunction($"world_cmd_set_{cleanCallee}");
                    if (setFunc.Handle == IntPtr.Zero)
                        setFunc = module.GetNamedFunction($"world_cmd_set_{compName}");

                    if (setFunc.Handle != IntPtr.Zero)
                    {
                        var callArgs = new List<LLVMValueRef> { targetVal, cmdEVal };
                        foreach (var arg in call.Arguments)
                        {
                            callArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                        }
                        var funcType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(setFunc);
                        builder.BuildCall2(funcType, setFunc, callArgs.ToArray(), "");
                    }
                }
            }

            return cmdEVal;
        }

        // --- High-Performance Zero-Migration Archetype Spawn ---

        // 1. Pre-evaluate all component arguments
        var evaluatedComps = new List<(string Name, int Id, LLVMTypeRef StructType, List<LLVMValueRef> ArgValues, LLVMValueRef? WholeVal)>();
        ulong targetMask = 0UL;

        foreach (var compExpr in node.Components)
        {
            if (compExpr is CallExpression call)
            {
                string compName = call.Callee;
                int compId = ecs.GetComponentId(compName);
                if (compId < 0)
                {
                    var clean = TypeSymbol.ToMonomorphizedIdentifier(compName);
                    compId = ecs.GetComponentId(clean);
                    if (compId >= 0) compName = clean;
                }

                if (compId >= 0)
                {
                    targetMask |= (1UL << compId);
                    var compStructType = ecs.GetComponentStructType(compName);
                    var argVals = new List<LLVMValueRef>();
                    for (int f = 0; f < call.Arguments.Count; f++)
                    {
                        var fVal = CompileExpression(context, module, builder, function, call.Arguments[f], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var expectedFieldType = compStructType.StructGetTypeAtIndex((uint)f);
                        if (fVal.TypeOf != expectedFieldType)
                        {
                            if (fVal.TypeOf == context.Int32Type && expectedFieldType == context.FloatType)
                                fVal = builder.BuildSIToFP(fVal, expectedFieldType, "si_to_fp");
                            else if (fVal.TypeOf == context.DoubleType && expectedFieldType == context.FloatType)
                                fVal = builder.BuildFPTrunc(fVal, expectedFieldType, "fp_trunc");
                            else if (fVal.TypeOf == context.FloatType && expectedFieldType == context.DoubleType)
                                fVal = builder.BuildFPExt(fVal, expectedFieldType, "fp_ext");
                            else if (fVal.TypeOf == context.Int64Type && expectedFieldType == context.Int32Type)
                                fVal = builder.BuildTrunc(fVal, expectedFieldType, "i64_trunc");
                            else if (fVal.TypeOf == context.Int32Type && expectedFieldType == context.Int64Type)
                                fVal = builder.BuildSExt(fVal, expectedFieldType, "i32_sext");
                        }
                        argVals.Add(fVal);
                    }
                    evaluatedComps.Add((compName, compId, compStructType, argVals, null));
                }
            }
            else
            {
                var compType = _typeChecker.GetNodeType(compExpr);
                string compName = compType.Name;
                int compId = ecs.GetComponentId(compName);
                if (compId < 0)
                {
                    var clean = TypeSymbol.ToMonomorphizedIdentifier(compName);
                    compId = ecs.GetComponentId(clean);
                    if (compId >= 0) compName = clean;
                }

                if (compId >= 0)
                {
                    targetMask |= (1UL << compId);
                    var compStructType = ecs.GetComponentStructType(compName);
                    var wholeVal = CompileExpression(context, module, builder, function, compExpr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    evaluatedComps.Add((compName, compId, compStructType, new List<LLVMValueRef>(), wholeVal));
                }
            }
        }

        var worldStructType = ecs.GetWorldStructType();
        var archStructType = ecs.GetArchetypeStructType();
        var archPtrType = LLVMTypeRef.CreatePointer(archStructType, 0);

        // 2. Allocate new entity ID in world
        var allocEntFunc = module.GetNamedFunction("world_alloc_entity");
        var allocEntType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(allocEntFunc);
        var eVal = builder.BuildCall2(allocEntType, allocEntFunc, new[] { targetVal }, "e_alloc");

        // 3. Find or create target archetype
        var maskArrayType = ecs.MaskArrayType;
        var maskWords = ecs.MaskWords;
        var maskBuf = builder.BuildAlloca(maskArrayType, "spawn_mask_buf");
        for (int w = 0; w < maskWords; w++)
        {
            var dstW = builder.BuildInBoundsGEP2(maskArrayType, maskBuf, new[]
            {
                LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)w)
            }, $"dst_spawn_w{w}");
            var wordVal = (w == 0) ? targetMask : 0UL;
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int64Type, wordVal), dstW);
        }
        var getArchFunc = module.GetNamedFunction("world_get_or_create_archetype");
        var getArchType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getArchFunc);
        var archIdx = builder.BuildCall2(getArchType, getArchFunc, new[] { targetVal, maskBuf }, "arch_idx");

        // 4. Ensure archetype capacity (world_grow_archetype if needed)
        var archTablesSlot = builder.BuildStructGEP2(worldStructType, targetVal, 2, "tables_slot");
        var archTablesBase = builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var archPtr = builder.BuildInBoundsGEP2(archStructType, archTablesBase, new[] { archIdx }, "arch_ptr");

        var cntSlot = builder.BuildStructGEP2(archStructType, archPtr, 1, "cnt_slot");
        var curCnt = builder.BuildLoad2(context.Int32Type, cntSlot, "cur_cnt");
        var capSlot = builder.BuildStructGEP2(archStructType, archPtr, 2, "cap_slot");
        var curCap = builder.BuildLoad2(context.Int32Type, capSlot, "cur_cap");

        var needGrow = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCnt, curCap, "need_grow");
        var growBB = function.AppendBasicBlock("bspawn_grow");
        var contBB = function.AppendBasicBlock("bspawn_cont");

        builder.BuildCondBr(needGrow, growBB, contBB);

        builder.PositionAtEnd(growBB);
        var growArchFunc = module.GetNamedFunction("world_grow_archetype");
        var growArchType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(growArchFunc);
        builder.BuildCall2(growArchType, growArchFunc, new[] { targetVal, archIdx }, "");
        builder.BuildBr(contBB);

        builder.PositionAtEnd(contBB);

        // 5. Allocate row in target archetype
        var archTablesBaseCont = builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base_cont");
        var archPtrCont = builder.BuildInBoundsGEP2(archStructType, archTablesBaseCont, new[] { archIdx }, "arch_ptr_cont");
        var cntSlotCont = builder.BuildStructGEP2(archStructType, archPtrCont, 1, "cnt_slot_cont");
        var row = builder.BuildLoad2(context.Int32Type, cntSlotCont, "row");
        var nextCnt = builder.BuildAdd(row, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_cnt");
        builder.BuildStore(nextCnt, cntSlotCont);

        // 6. Set arch.entities[row] = eVal
        var entSlot = builder.BuildStructGEP2(archStructType, archPtrCont, 3, "ent_slot");
        var entRaw = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int8Type, 0), entSlot, "ent_raw");
        var entTyped = builder.BuildBitCast(entRaw, LLVMTypeRef.CreatePointer(context.Int32Type, 0), "ent_typed");
        var entElem = builder.BuildInBoundsGEP2(context.Int32Type, entTyped, new[] { row }, "ent_elem");
        builder.BuildStore(eVal, entElem);

        // 7. Update world entity tracking: world.entity_arch[eVal] = archIdx, world.entity_row[eVal] = row
        var entArchSlot = builder.BuildStructGEP2(worldStructType, targetVal, 5, "ent_arch_slot");
        var entArchArr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int32Type, 0), entArchSlot, "ent_arch_arr");
        var eArchElem = builder.BuildInBoundsGEP2(context.Int32Type, entArchArr, new[] { eVal }, "e_arch_elem");
        builder.BuildStore(archIdx, eArchElem);

        var entRowSlot = builder.BuildStructGEP2(worldStructType, targetVal, 6, "ent_row_slot");
        var entRowArr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int32Type, 0), entRowSlot, "ent_row_arr");
        var eRowElem = builder.BuildInBoundsGEP2(context.Int32Type, entRowArr, new[] { eVal }, "e_row_elem");
        builder.BuildStore(row, eRowElem);

        // 8. Write component columns directly at row
        var colsArrSlot = builder.BuildStructGEP2(archStructType, archPtrCont, 4, "cols_arr");

        foreach (var comp in evaluatedComps)
        {
            var colSlot = builder.BuildInBoundsGEP2(ecs.GetColumnsArrayType(), colsArrSlot, new[]
            {
                LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)comp.Id)
            }, $"col_slot_{comp.Name}");

            var colRaw = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int8Type, 0), colSlot, $"col_raw_{comp.Name}");
            var colTyped = builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(comp.StructType, 0), $"col_typed_{comp.Name}");
            var elemPtr = builder.BuildInBoundsGEP2(comp.StructType, colTyped, new[] { row }, $"elem_{comp.Name}");

            if (comp.WholeVal.HasValue)
            {
                builder.BuildStore(comp.WholeVal.Value, elemPtr);
            }
            else
            {
                for (int f = 0; f < comp.ArgValues.Count; f++)
                {
                    var fGEP = builder.BuildStructGEP2(comp.StructType, elemPtr, (uint)f, $"{comp.Name}_f{f}_gep");
                    builder.BuildStore(comp.ArgValues[f], fGEP);
                }
            }
        }

        return eVal;
    }
}
