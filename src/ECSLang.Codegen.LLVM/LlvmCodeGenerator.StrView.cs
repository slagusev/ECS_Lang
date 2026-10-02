using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private static LLVMTypeRef GetStrViewStructType(LLVMContextRef context)
    {
        return context.GetStructType(new[] { LLVMTypeRef.CreatePointer(context.Int8Type, 0), context.Int32Type }, false);
    }

    private unsafe LLVMValueRef EmitViewSubstring(
        LLVMContextRef context,
        LLVMBuilderRef builder,
        LLVMValueRef basePtr,
        LLVMValueRef baseLen,
        ExpressionNode startExpr,
        ExpressionNode lenExpr,
        LLVMModuleRef module,
        LLVMValueRef function,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var startArg = CompileExpression(context, module, builder, function, startExpr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        var lenArg = CompileExpression(context, module, builder, function, lenExpr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

        var start32 = CoerceValue(builder, context, startArg, context.Int32Type);
        var len32 = CoerceValue(builder, context, lenArg, context.Int32Type);

        var zero32 = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

        var baseLen64 = baseLen.TypeOf == context.Int64Type ? baseLen : builder.BuildZExt(baseLen, context.Int64Type, "blen64");

        // Clamp start: start < 0 -> 0; start > baseLen -> baseLen
        var isStartNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, start32, zero32, "start_neg");
        var safeStart32 = builder.BuildSelect(isStartNeg, zero32, start32, "safe_start32");
        var start64 = builder.BuildZExt(safeStart32, context.Int64Type, "start64");

        var isStartBeyond = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, start64, baseLen64, "start_beyond");
        var clampedStart = builder.BuildSelect(isStartBeyond, baseLen64, start64, "clamped_start");

        // Remaining length: baseLen - clampedStart
        var remaining = builder.BuildSub(baseLen64, clampedStart, "rem_len");

        // Clamp length: len < 0 -> 0; len > remaining -> remaining
        var isLenNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, len32, zero32, "len_neg");
        var safeLen32 = builder.BuildSelect(isLenNeg, zero32, len32, "safe_len32");
        var reqLen64 = builder.BuildZExt(safeLen32, context.Int64Type, "req_len64");

        var isReqBeyond = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, reqLen64, remaining, "req_beyond");
        var clampedLen64 = builder.BuildSelect(isReqBeyond, remaining, reqLen64, "clamped_len64");
        var clampedLen32 = builder.BuildTrunc(clampedLen64, context.Int32Type, "clamped_len32");

        // Pointer into base buffer
        var subPtr = builder.BuildInBoundsGEP2(context.Int8Type, basePtr, new[] { clampedStart }, "view_sub_ptr");

        // Create struct { i8* subPtr, i32 clampedLen32 }
        var viewType = GetStrViewStructType(context);
        var undef = LLVMValueRef.CreateConstNull(viewType);
        var v1 = builder.BuildInsertValue(undef, subPtr, 0, "view_p");
        var v2 = builder.BuildInsertValue(v1, clampedLen32, 1, "view_s");
        return v2;
    }

    private unsafe LLVMValueRef GetOrCreateStrViewContainsFunction(LLVMContextRef context, LLVMModuleRef module)
    {
        var fn = module.GetNamedFunction("rt_str_view_contains");
        if (fn.Handle != IntPtr.Zero) return fn;

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, context.Int32Type, i8PtrType, context.Int32Type }, false);
        fn = module.AddFunction("rt_str_view_contains", fnType);

        var entryBB = fn.AppendBasicBlock("entry");
        var checkEmptyBB = fn.AppendBasicBlock("check_empty");
        var loopInitBB = fn.AppendBasicBlock("loop_init");
        var loopCondBB = fn.AppendBasicBlock("loop_cond");
        var loopBodyBB = fn.AppendBasicBlock("loop_body");
        var loopIncBB = fn.AppendBasicBlock("loop_inc");
        var retTrueBB = fn.AppendBasicBlock("ret_true");
        var retFalseBB = fn.AppendBasicBlock("ret_false");

        var builder = context.CreateBuilder();
        builder.PositionAtEnd(entryBB);

        var hPtr = fn.GetParam(0);
        var hLen = fn.GetParam(1);
        var nPtr = fn.GetParam(2);
        var nLen = fn.GetParam(3);

        var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
        var isNeedleEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, nLen, zero, "is_n_empty");
        builder.BuildCondBr(isNeedleEmpty, retTrueBB, checkEmptyBB);

        builder.PositionAtEnd(checkEmptyBB);
        var isNeedleLonger = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, nLen, hLen, "is_n_longer");
        builder.BuildCondBr(isNeedleLonger, retFalseBB, loopInitBB);

        builder.PositionAtEnd(loopInitBB);
        var maxStart = builder.BuildSub(hLen, nLen, "max_start");
        var iAlloca = builder.BuildAlloca(context.Int32Type, "i");
        builder.BuildStore(zero, iAlloca);
        builder.BuildBr(loopCondBB);

        builder.PositionAtEnd(loopCondBB);
        var curI = builder.BuildLoad2(context.Int32Type, iAlloca, "cur_i");
        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, curI, maxStart, "has_more");
        builder.BuildCondBr(hasMore, loopBodyBB, retFalseBB);

        builder.PositionAtEnd(loopBodyBB);
        var curI64 = builder.BuildZExt(curI, context.Int64Type, "cur_i64");
        var curPtr = builder.BuildInBoundsGEP2(context.Int8Type, hPtr, new[] { curI64 }, "cur_ptr");
        var nLen64 = builder.BuildZExt(nLen, context.Int64Type, "n_len64");

        var strncmpFunc = GetOrDeclareCrtFunc(module, "strncmp", context.Int32Type, new[] { i8PtrType, i8PtrType, context.Int64Type });
        var strncmpType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strncmpFunc);
        var cmp = builder.BuildCall2(strncmpType, strncmpFunc, new[] { curPtr, nPtr, nLen64 }, "cmp_res");
        var isMatch = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, cmp, zero, "is_match");
        builder.BuildCondBr(isMatch, retTrueBB, loopIncBB);

        builder.PositionAtEnd(loopIncBB);
        var nextI = builder.BuildAdd(curI, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_i");
        builder.BuildStore(nextI, iAlloca);
        builder.BuildBr(loopCondBB);

        builder.PositionAtEnd(retTrueBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int1Type, 1));

        builder.PositionAtEnd(retFalseBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int1Type, 0));

        return fn;
    }

    private unsafe LLVMValueRef GetOrCreateStrViewStartsWithFunction(LLVMContextRef context, LLVMModuleRef module)
    {
        var fn = module.GetNamedFunction("rt_str_view_starts_with");
        if (fn.Handle != IntPtr.Zero) return fn;

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, context.Int32Type, i8PtrType, context.Int32Type }, false);
        fn = module.AddFunction("rt_str_view_starts_with", fnType);

        var entryBB = fn.AppendBasicBlock("entry");
        var doCmpBB = fn.AppendBasicBlock("do_cmp");
        var retFalseBB = fn.AppendBasicBlock("ret_false");

        var builder = context.CreateBuilder();
        builder.PositionAtEnd(entryBB);

        var hPtr = fn.GetParam(0);
        var hLen = fn.GetParam(1);
        var nPtr = fn.GetParam(2);
        var nLen = fn.GetParam(3);

        var isNeedleLonger = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, nLen, hLen, "is_n_longer");
        builder.BuildCondBr(isNeedleLonger, retFalseBB, doCmpBB);

        builder.PositionAtEnd(doCmpBB);
        var nLen64 = builder.BuildZExt(nLen, context.Int64Type, "n_len64");
        var strncmpFunc = GetOrDeclareCrtFunc(module, "strncmp", context.Int32Type, new[] { i8PtrType, i8PtrType, context.Int64Type });
        var strncmpType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strncmpFunc);
        var cmp = builder.BuildCall2(strncmpType, strncmpFunc, new[] { hPtr, nPtr, nLen64 }, "cmp_res");
        var isMatch = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, cmp, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_match");
        builder.BuildRet(isMatch);

        builder.PositionAtEnd(retFalseBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int1Type, 0));

        return fn;
    }

    private unsafe bool TryGenerateStrViewMethodCall(
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
                result = builder.BuildExtractValue(targetVal, 1, "view_len");
                return true;
            }

            case "view_substring" or "substring":
            {
                var basePtr = builder.BuildExtractValue(targetVal, 0, "view_ptr");
                var baseLen = builder.BuildExtractValue(targetVal, 1, "view_len");
                result = EmitViewSubstring(context, builder, basePtr, baseLen, methodCall.Arguments[0], methodCall.Arguments[1], module, function, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                return true;
            }

            case "contains":
            {
                var hPtr = builder.BuildExtractValue(targetVal, 0, "h_ptr");
                var hLen = builder.BuildExtractValue(targetVal, 1, "h_len");

                var argVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var argType = _typeChecker.GetNodeType(methodCall.Arguments[0]);

                LLVMValueRef nPtr, nLen;
                if (argType == TypeSymbol.StrView)
                {
                    nPtr = builder.BuildExtractValue(argVal, 0, "n_ptr");
                    nLen = builder.BuildExtractValue(argVal, 1, "n_len");
                }
                else
                {
                    nPtr = argVal;
                    var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                    var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                    var nLen64 = builder.BuildCall2(strlenType, strlenFunc, new[] { nPtr }, "n_len64");
                    nLen = builder.BuildTrunc(nLen64, context.Int32Type, "n_len32");
                }

                var containsFn = GetOrCreateStrViewContainsFunction(context, module);
                var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFn);
                result = builder.BuildCall2(fnType, containsFn, new[] { hPtr, hLen, nPtr, nLen }, "view_contains_res");
                return true;
            }

            case "starts_with":
            {
                var hPtr = builder.BuildExtractValue(targetVal, 0, "h_ptr");
                var hLen = builder.BuildExtractValue(targetVal, 1, "h_len");

                var argVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var argType = _typeChecker.GetNodeType(methodCall.Arguments[0]);

                LLVMValueRef nPtr, nLen;
                if (argType == TypeSymbol.StrView)
                {
                    nPtr = builder.BuildExtractValue(argVal, 0, "n_ptr");
                    nLen = builder.BuildExtractValue(argVal, 1, "n_len");
                }
                else
                {
                    nPtr = argVal;
                    var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                    var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);
                    var nLen64 = builder.BuildCall2(strlenType, strlenFunc, new[] { nPtr }, "n_len64");
                    nLen = builder.BuildTrunc(nLen64, context.Int32Type, "n_len32");
                }

                var startsWithFn = GetOrCreateStrViewStartsWithFunction(context, module);
                var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(startsWithFn);
                result = builder.BuildCall2(fnType, startsWithFn, new[] { hPtr, hLen, nPtr, nLen }, "view_starts_with_res");
                return true;
            }

            case "to_string":
            {
                var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                result = EmitToString(context, module, builder, targetVal, TypeSymbol.StrView, i8PtrType, ecs, worldPtr);
                return true;
            }

            default:
                result = default;
                return false;
        }
    }
}
