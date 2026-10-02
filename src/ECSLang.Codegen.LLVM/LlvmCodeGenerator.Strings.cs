using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe bool TryGenerateStringMethodCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        MethodCallExpression methodCall,
        LLVMValueRef targetVal,
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

        switch (methodCall.MethodName)
        {
            case "len" or "length":
            {
                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "str_len_64");
                result = builder.BuildTrunc(len64, context.Int32Type, "str_len");
                return true;
            }

            case "contains":
            {
                var subVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var strstrFunc = GetOrDeclareCrtFunc(module, "strstr", i8PtrType, new[] { i8PtrType, i8PtrType });
                var strstrType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strstrFunc);

                var foundPtr = builder.BuildCall2(strstrType, strstrFunc, new[] { targetVal, subVal }, "strstr_res");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, foundPtr, nullPtr, "contains_res");
                return true;
            }

            case "starts_with":
            {
                var prefixVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                var strncmpFunc = GetOrDeclareCrtFunc(module, "strncmp", context.Int32Type, new[] { i8PtrType, i8PtrType, context.Int64Type });
                var strncmpType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strncmpFunc);

                var prefixLen = builder.BuildCall2(strlenType, strlenFunc, new[] { prefixVal }, "prefix_len");
                var cmpRes = builder.BuildCall2(strncmpType, strncmpFunc, new[] { targetVal, prefixVal, prefixLen }, "strncmp_res");
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, cmpRes, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "starts_with_res");
                return true;
            }

            case "ends_with":
            {
                var suffixVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);

                var sLen = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "s_len");
                var sufLen = builder.BuildCall2(strlenType, strlenFunc, new[] { suffixVal }, "suf_len");

                var canFit = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE, sLen, sufLen, "ends_can_fit");

                var fitBB = function.AppendBasicBlock("ends_fit");
                var falseBB = function.AppendBasicBlock("ends_false");
                var mergeBB = function.AppendBasicBlock("ends_merge");

                var resAlloca = CreateEntryBlockAlloca(context, function, context.Int1Type, "ends_res_alloca");
                builder.BuildCondBr(canFit, fitBB, falseBB);

                builder.PositionAtEnd(fitBB);
                var offset = builder.BuildSub(sLen, sufLen, "suf_offset");
                var tailPtr = builder.BuildInBoundsGEP2(context.Int8Type, targetVal, new[] { offset }, "tail_ptr");
                var strcmpFunc = GetOrDeclareCrtFunc(module, "strcmp", context.Int32Type, new[] { i8PtrType, i8PtrType });
                var strcmpType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strcmpFunc);
                var cmp = builder.BuildCall2(strcmpType, strcmpFunc, new[] { tailPtr, suffixVal }, "strcmp_res");
                var isEq = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, cmp, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_ends_eq");
                builder.BuildStore(isEq, resAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(falseBB);
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 0), resAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(context.Int1Type, resAlloca, "ends_with_res");
                return true;
            }

            case "index_of":
            {
                var subVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var searchStart = targetVal;
                if (methodCall.Arguments.Count == 2)
                {
                    var startArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var start32 = CoerceValue(builder, context, startArg, context.Int32Type);
                    var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    var isNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, start32, zero, "is_neg");
                    var clampedOffset = builder.BuildSelect(isNeg, zero, start32, "clamped_offset");
                    searchStart = builder.BuildInBoundsGEP2(context.Int8Type, targetVal, new[] { clampedOffset }, "search_start");
                }

                var strstrFunc = GetOrDeclareCrtFunc(module, "strstr", i8PtrType, new[] { i8PtrType, i8PtrType });
                var strstrType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strstrFunc);

                var foundPtr = builder.BuildCall2(strstrType, strstrFunc, new[] { searchStart, subVal }, "strstr_idx");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isFound = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, foundPtr, nullPtr, "is_found");

                var foundInt = builder.BuildPtrToInt(foundPtr, context.Int64Type, "found_int");
                var sInt = builder.BuildPtrToInt(targetVal, context.Int64Type, "s_int");
                var diff64 = builder.BuildSub(foundInt, sInt, "diff_64");
                var diff32 = builder.BuildTrunc(diff64, context.Int32Type, "diff_32");

                var minusOne = LLVMValueRef.CreateConstInt(context.Int32Type, unchecked((ulong)-1), true);
                result = builder.BuildSelect(isFound, diff32, minusOne, "index_of_res");
                return true;
            }

            case "view_substring":
            {
                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                var sLen = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "sub_slen");
                result = EmitViewSubstring(context, builder, targetVal, sLen, methodCall.Arguments[0], methodCall.Arguments[1], module, function, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                return true;
            }

            case "substring":
            {
                var startArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var lenArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var start32 = CoerceValue(builder, context, startArg, context.Int32Type);
                var len32 = CoerceValue(builder, context, lenArg, context.Int32Type);

                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                var sLen = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "sub_slen");

                // Clamp start: if start < 0 -> 0; if start > sLen -> sLen
                var zero32 = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                var zero64 = LLVMValueRef.CreateConstInt(context.Int64Type, 0);
                var isStartNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, start32, zero32, "is_start_neg");
                var safeStart32 = builder.BuildSelect(isStartNeg, zero32, start32, "safe_start_32");
                var start64 = builder.BuildZExt(safeStart32, context.Int64Type, "start_64");

                var isStartBeyond = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, start64, sLen, "is_start_beyond");
                var clampedStart = builder.BuildSelect(isStartBeyond, sLen, start64, "clamped_start");

                // Remaining available length from clampedStart
                var remaining = builder.BuildSub(sLen, clampedStart, "rem_len");

                // Clamp length: if len < 0 -> 0; if len > remaining -> remaining
                var isLenNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, len32, zero32, "is_len_neg");
                var safeLen32 = builder.BuildSelect(isLenNeg, zero32, len32, "safe_len_32");
                var reqLen64 = builder.BuildZExt(safeLen32, context.Int64Type, "req_len_64");

                var isReqBeyond = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, reqLen64, remaining, "is_req_beyond");
                var clampedLen = builder.BuildSelect(isReqBeyond, remaining, reqLen64, "clamped_len");

                // Allocate clampedLen + 1 bytes in String Arena (or malloc fallback)
                var allocSize = builder.BuildAdd(clampedLen, LLVMValueRef.CreateConstInt(context.Int64Type, 1), "sub_alloc_size");

                LLVMValueRef newBuf;
                var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                if (_arenaEmitter != null && ecs != null && worldPtr.Handle != IntPtr.Zero)
                {
                    var arenaAlloc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
                    var allocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(arenaAlloc);
                    newBuf = builder.BuildCall2(allocType, arenaAlloc, new[] { worldPtr, allocSize }, "sub_arena_buf");
                }
                else
                {
                    var mallocFunc = GetOrDeclareCrtFunc(module, "malloc", i8PtrType, new[] { context.Int64Type });
                    var mallocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(mallocFunc);
                    newBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { allocSize }, "sub_malloc_buf");
                }

                // memcpy(newBuf, targetVal + clampedStart, clampedLen)
                var srcPtr = builder.BuildInBoundsGEP2(context.Int8Type, targetVal, new[] { clampedStart }, "sub_src_ptr");
                var memcpyFunc = GetOrDeclareCrtFunc(module, "memcpy", i8PtrType, new[] { i8PtrType, i8PtrType, context.Int64Type });
                var memcpyType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(memcpyFunc);
                builder.BuildCall2(memcpyType, memcpyFunc, new[] { newBuf, srcPtr, clampedLen }, "");

                // Null terminator: newBuf[clampedLen] = 0
                var termPtr = builder.BuildInBoundsGEP2(context.Int8Type, newBuf, new[] { clampedLen }, "sub_term_ptr");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), termPtr);

                result = newBuf;
                return true;
            }

            default:
                result = default;
                return false;
        }
    }
}
