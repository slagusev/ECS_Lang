using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef CompileArrayLiteralExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        ArrayLiteralExpression arrLit,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var arrTypeSym = _typeChecker.GetNodeType(arrLit);
        if (arrTypeSym.IsDynamicArray)
        {
            arrTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
            var dynElemType = MapType(context, dynElemSym.Name, ecs);
            var dynArrStructType = MapType(context, arrTypeSym.Name, ecs);
            var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_arr");

            int elemCount = arrLit.Elements.Count;
            if (elemCount == 0)
            {
                var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
                var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                builder.BuildStore(dataZero, p0);
                var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                builder.BuildStore(zeroInt, p1);
                var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                builder.BuildStore(zeroInt, p2);
            }
            else
            {
                ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                ulong totalBytes = (ulong)elemCount * elemSize;
                var mallocFunc = module.GetNamedFunction("malloc");
                var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                var rawBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, totalBytes) }, "dyn_buf");
                var typedBuf = builder.BuildBitCast(rawBuf, LLVMTypeRef.CreatePointer(dynElemType, 0), "dyn_buf_typed");

                for (int i = 0; i < elemCount; i++)
                {
                    var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                    var elemGEP = builder.BuildInBoundsGEP2(dynElemType, typedBuf, new[] { idxVal }, $"dyn_elem_{i}");
                    builder.BuildStore(elemVal, elemGEP);
                }

                var countVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)elemCount);
                var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                builder.BuildStore(typedBuf, p0);
                var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                builder.BuildStore(countVal, p1);
                var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                builder.BuildStore(countVal, p2);
            }

            return builder.BuildLoad2(dynArrStructType, dynArrAlloca, "dyn_arr_val");
        }
        else
        {
            var llvmArrType = MapType(context, arrTypeSym.Name, ecs);
            var arrAlloca = CreateEntryBlockAlloca(context, function, llvmArrType, "arr_lit");
            var zeroConst = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
            for (int i = 0; i < arrLit.Elements.Count; i++)
            {
                var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                var elemGEP = builder.BuildInBoundsGEP2(llvmArrType, arrAlloca, new[] { zeroConst, idxVal }, $"arr_elem_{i}");
                builder.BuildStore(elemVal, elemGEP);
            }
            return builder.BuildLoad2(llvmArrType, arrAlloca, "arr_lit_val");
        }
    }

    private unsafe LLVMValueRef CompileIndexExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        IndexExpression idxExpr,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var elemType = MapType(context, _typeChecker.GetNodeType(idxExpr).Name, ecs);
        var indexVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
        var zeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

        var idxTargetType = _typeChecker.GetNodeType(idxExpr.Target);
        if (idxTargetType.IsMap)
        {
            idxTargetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
            var mapKeyVal = CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var mapStructType = MapType(context, idxTargetType.Name, ecs);

            LLVMValueRef mapStructPtr;
            if (idxExpr.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
            {
                mapStructPtr = idPtr;
            }
            else
            {
                var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                builder.BuildStore(tv, tempAlloca);
                mapStructPtr = tempAlloca;
            }

            var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
            var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
            return builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, mapKeyVal }, "map_get_idx");
        }

        if (idxTargetType.IsDynamicArray)
        {
            idxTargetType.TryGetDynamicArrayElement(out var dynElemSym);
            var dynElemType = MapType(context, dynElemSym.Name, ecs);
            var dynArrStructType = MapType(context, idxTargetType.Name, ecs);
            var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

            LLVMValueRef dynStructPtr;
            if (idxExpr.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
            {
                dynStructPtr = idPtr;
            }
            else
            {
                var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                builder.BuildStore(tv, tempAlloca);
                dynStructPtr = tempAlloca;
            }

            var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
            var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "dyn_data_ptr");
            var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { indexVal }, "dyn_elem_gep");
            return builder.BuildLoad2(dynElemType, elemGEP, "dyn_elem_val");
        }

        if (idxExpr.Target is IdentifierExpression idxTargetId && locals.TryGetValue(idxTargetId.Name, out var targetArrPtr))
        {
            var arrTypeName = varTypes[idxTargetId.Name];
            var arrType = MapType(context, arrTypeName, ecs);
            var elemGEP = builder.BuildInBoundsGEP2(arrType, targetArrPtr, new[] { zeroIdx, indexVal }, $"{idxTargetId.Name}_idx_gep");
            return builder.BuildLoad2(elemType, elemGEP, $"{idxTargetId.Name}_elem_val");
        }
        else if (idxExpr.Target is MemberAccessExpression memAccess && memAccess.Target is IdentifierExpression memTargetId && locals.TryGetValue(memTargetId.Name, out var structPtr2))
        {
            if (varTypes.TryGetValue(memTargetId.Name, out var structTypeName))
            {
                var structType = ecs.GetComponentStructType(structTypeName);
                int offset = ecs.GetFieldOffset(structTypeName, memAccess.MemberName);
                var fieldGEP = builder.BuildStructGEP2(structType, structPtr2, (uint)offset, $"{memTargetId.Name}_{memAccess.MemberName}");
                var memType = _typeChecker.GetMemberType(structTypeName, memAccess.MemberName, memAccess.Span);
                var fieldArrType = MapType(context, memType.Name, ecs);
                var elemGEP = builder.BuildInBoundsGEP2(fieldArrType, fieldGEP, new[] { zeroIdx, indexVal }, $"{memAccess.MemberName}_idx_gep");
                return builder.BuildLoad2(elemType, elemGEP, $"{memAccess.MemberName}_elem_val");
            }
        }

        // Fallback: evaluate target expression into temporary
        var targetValExpr = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        var tmpArrAlloca = CreateEntryBlockAlloca(context, function, targetValExpr.TypeOf, "tmp_idx_arr");
        builder.BuildStore(targetValExpr, tmpArrAlloca);
        var fallbackElemGEP = builder.BuildInBoundsGEP2(targetValExpr.TypeOf, tmpArrAlloca, new[] { zeroIdx, indexVal }, "tmp_elem_gep");
        return builder.BuildLoad2(elemType, fallbackElemGEP, "tmp_elem_val");
    }

    private unsafe bool TryGenerateCollectionConstructorCall(
        LLVMContextRef context,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        CallExpression call,
        EcsRuntimeEmitter ecs,
        out LLVMValueRef result)
    {
        if (call.Callee.StartsWith("Vec<") || call.Callee.StartsWith("List<"))
        {
            var dynTypeSym = TypeSymbol.FromName(call.Callee);
            dynTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
            var dynElemType = MapType(context, dynElemSym.Name, ecs);
            var dynArrStructType = MapType(context, dynTypeSym.Name, ecs);
            var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "vec_init");

            var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
            var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

            var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
            builder.BuildStore(dataZero, p0);
            var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
            builder.BuildStore(zeroInt, p1);
            var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
            builder.BuildStore(zeroInt, p2);

            result = builder.BuildLoad2(dynArrStructType, dynArrAlloca, "vec_val");
            return true;
        }

        if (call.Callee.StartsWith("Map<") || call.Callee.StartsWith("HashMap<"))
        {
            var mapTypeSym = TypeSymbol.FromName(call.Callee);
            mapTypeSym.TryGetMapInfo(out var kSym, out var vSym);
            var mapStructType = MapType(context, mapTypeSym.Name, ecs);
            var entryStructType = _mapEmitter!.GetEntryType(kSym.Name, vSym.Name, (t) => MapType(context, t, ecs));
            var mapAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_init");

            var nullEntries = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(entryStructType, 0));
            var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

            var p0 = builder.BuildStructGEP2(mapStructType, mapAlloca, 0, "map_entries_slot");
            builder.BuildStore(nullEntries, p0);
            var p1 = builder.BuildStructGEP2(mapStructType, mapAlloca, 1, "map_count_slot");
            builder.BuildStore(zeroInt, p1);
            var p2 = builder.BuildStructGEP2(mapStructType, mapAlloca, 2, "map_cap_slot");
            builder.BuildStore(zeroInt, p2);

            result = builder.BuildLoad2(mapStructType, mapAlloca, "map_val");
            return true;
        }

        result = default;
        return false;
    }

    private unsafe bool TryGenerateCollectionMethodCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        MethodCallExpression methodCall,
        TypeSymbol targetType,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        out LLVMValueRef result)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        if (targetType.IsDynamicArray)
        {
            targetType.TryGetDynamicArrayElement(out var dynElemSym);
            var dynElemType = MapType(context, dynElemSym.Name, ecs);
            var dynArrStructType = MapType(context, targetType.Name, ecs);
            var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

            LLVMValueRef dynStructPtr;
            if (methodCall.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
            {
                dynStructPtr = idPtr;
            }
            else
            {
                var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                builder.BuildStore(tv, tempAlloca);
                dynStructPtr = tempAlloca;
            }

            if (methodCall.MethodName is "len" or "length" or "capacity")
            {
                uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                var fieldSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, fieldIdx, $"dyn_{methodCall.MethodName}_slot");
                result = builder.BuildLoad2(context.Int32Type, fieldSlot, $"dyn_{methodCall.MethodName}_val");
                return true;
            }

            if (methodCall.MethodName == "clear")
            {
                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), lenSlot);
                result = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                return true;
            }

            if (methodCall.MethodName == "push")
            {
                var itemVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var capSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 2, "dyn_cap_slot");

                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var curCap = builder.BuildLoad2(context.Int32Type, capSlot, "cur_cap");

                var isFull = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curLen, curCap, "is_full");

                var growBB = function.AppendBasicBlock("dyn_grow");
                var insertBB = function.AppendBasicBlock("dyn_insert");

                builder.BuildCondBr(isFull, growBB, insertBB);

                // growBB:
                builder.PositionAtEnd(growBB);
                var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_cap_zero");
                var doubleCap = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 2), "double_cap");
                var newCap = builder.BuildSelect(isCapZero, LLVMValueRef.CreateConstInt(context.Int32Type, 4), doubleCap, "new_cap");
                builder.BuildStore(newCap, capSlot);

                // realloc(data, newCap * sizeof(elem))
                ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                var newCap64 = builder.BuildZExt(newCap, context.Int64Type, "new_cap_64");
                var newSizeBytes = builder.BuildMul(newCap64, LLVMValueRef.CreateConstInt(context.Int64Type, elemSize), "new_size_bytes");

                var oldData = builder.BuildLoad2(elemPtrType, dataSlot, "old_data");
                var oldDataRaw = builder.BuildBitCast(oldData, i8PtrType, "old_data_raw");

                var reallocFunc = module.GetNamedFunction("realloc");
                var reallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int64Type }, false);
                var newDataRaw = builder.BuildCall2(reallocType, reallocFunc, new[] { oldDataRaw, newSizeBytes }, "new_data_raw");
                var newDataTyped = builder.BuildBitCast(newDataRaw, elemPtrType, "new_data_typed");
                builder.BuildStore(newDataTyped, dataSlot);

                builder.BuildBr(insertBB);

                // insertBB:
                builder.PositionAtEnd(insertBB);
                var curDataAfter = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data_after");
                var curLenAfter = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len_after");
                var insertGEP = builder.BuildInBoundsGEP2(dynElemType, curDataAfter, new[] { curLenAfter }, "insert_elem_gep");
                builder.BuildStore(itemVal, insertGEP);

                var nextLen = builder.BuildAdd(curLenAfter, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_len");
                builder.BuildStore(nextLen, lenSlot);

                result = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                return true;
            }

            if (methodCall.MethodName == "pop")
            {
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");

                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var newLen = builder.BuildSub(curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_len");
                builder.BuildStore(newLen, lenSlot);

                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");
                var elemGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { newLen }, "pop_elem_gep");
                result = builder.BuildLoad2(dynElemType, elemGEP, "pop_val");
                return true;
            }

            if (methodCall.MethodName == "get")
            {
                var idxVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                idxVal = EnsureInt32(context, builder, idxVal, "arr_get_idx");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var geZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "ge_zero");
                var ltLen = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "lt_len");
                var inBounds = builder.BuildAnd(geZero, ltLen, "in_bounds");

                var optStructType = context.GetStructType(new[] { context.Int32Type, dynElemType }, false);
                var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_arr_get_res");

                var inBB = function.AppendBasicBlock("arr_get_in");
                var outBB = function.AppendBasicBlock("arr_get_out");
                var mergeBB = function.AppendBasicBlock("arr_get_merge");

                builder.BuildCondBr(inBounds, inBB, outBB);

                builder.PositionAtEnd(inBB);
                var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { idxVal }, "elem_gep");
                var elemVal = builder.BuildLoad2(dynElemType, elemGEP, "elem_val");
                var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                builder.BuildStore(elemVal, val1);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(outBB);
                var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                builder.BuildStore(LLVMValueRef.CreateConstNull(dynElemType), val0);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(optStructType, retAlloca, "opt_arr_val");
                return true;
            }

            if (methodCall.MethodName == "for_each")
            {
                var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var fnRaw = builder.BuildExtractValue(closureVal, 0, "fe_fn_raw");
                var envPtr = builder.BuildExtractValue(closureVal, 1, "fe_env_ptr");

                var feFnType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, dynElemType }, false);
                var feTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(feFnType, 0), "fe_typed_fn");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "fe_idx");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                var condBB = function.AppendBasicBlock("fe_cond");
                var bodyBB = function.AppendBasicBlock("fe_body");
                var exitBB = function.AppendBasicBlock("fe_exit");

                builder.BuildBr(condBB);

                builder.PositionAtEnd(condBB);
                var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "fe_i");
                var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "fe_has_more");
                builder.BuildCondBr(hasMore, bodyBB, exitBB);

                builder.PositionAtEnd(bodyBB);
                var elemGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "fe_elem_gep");
                var elemVal = builder.BuildLoad2(dynElemType, elemGEP, "fe_elem_val");
                builder.BuildCall2(feFnType, feTypedFn, new[] { envPtr, elemVal }, "");
                var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "fe_next_i");
                builder.BuildStore(nextIdx, idxAlloca);
                builder.BuildBr(condBB);

                builder.PositionAtEnd(exitBB);
                result = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                return true;
            }

            if (methodCall.MethodName == "map")
            {
                var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var fnRaw = builder.BuildExtractValue(closureVal, 0, "map_fn_raw");
                var envPtr = builder.BuildExtractValue(closureVal, 1, "map_env_ptr");

                var retTypeSym = _typeChecker.GetNodeType(methodCall);
                retTypeSym.TryGetDynamicArrayElement(out var resElemSym);
                var resElemType = MapType(context, resElemSym.Name, ecs);
                var resArrStructType = MapType(context, retTypeSym.Name, ecs);
                var resElemPtrType = LLVMTypeRef.CreatePointer(resElemType, 0);

                var mapFnType = LLVMTypeRef.CreateFunction(resElemType, new[] { i8PtrType, dynElemType }, false);
                var mapTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(mapFnType, 0), "map_typed_fn");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var resAlloca = CreateEntryBlockAlloca(context, function, resArrStructType, "map_res_arr");
                var rDataSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 0, "r_data");
                var rLenSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 1, "r_len");
                var rCapSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 2, "r_cap");

                ulong resElemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, resElemType));
                var curLen64 = builder.BuildZExt(curLen, context.Int64Type, "cur_len64");
                var sizeBytes = builder.BuildMul(curLen64, LLVMValueRef.CreateConstInt(context.Int64Type, resElemSize), "size_bytes");
                var isZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_zero");
                var allocBytes = builder.BuildSelect(isZero, LLVMValueRef.CreateConstInt(context.Int64Type, 16), sizeBytes, "alloc_bytes");

                var mallocFunc = module.GetNamedFunction("malloc");
                var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                var newMem = builder.BuildCall2(mallocType, mallocFunc, new[] { allocBytes }, "map_new_mem");
                var newTypedMem = builder.BuildBitCast(newMem, resElemPtrType, "new_typed_mem");

                builder.BuildStore(newTypedMem, rDataSlot);
                builder.BuildStore(curLen, rLenSlot);
                builder.BuildStore(curLen, rCapSlot);

                var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "map_idx");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                var condBB = function.AppendBasicBlock("map_cond");
                var bodyBB = function.AppendBasicBlock("map_body");
                var exitBB = function.AppendBasicBlock("map_exit");

                builder.BuildBr(condBB);

                builder.PositionAtEnd(condBB);
                var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "map_i");
                var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "map_has_more");
                builder.BuildCondBr(hasMore, bodyBB, exitBB);

                builder.PositionAtEnd(bodyBB);
                var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                var mappedVal = builder.BuildCall2(mapFnType, mapTypedFn, new[] { envPtr, inVal }, "mapped_val");

                var outGEP = builder.BuildInBoundsGEP2(resElemType, newTypedMem, new[] { idxVal }, "out_gep");
                builder.BuildStore(mappedVal, outGEP);

                var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "map_next_i");
                builder.BuildStore(nextIdx, idxAlloca);
                builder.BuildBr(condBB);

                builder.PositionAtEnd(exitBB);
                result = builder.BuildLoad2(resArrStructType, resAlloca, "map_res");
                return true;
            }

            if (methodCall.MethodName == "filter")
            {
                var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var fnRaw = builder.BuildExtractValue(closureVal, 0, "filter_fn_raw");
                var envPtr = builder.BuildExtractValue(closureVal, 1, "filter_env_ptr");

                var filterFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                var filterTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(filterFnType, 0), "filter_typed_fn");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var resAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "filter_res_arr");
                var rDataSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 0, "r_data");
                var rLenSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 1, "r_len");
                var rCapSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 2, "r_cap");

                ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                var curLen64 = builder.BuildZExt(curLen, context.Int64Type, "cur_len64");
                var sizeBytes = builder.BuildMul(curLen64, LLVMValueRef.CreateConstInt(context.Int64Type, elemSize), "size_bytes");
                var isZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_zero");
                var allocBytes = builder.BuildSelect(isZero, LLVMValueRef.CreateConstInt(context.Int64Type, 16), sizeBytes, "alloc_bytes");

                var mallocFunc = module.GetNamedFunction("malloc");
                var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                var newMem = builder.BuildCall2(mallocType, mallocFunc, new[] { allocBytes }, "filter_mem");
                var newTypedMem = builder.BuildBitCast(newMem, elemPtrType, "filter_typed_mem");

                builder.BuildStore(newTypedMem, rDataSlot);
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), rLenSlot);
                builder.BuildStore(curLen, rCapSlot);

                var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "filter_idx");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                var condBB = function.AppendBasicBlock("filter_cond");
                var bodyBB = function.AppendBasicBlock("filter_body");
                var checkBB = function.AppendBasicBlock("filter_check");
                var nextBB = function.AppendBasicBlock("filter_next");
                var exitBB = function.AppendBasicBlock("filter_exit");

                builder.BuildBr(condBB);

                builder.PositionAtEnd(condBB);
                var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "filter_i");
                var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "filter_has_more");
                builder.BuildCondBr(hasMore, bodyBB, exitBB);

                builder.PositionAtEnd(bodyBB);
                var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                var pass = builder.BuildCall2(filterFnType, filterTypedFn, new[] { envPtr, inVal }, "filter_pass");
                builder.BuildCondBr(pass, checkBB, nextBB);

                builder.PositionAtEnd(checkBB);
                var curOutLen = builder.BuildLoad2(context.Int32Type, rLenSlot, "out_len");
                var outGEP = builder.BuildInBoundsGEP2(dynElemType, newTypedMem, new[] { curOutLen }, "out_gep");
                builder.BuildStore(inVal, outGEP);
                var newOutLen = builder.BuildAdd(curOutLen, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_out_len");
                builder.BuildStore(newOutLen, rLenSlot);
                builder.BuildBr(nextBB);

                builder.PositionAtEnd(nextBB);
                var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "filter_next_i");
                builder.BuildStore(nextIdx, idxAlloca);
                builder.BuildBr(condBB);

                builder.PositionAtEnd(exitBB);
                result = builder.BuildLoad2(dynArrStructType, resAlloca, "filter_res");
                return true;
            }

            if (methodCall.MethodName is "any" or "all")
            {
                bool isAny = methodCall.MethodName == "any";
                var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var fnRaw = builder.BuildExtractValue(closureVal, 0, "pred_fn_raw");
                var envPtr = builder.BuildExtractValue(closureVal, 1, "pred_env_ptr");

                var predFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                var predTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(predFnType, 0), "pred_typed_fn");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var retAlloca = CreateEntryBlockAlloca(context, function, context.Int1Type, "pred_res");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, isAny ? 0UL : 1UL), retAlloca);

                var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "pred_idx");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                var condBB = function.AppendBasicBlock("pred_cond");
                var bodyBB = function.AppendBasicBlock("pred_body");
                var earlyBB = function.AppendBasicBlock("pred_early");
                var nextBB = function.AppendBasicBlock("pred_next");
                var exitBB = function.AppendBasicBlock("pred_exit");

                builder.BuildBr(condBB);

                builder.PositionAtEnd(condBB);
                var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "pred_i");
                var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "pred_has_more");
                builder.BuildCondBr(hasMore, bodyBB, exitBB);

                builder.PositionAtEnd(bodyBB);
                var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                var pass = builder.BuildCall2(predFnType, predTypedFn, new[] { envPtr, inVal }, "pred_pass");

                if (isAny)
                {
                    builder.BuildCondBr(pass, earlyBB, nextBB);
                }
                else
                {
                    builder.BuildCondBr(pass, nextBB, earlyBB);
                }

                builder.PositionAtEnd(earlyBB);
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, isAny ? 1UL : 0UL), retAlloca);
                builder.BuildBr(exitBB);

                builder.PositionAtEnd(nextBB);
                var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "pred_next_i");
                builder.BuildStore(nextIdx, idxAlloca);
                builder.BuildBr(condBB);

                builder.PositionAtEnd(exitBB);
                result = builder.BuildLoad2(context.Int1Type, retAlloca, "pred_final");
                return true;
            }

            if (methodCall.MethodName == "find")
            {
                var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var fnRaw = builder.BuildExtractValue(closureVal, 0, "find_fn_raw");
                var envPtr = builder.BuildExtractValue(closureVal, 1, "find_env_ptr");

                var findFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                var findTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(findFnType, 0), "find_typed_fn");

                var optStructType = context.GetStructType(new[] { context.Int32Type, dynElemType }, false);
                var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "find_opt_res");

                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "find_idx");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                var condBB = function.AppendBasicBlock("find_cond");
                var bodyBB = function.AppendBasicBlock("find_body");
                var foundBB = function.AppendBasicBlock("find_found");
                var nextBB = function.AppendBasicBlock("find_next");
                var notFoundBB = function.AppendBasicBlock("find_not_found");
                var exitBB = function.AppendBasicBlock("find_exit");

                builder.BuildBr(condBB);

                builder.PositionAtEnd(condBB);
                var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "find_i");
                var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "find_has_more");
                builder.BuildCondBr(hasMore, bodyBB, notFoundBB);

                builder.PositionAtEnd(bodyBB);
                var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                var pass = builder.BuildCall2(findFnType, findTypedFn, new[] { envPtr, inVal }, "find_pass");
                builder.BuildCondBr(pass, foundBB, nextBB);

                builder.PositionAtEnd(foundBB);
                var tagSlot1 = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag1");
                var valSlot1 = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val1");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot1);
                builder.BuildStore(inVal, valSlot1);
                builder.BuildBr(exitBB);

                builder.PositionAtEnd(nextBB);
                var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "find_next_i");
                builder.BuildStore(nextIdx, idxAlloca);
                builder.BuildBr(condBB);

                builder.PositionAtEnd(notFoundBB);
                var tagSlot0 = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag0");
                var valSlot0 = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val0");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot0);
                builder.BuildStore(LLVMValueRef.CreateConstNull(dynElemType), valSlot0);
                builder.BuildBr(exitBB);

                builder.PositionAtEnd(exitBB);
                result = builder.BuildLoad2(optStructType, optAlloca, "find_opt_val");
                return true;
            }
        }

        if (targetType.IsMap)
        {
            targetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
            var mapStructType = MapType(context, targetType.Name, ecs);

            LLVMValueRef mapStructPtr;
            if (methodCall.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
            {
                mapStructPtr = idPtr;
            }
            else
            {
                var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                builder.BuildStore(tv, tempAlloca);
                mapStructPtr = tempAlloca;
            }

            if (methodCall.MethodName is "len" or "length" or "count" or "capacity")
            {
                uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                var fieldSlot = builder.BuildStructGEP2(mapStructType, mapStructPtr, fieldIdx, $"map_{methodCall.MethodName}_slot");
                result = builder.BuildLoad2(context.Int32Type, fieldSlot, $"map_{methodCall.MethodName}_val");
                return true;
            }

            if (methodCall.MethodName is "insert" or "put")
            {
                var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var vArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var insertFunc = _mapEmitter!.GetOrCreateInsert(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                builder.BuildCall2(ifType, insertFunc, new[] { mapStructPtr, kArg, vArg }, "");
                result = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                return true;
            }

            if (methodCall.MethodName == "get")
            {
                var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                result = builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "map_get_val");
                return true;
            }

            if (methodCall.MethodName is "find" or "get_opt")
            {
                var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);

                var hasIt = builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_has_key");
                var valLlvmType = MapType(context, mapValSym.Name, ecs);
                var optStructType = context.GetStructType(new[] { context.Int32Type, valLlvmType }, false);
                var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_map_find_res");

                var foundBB = function.AppendBasicBlock("map_find_found");
                var notFoundBB = function.AppendBasicBlock("map_find_not_found");
                var mergeBB = function.AppendBasicBlock("map_find_merge");

                builder.BuildCondBr(hasIt, foundBB, notFoundBB);

                builder.PositionAtEnd(foundBB);
                var valRes = builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "found_val");
                var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                builder.BuildStore(valRes, val1);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(notFoundBB);
                var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                builder.BuildStore(LLVMValueRef.CreateConstNull(valLlvmType), val0);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(optStructType, retAlloca, "opt_map_val");
                return true;
            }

            if (methodCall.MethodName is "contains" or "has")
            {
                var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                result = builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_contains_val");
                return true;
            }

            if (methodCall.MethodName == "remove")
            {
                var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var removeFunc = _mapEmitter!.GetOrCreateRemove(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var rfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(removeFunc);
                result = builder.BuildCall2(rfType, removeFunc, new[] { mapStructPtr, kArg }, "map_remove_val");
                return true;
            }

            if (methodCall.MethodName == "clear")
            {
                var clearFunc = _mapEmitter!.GetOrCreateClear(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(clearFunc);
                builder.BuildCall2(cfType, clearFunc, new[] { mapStructPtr }, "");
                result = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                return true;
            }
        }

        result = default;
        return false;
    }
}
