using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe void CompileSystem(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        SystemDeclaration sys,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        builder.CurrentDebugLocation = default;
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var sysFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var sysFunc = module.AddFunction($"system_{sys.Name}", sysFuncType);
        var worldParam = sysFunc.GetParam(0);
        worldParam.Name = "world";

        if (_diBuilder.HasValue)
        {
            var subroutineType = _diBuilder.Value.CreateSubroutineType(_diFile, Array.Empty<LLVMMetadataRef>(), LLVMDIFlags.LLVMDIFlagZero);
            uint line = (uint)Math.Max(1, sys.Span.Line);
            var subprogram = _diBuilder.Value.CreateFunction(
                _diFile,
                $"system_{sys.Name}",
                $"system_{sys.Name}",
                _diFile,
                line,
                subroutineType,
                0,
                1,
                line,
                LLVMDIFlags.LLVMDIFlagZero,
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0);
            LlvmApi.SetSubprogram(sysFunc, subprogram);
            _currentSubprogram = subprogram;
            var fnLoc = LlvmApi.DIBuilderCreateDebugLocation(context, line, 1, subprogram, null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, fnLoc);
        }

        var entryBB = sysFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var worldAlloca = builder.BuildAlloca(worldPtrType, "world_alloca");
        builder.BuildStore(worldParam, worldAlloca);

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        if (sys.IsEventSystem)
        {
            var evParam = sys.ReadParams[0];
            string evTypeName = evParam.TypeName;
            int evBaseOffset = ecs.GetEventWorldOffset(evTypeName);
            var evStructType = ecs.GetComponentStructType(evTypeName);
            var evPtrType = LLVMTypeRef.CreatePointer(evStructType, 0);

            var rCountSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)(evBaseOffset + 0), "ev_rcount_slot");
            var rDataSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)(evBaseOffset + 2), "ev_rdata_slot");

            var rCount = builder.BuildLoad2(context.Int32Type, rCountSlot, "ev_rcount");
            var rDataRaw = builder.BuildLoad2(i8PtrType, rDataSlot, "ev_rdata_raw");
            var rDataTyped = builder.BuildBitCast(rDataRaw, evPtrType, "ev_rdata_typed");

            var evIdxAlloca = builder.BuildAlloca(context.Int32Type, "ev_idx");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), evIdxAlloca);

            var evCondBB = sysFunc.AppendBasicBlock("ev_cond");
            var evBodyBB = sysFunc.AppendBasicBlock("ev_body");
            var evIncBB = sysFunc.AppendBasicBlock("ev_inc");
            var evExitBB = sysFunc.AppendBasicBlock("ev_exit");

            builder.BuildBr(evCondBB);

            // ev_cond
            builder.PositionAtEnd(evCondBB);
            var curEvIdx = builder.BuildLoad2(context.Int32Type, evIdxAlloca, "cur_ev_idx");
            var hasMoreEvs = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curEvIdx, rCount, "has_more_evs");
            builder.BuildCondBr(hasMoreEvs, evBodyBB, evExitBB);

            // ev_body
            builder.PositionAtEnd(evBodyBB);
            var evElemPtr = builder.BuildInBoundsGEP2(evStructType, rDataTyped, new[] { curEvIdx }, "ev_elem");

            var evLocals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
            var evVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);

            evLocals[evParam.Name] = evElemPtr;
            evVarTypes[evParam.Name] = evTypeName;

            evLocals["world"] = worldAlloca;
            evVarTypes["world"] = "World";

            // Bind any resources specified in read(...)
            for (int r = 1; r < sys.ReadParams.Count; r++)
            {
                var rp = sys.ReadParams[r];
                if (_typeChecker.Resources.ContainsKey(rp.TypeName))
                {
                    int resOffset = ecs.GetResourceOffset(rp.TypeName);
                    var resSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)resOffset, $"{rp.Name}_res_slot");
                    evLocals[rp.Name] = resSlot;
                    evVarTypes[rp.Name] = rp.TypeName;
                }
                else if (rp.TypeName == "Commands")
                {
                    evLocals[rp.Name] = worldAlloca;
                    evVarTypes[rp.Name] = "Commands";
                }
            }

            CompileBlock(context, module, builder, sysFunc, sys.Body, evLocals, evVarTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(evIncBB);
            }

            // ev_inc
            builder.PositionAtEnd(evIncBB);
            var nextEvIdx = builder.BuildAdd(curEvIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_ev_idx");
            builder.BuildStore(nextEvIdx, evIdxAlloca);
            builder.BuildBr(evCondBB);

            // ev_exit
            builder.PositionAtEnd(evExitBB);
            builder.BuildRetVoid();
            return;
        }

        // Compute required query component mask and without mask
        int maskWords = ecs.MaskWords;
        var requiredWords = new ulong[maskWords];
        var withoutWords = new ulong[maskWords];

        foreach (var qp in sys.QueryParams)
        {
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                int compId = ecs.GetComponentId(qp.TypeName);
                if (compId >= 0)
                {
                    int wIdx = compId >> 6;
                    int bIdx = compId & 63;
                    if (wIdx < maskWords)
                        requiredWords[wIdx] |= (1UL << bIdx);
                }
            }
        }

        foreach (var filter in sys.Filters)
        {
            if (filter.Kind == QueryFilterKind.With && _typeChecker.Components.ContainsKey(filter.ComponentName))
            {
                int compId = ecs.GetComponentId(filter.ComponentName);
                if (compId >= 0)
                {
                    int wIdx = compId >> 6;
                    int bIdx = compId & 63;
                    if (wIdx < maskWords)
                        requiredWords[wIdx] |= (1UL << bIdx);
                }
            }
            else if (filter.Kind == QueryFilterKind.Without && _typeChecker.Components.ContainsKey(filter.ComponentName))
            {
                int compId = ecs.GetComponentId(filter.ComponentName);
                if (compId >= 0)
                {
                    int wIdx = compId >> 6;
                    int bIdx = compId & 63;
                    if (wIdx < maskWords)
                        withoutWords[wIdx] |= (1UL << bIdx);
                }
            }
        }

        // Global constant query masks for introspection/metadata
        string cleanSysName = sysFunc.Name;
        var reqConstArr = requiredWords.Select(w => LLVMValueRef.CreateConstInt(context.Int64Type, w)).ToArray();
        var reqGlobal = module.AddGlobal(ecs.MaskArrayType, $"_ecs_query_req_{cleanSysName}");
        reqGlobal.Initializer = LLVMValueRef.CreateConstArray(context.Int64Type, reqConstArr);
        reqGlobal.Linkage = LLVMLinkage.LLVMInternalLinkage;
        LlvmApi.SetGlobalConstant(reqGlobal, 1);

        var withoutConstArr = withoutWords.Select(w => LLVMValueRef.CreateConstInt(context.Int64Type, w)).ToArray();
        var withoutGlobal = module.AddGlobal(ecs.MaskArrayType, $"_ecs_query_without_{cleanSysName}");
        withoutGlobal.Initializer = LLVMValueRef.CreateConstArray(context.Int64Type, withoutConstArr);
        withoutGlobal.Linkage = LLVMLinkage.LLVMInternalLinkage;
        LlvmApi.SetGlobalConstant(withoutGlobal, 1);

        // Outer loop: iterate over all archetypes
        var archCountSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, 0, "world_arch_count_slot");
        var numArchs = builder.BuildLoad2(context.Int32Type, archCountSlot, "num_archs");
        var archIdxAlloca = builder.BuildAlloca(context.Int32Type, "arch_idx");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), archIdxAlloca);

        var archCondBB = sysFunc.AppendBasicBlock("arch_cond");
        var archBodyBB = sysFunc.AppendBasicBlock("arch_body");
        var nextArchBB = sysFunc.AppendBasicBlock("next_arch");
        var sysExitBB = sysFunc.AppendBasicBlock("sys_exit");

        builder.BuildBr(archCondBB);

        // arch_cond: while (arch_idx < num_archs)
        builder.PositionAtEnd(archCondBB);
        var curArchIdx = builder.BuildLoad2(context.Int32Type, archIdxAlloca, "cur_arch_idx");
        var hasMoreArchs = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdx, numArchs, "has_more_archs");
        builder.BuildCondBr(hasMoreArchs, archBodyBB, sysExitBB);

        // arch_body: check if archetype matches requiredMask and does not contain withoutMask
        builder.PositionAtEnd(archBodyBB);
        var archPtrType = LLVMTypeRef.CreatePointer(ecs.GetArchetypeStructType(), 0);
        var archTablesSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, 2, "world_arch_tables_slot");
        var tablesBase = builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var curArchPtr = builder.BuildInBoundsGEP2(ecs.GetArchetypeStructType(), tablesBase, new[] { curArchIdx }, "cur_arch_ptr");

        var maskSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 0, "mask_slot");

        // Multi-word matching with zero-word skip:
        LLVMValueRef? isMatch = null;
        for (int w = 0; w < maskWords; w++)
        {
            ulong reqW = requiredWords[w];
            ulong withoutW = withoutWords[w];

            // Zero-word skip: if both are zero, this word imposes no constraints on the archetype
            if (reqW == 0 && withoutW == 0)
                continue;

            var maskWPtr = builder.BuildInBoundsGEP2(ecs.MaskArrayType, maskSlot, new[]
            {
                LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)w)
            }, $"mask_w{w}_ptr");
            var archMaskW = builder.BuildLoad2(context.Int64Type, maskWPtr, $"arch_mask_w{w}");

            LLVMValueRef wordMatch;
            if (reqW != 0)
            {
                var reqValW = LLVMValueRef.CreateConstInt(context.Int64Type, reqW);
                var andMaskW = builder.BuildAnd(archMaskW, reqValW, $"and_mask_w{w}");
                var hasReqW = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andMaskW, reqValW, $"has_req_w{w}");

                if (withoutW != 0)
                {
                    var withoutValW = LLVMValueRef.CreateConstInt(context.Int64Type, withoutW);
                    var andWithoutW = builder.BuildAnd(archMaskW, withoutValW, $"and_without_w{w}");
                    var hasNoWithoutW = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andWithoutW, LLVMValueRef.CreateConstInt(context.Int64Type, 0), $"has_no_without_w{w}");
                    wordMatch = builder.BuildAnd(hasReqW, hasNoWithoutW, $"word_{w}_match");
                }
                else
                {
                    wordMatch = hasReqW;
                }
            }
            else
            {
                var withoutValW = LLVMValueRef.CreateConstInt(context.Int64Type, withoutW);
                var andWithoutW = builder.BuildAnd(archMaskW, withoutValW, $"and_without_w{w}");
                wordMatch = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andWithoutW, LLVMValueRef.CreateConstInt(context.Int64Type, 0), $"has_no_without_w{w}");
            }

            isMatch = isMatch == null
                ? wordMatch
                : builder.BuildAnd(isMatch.Value, wordMatch, $"is_match_w{w}");
        }

        var finalMatch = isMatch ?? LLVMValueRef.CreateConstInt(context.Int1Type, 1);

        var checkCountBB = sysFunc.AppendBasicBlock("check_count");
        builder.BuildCondBr(finalMatch, checkCountBB, nextArchBB);

        // check_count: if (arch.count > 0)
        builder.PositionAtEnd(checkCountBB);
        var cntSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 1, "cnt_slot");
        var archCount = builder.BuildLoad2(context.Int32Type, cntSlot, "arch_count");
        var hasEntities = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, archCount, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "has_entities");

        var entLoopHeaderBB = sysFunc.AppendBasicBlock("ent_loop_header");
        builder.BuildCondBr(hasEntities, entLoopHeaderBB, nextArchBB);

        // entLoopHeaderBB: setup row = 0
        builder.PositionAtEnd(entLoopHeaderBB);
        var rowAlloca = builder.BuildAlloca(context.Int32Type, "row");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), rowAlloca);

        var entLoopCondBB = sysFunc.AppendBasicBlock("ent_loop_cond");
        var entLoopBodyBB = sysFunc.AppendBasicBlock("ent_loop_body");

        builder.BuildBr(entLoopCondBB);

        // ent_loop_cond: while (row < arch.count)
        builder.PositionAtEnd(entLoopCondBB);
        var curRow = builder.BuildLoad2(context.Int32Type, rowAlloca, "cur_row");
        var hasMoreRows = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curRow, archCount, "has_more_rows");
        builder.BuildCondBr(hasMoreRows, entLoopBodyBB, nextArchBB);

        // ent_loop_body
        builder.PositionAtEnd(entLoopBodyBB);
        var colsArrGEP = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 4, "cols_arr");

        var locals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
        var varTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        locals["world"] = worldAlloca;
        varTypes["world"] = "World";

        foreach (var qp in sys.QueryParams)
        {
            varTypes[qp.Name] = qp.TypeName;
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                int compId = ecs.GetComponentId(qp.TypeName);
                var compStructType = ecs.GetComponentStructType(qp.TypeName);
                var colSlot = builder.BuildInBoundsGEP2(ecs.GetColumnsArrayType(), colsArrGEP, new[]
                {
                    LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)compId)
                }, $"{qp.Name}_col_slot");
                var colRaw = builder.BuildLoad2(i8PtrType, colSlot, $"{qp.Name}_raw");
                var colTyped = builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(compStructType, 0), $"{qp.Name}_col");
                var elemPtr = builder.BuildInBoundsGEP2(compStructType, colTyped, new[] { curRow }, $"{qp.Name}_elem");
                locals[qp.Name] = elemPtr;
            }
            else if (_typeChecker.Resources.ContainsKey(qp.TypeName))
            {
                int resOffset = ecs.GetResourceOffset(qp.TypeName);
                var resSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)resOffset, $"{qp.Name}_res_slot");
                locals[qp.Name] = resSlot;
            }
            else if (qp.TypeName is "Entity" or "entity")
            {
                var i32PtrType = LLVMTypeRef.CreatePointer(context.Int32Type, 0);
                var entArrSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 3, $"{qp.Name}_arr_slot");
                var entArrRaw = builder.BuildLoad2(i8PtrType, entArrSlot, $"{qp.Name}_arr_raw");
                var entArrTyped = builder.BuildBitCast(entArrRaw, i32PtrType, $"{qp.Name}_arr_typed");
                var entElemPtr = builder.BuildInBoundsGEP2(context.Int32Type, entArrTyped, new[] { curRow }, $"{qp.Name}_elem_ptr");
                var curEntityVal = builder.BuildLoad2(context.Int32Type, entElemPtr, $"{qp.Name}_val");
                var entAlloca = CreateEntryBlockAlloca(context, sysFunc, context.Int32Type, $"{qp.Name}_alloca");
                builder.BuildStore(curEntityVal, entAlloca);
                locals[qp.Name] = entAlloca;
            }
            else if (qp.TypeName == "Commands")
            {
                locals[qp.Name] = worldAlloca;
            }
        }

        // Compile statements inside system body
        CompileBlock(context, module, builder, sysFunc, sys.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

        // Advance row: row++
        var nextRow = builder.BuildAdd(curRow, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_row");
        builder.BuildStore(nextRow, rowAlloca);
        builder.BuildBr(entLoopCondBB);

        // next_arch: arch_idx++
        builder.PositionAtEnd(nextArchBB);
        var nextArchIdx = builder.BuildAdd(curArchIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_arch_idx");
        builder.BuildStore(nextArchIdx, archIdxAlloca);
        builder.BuildBr(archCondBB);

        // sys_exit
        builder.PositionAtEnd(sysExitBB);
        builder.BuildRetVoid();

        // Generate Win32 Threadpool callback wrapper:
        // VOID CALLBACK job_{sys.Name}(PTP_CALLBACK_INSTANCE Instance, PVOID Context, PTP_WORK Work)
        var jobCallbackType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType, i8PtrType }, false);
        var jobFunc = module.AddFunction($"job_{sys.Name}", jobCallbackType);
        var jobBB = jobFunc.AppendBasicBlock("entry");
        var jobBuilder = context.CreateBuilder();
        jobBuilder.PositionAtEnd(jobBB);
        var contextArg = jobFunc.GetParam(1); // Context is pointer to world
        var worldArgTyped = jobBuilder.BuildBitCast(contextArg, worldPtrType, "world_typed");
        jobBuilder.BuildCall2(sysFuncType, sysFunc, new[] { worldArgTyped });
        jobBuilder.BuildRetVoid();

        builder.CurrentDebugLocation = default;
        _currentSubprogram = null;
    }

    private unsafe void CompilePipeline(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        PipelineDeclaration pipe,
        EcsRuntimeEmitter ecs)
    {
        builder.CurrentDebugLocation = default;
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var pipeFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var pipeFunc = module.AddFunction($"pipeline_{pipe.Name}", pipeFuncType);
        var worldParam = pipeFunc.GetParam(0);
        worldParam.Name = "world";

        if (_diBuilder.HasValue)
        {
            var subroutineType = _diBuilder.Value.CreateSubroutineType(_diFile, Array.Empty<LLVMMetadataRef>(), LLVMDIFlags.LLVMDIFlagZero);
            uint line = (uint)Math.Max(1, pipe.Span.Line);
            var subprogram = _diBuilder.Value.CreateFunction(
                _diFile,
                pipe.Name,
                pipe.Name,
                _diFile,
                line,
                subroutineType,
                0,
                1,
                line,
                LLVMDIFlags.LLVMDIFlagZero,
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0);
            LlvmApi.SetSubprogram(pipeFunc, subprogram);
            _currentSubprogram = subprogram;
            var fnLoc = LlvmApi.DIBuilderCreateDebugLocation(context, line, 1, subprogram, null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, fnLoc);
        }

        var entryBB = pipeFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        // Automatically swap event buffers at start of pipeline execution only if no explicit swap_events is in the pipeline
        bool hasExplicitSwap = pipe.Stages.Any(s => s.Actions.Any(a => a is SwapEventsAction));
        var swapFuncInit = module.GetNamedFunction("world_swap_events");
        if (!hasExplicitSwap && swapFuncInit.Handle != IntPtr.Zero)
        {
            var swapFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
            builder.BuildCall2(swapFuncType, swapFuncInit, new[] { worldParam });
        }

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        foreach (var stage in pipe.Stages)
        {
            foreach (var action in stage.Actions)
            {
                SetDebugLocation(context, builder, action.Span);
                if (action is SystemCallAction call)
                {
                    var sysFunc = module.GetNamedFunction($"system_{call.SystemName}");
                    if (sysFunc.Handle == IntPtr.Zero && call.SystemName.Contains("<"))
                    {
                        sysFunc = module.GetNamedFunction($"system_{TypeSymbol.ToMonomorphizedIdentifier(call.SystemName)}");
                    }
                    if (sysFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                    }
                }
                else if (action is ParallelAction par)
                {
                    CompileParallelBatch(context, module, builder, par.Systems, worldParam, worldPtrType, i8PtrType);
                }
                else if (action is ParallelAutoBlockNode autoBlock)
                {
                    CompileParallelAutoBlock(context, module, builder, autoBlock, worldParam, worldPtrType, i8PtrType);
                }
                else if (action is SortHierarchyAction)
                {
                    var sortFunc = module.GetNamedFunction("world_sort_hierarchy");
                    if (sortFunc.Handle != IntPtr.Zero)
                    {
                        var sortFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(sortFuncType, sortFunc, new[] { worldParam });
                    }
                }
                else if (action is SwapEventsAction)
                {
                    var swapFunc = module.GetNamedFunction("world_swap_events");
                    if (swapFunc.Handle != IntPtr.Zero)
                    {
                        var swapFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(swapFuncType, swapFunc, new[] { worldParam });
                    }
                }
                else if (action is ApplyCommandsAction)
                {
                    var applyFunc = module.GetNamedFunction("world_apply_commands");
                    if (applyFunc.Handle != IntPtr.Zero)
                    {
                        var applyFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(applyFuncType, applyFunc, new[] { worldParam });
                    }
                }
            }

            // Automatically apply any remaining deferred commands at stage boundary
            var stageApplyFunc = module.GetNamedFunction("world_apply_commands");
            if (stageApplyFunc.Handle != IntPtr.Zero)
            {
                var applyFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                builder.BuildCall2(applyFuncType, stageApplyFunc, new[] { worldParam });
            }
        }

        builder.BuildRetVoid();

        builder.CurrentDebugLocation = default;
        _currentSubprogram = null;
    }


}
