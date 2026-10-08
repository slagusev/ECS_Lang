using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef CompileNoneIdentifier(
        LLVMContextRef context,
        LLVMValueRef function,
        IdentifierExpression ident,
        EcsRuntimeEmitter ecs,
        LLVMBuilderRef builder)
    {
        var optType = _typeChecker.GetNodeType(ident);
        var optLlvmType = MapType(context, optType.Name, ecs);
        var tmpAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "tmp_none");
        var tagSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 0, "opt_tag");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot);
        if (optType.TryGetOptionInfo(out var innerSym))
        {
            var valSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 1, "opt_val");
            builder.BuildStore(LLVMValueRef.CreateConstNull(MapType(context, innerSym.Name, ecs)), valSlot);
        }
        return builder.BuildLoad2(optLlvmType, tmpAlloca, "none_val");
    }

    private unsafe bool TryGenerateTaggedUnionConstructorCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        CallExpression call,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        out LLVMValueRef result)
    {
        if (call.Callee == "Some" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::Some")))
        {
            var optType = _typeChecker.GetNodeType(call);
            var optLlvmType = MapType(context, optType.Name, ecs);
            var tmpAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "tmp_some");
            var tagSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 0, "opt_tag");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot);
            if (optType.TryGetOptionInfo(out var innerSym))
            {
                var valSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 1, "opt_val");
                var valArg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                builder.BuildStore(valArg, valSlot);
            }
            result = builder.BuildLoad2(optLlvmType, tmpAlloca, "some_val");
            return true;
        }

        if (call.Callee == "None" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::None")))
        {
            var optType = _typeChecker.GetNodeType(call);
            var optLlvmType = MapType(context, optType.Name, ecs);
            var tmpAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "tmp_none");
            var tagSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 0, "opt_tag");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot);
            if (optType.TryGetOptionInfo(out var innerSym))
            {
                var valSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 1, "opt_val");
                builder.BuildStore(LLVMValueRef.CreateConstNull(MapType(context, innerSym.Name, ecs)), valSlot);
            }
            result = builder.BuildLoad2(optLlvmType, tmpAlloca, "none_val");
            return true;
        }

        if (call.Callee is "Ok" or "Result::Ok" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Ok")))
        {
            var resType = _typeChecker.GetNodeType(call);
            resType.TryGetResultInfo(out var okType, out var errType);
            var resLlvmType = MapType(context, resType.Name, ecs);
            var tmpAlloca = CreateEntryBlockAlloca(context, function, resLlvmType, "tmp_ok");
            var tagSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 0, "res_tag");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot);
            var okSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 1, "res_ok_val");
            var valArg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            builder.BuildStore(valArg, okSlot);
            var errSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 2, "res_err_val");
            builder.BuildStore(LLVMValueRef.CreateConstNull(MapType(context, errType.Name, ecs)), errSlot);
            result = builder.BuildLoad2(resLlvmType, tmpAlloca, "ok_val");
            return true;
        }

        if (call.Callee is "Err" or "Result::Err" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Err")))
        {
            var resType = _typeChecker.GetNodeType(call);
            resType.TryGetResultInfo(out var okType, out var errType);
            var resLlvmType = MapType(context, resType.Name, ecs);
            var tmpAlloca = CreateEntryBlockAlloca(context, function, resLlvmType, "tmp_err");
            var tagSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 0, "res_tag");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot);
            var okSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 1, "res_ok_val");
            builder.BuildStore(LLVMValueRef.CreateConstNull(MapType(context, okType.Name, ecs)), okSlot);
            var errSlot = builder.BuildStructGEP2(resLlvmType, tmpAlloca, 2, "res_err_val");
            var errArg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            builder.BuildStore(errArg, errSlot);
            result = builder.BuildLoad2(resLlvmType, tmpAlloca, "err_val");
            return true;
        }

        result = default;
        return false;
    }

    private unsafe bool TryGenerateTaggedUnionMethodCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        MethodCallExpression methodCall,
        LLVMValueRef targetVal,
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
        if (targetType.IsOption)
        {
            targetType.TryGetOptionInfo(out var elemType);
            var elemValType = MapType(context, elemType.Name, ecs);
            var tagVal = builder.BuildExtractValue(targetVal, 0, "opt_tag");

            if (methodCall.MethodName == "is_some")
            {
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_some");
                return true;
            }

            if (methodCall.MethodName == "is_none")
            {
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_none");
                return true;
            }

            if (methodCall.MethodName == "unwrap")
            {
                var isSome = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "opt_is_some");
                var okBB = function.AppendBasicBlock("opt_unwrap_ok");
                var failBB = function.AppendBasicBlock("opt_unwrap_panic");

                builder.BuildCondBr(isSome, okBB, failBB);

                builder.PositionAtEnd(failBB);
                var panicMsg = builder.BuildGlobalStringPtr("Panic: called Option.unwrap() on None", "panic_opt_unwrap");
                builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                var exitFunc = module.GetNamedFunction("exit");
                var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                if (exitFunc.Handle == IntPtr.Zero)
                {
                    exitFunc = module.AddFunction("exit", exitType);
                }
                builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                builder.BuildUnreachable();

                builder.PositionAtEnd(okBB);
                result = builder.BuildExtractValue(targetVal, 1, "opt_unwrapped");
                return true;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                var defVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var isSome = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "opt_is_some");
                var okBB = function.AppendBasicBlock("opt_unwrapor_ok");
                var defBB = function.AppendBasicBlock("opt_unwrapor_def");
                var mergeBB = function.AppendBasicBlock("opt_unwrapor_merge");

                var retAlloca = CreateEntryBlockAlloca(context, function, elemValType, "unwrap_or_ret");

                builder.BuildCondBr(isSome, okBB, defBB);

                builder.PositionAtEnd(okBB);
                var someVal = builder.BuildExtractValue(targetVal, 1, "opt_unwrapped_val");
                builder.BuildStore(someVal, retAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(defBB);
                builder.BuildStore(defVal, retAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(elemValType, retAlloca, "unwrap_or_val");
                return true;
            }
        }

        if (targetType.IsResult)
        {
            targetType.TryGetResultInfo(out var okType, out var errType);
            var okLlvmType = MapType(context, okType.Name, ecs);
            var errLlvmType = MapType(context, errType.Name, ecs);

            var tagVal = builder.BuildExtractValue(targetVal, 0, "res_tag");

            if (methodCall.MethodName == "is_ok")
            {
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_ok");
                return true;
            }

            if (methodCall.MethodName == "is_err")
            {
                result = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_err");
                return true;
            }

            if (methodCall.MethodName == "unwrap")
            {
                var isOk = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "res_is_ok");
                var okBB = function.AppendBasicBlock("res_unwrap_ok");
                var failBB = function.AppendBasicBlock("res_unwrap_panic");

                builder.BuildCondBr(isOk, okBB, failBB);

                builder.PositionAtEnd(failBB);
                var panicMsg = builder.BuildGlobalStringPtr("Panic: called Result.unwrap() on Err", "panic_res_unwrap");
                builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                var exitFunc = module.GetNamedFunction("exit");
                var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                if (exitFunc.Handle == IntPtr.Zero)
                {
                    exitFunc = module.AddFunction("exit", exitType);
                }
                builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                builder.BuildUnreachable();

                builder.PositionAtEnd(okBB);
                result = builder.BuildExtractValue(targetVal, 1, "res_unwrapped_ok");
                return true;
            }

            if (methodCall.MethodName == "unwrap_err")
            {
                var isErr = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "res_is_err");
                var errBB = function.AppendBasicBlock("res_unwrap_err");
                var failBB = function.AppendBasicBlock("res_unwrap_err_panic");

                builder.BuildCondBr(isErr, errBB, failBB);

                builder.PositionAtEnd(failBB);
                var panicMsg = builder.BuildGlobalStringPtr("Panic: called Result.unwrap_err() on Ok", "panic_res_unwrap_err");
                builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                var exitFunc = module.GetNamedFunction("exit");
                var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                if (exitFunc.Handle == IntPtr.Zero)
                {
                    exitFunc = module.AddFunction("exit", exitType);
                }
                builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                builder.BuildUnreachable();

                builder.PositionAtEnd(errBB);
                result = builder.BuildExtractValue(targetVal, 2, "res_unwrapped_err");
                return true;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                var defVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var isOk = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "res_is_ok");
                var okBB = function.AppendBasicBlock("res_unwrapor_ok");
                var defBB = function.AppendBasicBlock("res_unwrapor_def");
                var mergeBB = function.AppendBasicBlock("res_unwrapor_merge");

                var retAlloca = CreateEntryBlockAlloca(context, function, okLlvmType, "res_unwrap_or_ret");

                builder.BuildCondBr(isOk, okBB, defBB);

                builder.PositionAtEnd(okBB);
                var okVal = builder.BuildExtractValue(targetVal, 1, "res_unwrapped_ok");
                builder.BuildStore(okVal, retAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(defBB);
                builder.BuildStore(defVal, retAlloca);
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(okLlvmType, retAlloca, "res_unwrap_or_val");
                return true;
            }
        }

        result = default;
        return false;
    }
}
