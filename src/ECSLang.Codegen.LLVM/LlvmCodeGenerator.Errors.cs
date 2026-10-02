using System;
using System.Collections.Generic;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private LLVMValueRef GenerateErrorPropagationExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        ErrorPropagationExpressionNode tryExpr,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var innerType = _typeChecker.GetNodeType(tryExpr.Expr);
        bool isMain = function.Name == "main" || (_currentFunctionDecl != null && _currentFunctionDecl.Name == "main");

        if (innerType.TryGetResultInfo(out var okType, out var errType))
        {
            var innerVal = CompileExpression(context, module, builder, function, tryExpr.Expr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var innerLlvmType = MapType(context, innerType.Name, ecs);

            var innerAlloca = CreateEntryBlockAlloca(context, function, innerLlvmType, "try_res_alloca");
            builder.BuildStore(innerVal, innerAlloca);

            var tagSlot = builder.BuildStructGEP2(innerLlvmType, innerAlloca, 0, "try_tag_slot");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagSlot, "try_tag");

            // In Result<T, E>, tag == 0 is Ok, tag == 1 is Err
            var isOk = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_ok");

            var okBB = function.AppendBasicBlock("try_ok");
            var errBB = function.AppendBasicBlock("try_err");
            builder.BuildCondBr(isOk, okBB, errBB);

            // Emit Error branch
            builder.PositionAtEnd(errBB);
            var errSlot = builder.BuildStructGEP2(innerLlvmType, innerAlloca, 2, "try_err_slot");
            var errLlvmType = MapType(context, errType.Name, ecs);
            var errVal = builder.BuildLoad2(errLlvmType, errSlot, "try_err_val");

            if (isMain)
            {
                if (errType == TypeSymbol.String)
                {
                    var fmt = builder.BuildGlobalStringPtr("[Error] %s\n", "err_fmt_str");
                    builder.BuildCall2(printfType, printfFunc, new[] { fmt, errVal }, "print_err_str");
                }
                else if (errType.IsInteger)
                {
                    var fmt = builder.BuildGlobalStringPtr("[Error] Error code: %d\n", "err_fmt_int");
                    builder.BuildCall2(printfType, printfFunc, new[] { fmt, errVal }, "print_err_int");
                }
                else
                {
                    var msg = builder.BuildGlobalStringPtr("[Error] Unhandled error encountered in main\n", "err_msg");
                    builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_err");
                }

                builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int32Type, 1));
            }
            else
            {
                var retTypeSymbol = _currentFunctionDecl?.ReturnType != null ? TypeSymbol.FromName(_currentFunctionDecl.ReturnType) : null;
                var retLlvmType = retTypeSymbol != null ? MapType(context, retTypeSymbol.Name, ecs) : innerLlvmType;

                var earlyRetAlloca = CreateEntryBlockAlloca(context, function, retLlvmType, "early_err_ret");
                var retTagSlot = builder.BuildStructGEP2(retLlvmType, earlyRetAlloca, 0, "err_tag");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), retTagSlot);

                TypeSymbol retOkType = okType;
                if (retTypeSymbol != null && retTypeSymbol.TryGetResultInfo(out var rOk, out _))
                {
                    retOkType = rOk;
                }
                var retOkLlvmType = MapType(context, retOkType.Name, ecs);
                var retOkSlot = builder.BuildStructGEP2(retLlvmType, earlyRetAlloca, 1, "dummy_ok");
                builder.BuildStore(LLVMValueRef.CreateConstNull(retOkLlvmType), retOkSlot);

                var retErrSlot = builder.BuildStructGEP2(retLlvmType, earlyRetAlloca, 2, "ret_err");
                builder.BuildStore(errVal, retErrSlot);

                var retVal = builder.BuildLoad2(retLlvmType, earlyRetAlloca, "early_ret_val");
                builder.BuildRet(retVal);
            }

            // Emit Ok branch
            builder.PositionAtEnd(okBB);
            var okSlot = builder.BuildStructGEP2(innerLlvmType, innerAlloca, 1, "try_ok_slot");
            var okLlvmType = MapType(context, okType.Name, ecs);
            return builder.BuildLoad2(okLlvmType, okSlot, "unwrapped_ok");
        }
        else if (innerType.TryGetOptionInfo(out var valueType))
        {
            var innerVal = CompileExpression(context, module, builder, function, tryExpr.Expr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var innerLlvmType = MapType(context, innerType.Name, ecs);

            var innerAlloca = CreateEntryBlockAlloca(context, function, innerLlvmType, "try_opt_alloca");
            builder.BuildStore(innerVal, innerAlloca);

            var tagSlot = builder.BuildStructGEP2(innerLlvmType, innerAlloca, 0, "try_tag_slot");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagSlot, "try_tag");

            // In Option<T>, tag == 1 is Some, tag == 0 is None
            var isSome = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_some");

            var someBB = function.AppendBasicBlock("try_some");
            var noneBB = function.AppendBasicBlock("try_none");
            builder.BuildCondBr(isSome, someBB, noneBB);

            // Emit None branch
            builder.PositionAtEnd(noneBB);
            if (isMain)
            {
                var msg = builder.BuildGlobalStringPtr("[Error] None value encountered in main\n", "err_msg");
                builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_err");
                builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int32Type, 1));
            }
            else
            {
                var retTypeSymbol = _currentFunctionDecl?.ReturnType != null ? TypeSymbol.FromName(_currentFunctionDecl.ReturnType) : null;
                var retLlvmType = retTypeSymbol != null ? MapType(context, retTypeSymbol.Name, ecs) : innerLlvmType;

                var earlyRetAlloca = CreateEntryBlockAlloca(context, function, retLlvmType, "early_none_ret");
                var retTagSlot = builder.BuildStructGEP2(retLlvmType, earlyRetAlloca, 0, "none_tag");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), retTagSlot);

                TypeSymbol retValType = valueType;
                if (retTypeSymbol != null && retTypeSymbol.TryGetOptionInfo(out var rVal))
                {
                    retValType = rVal;
                }
                var retValLlvmType = MapType(context, retValType.Name, ecs);
                var retValSlot = builder.BuildStructGEP2(retLlvmType, earlyRetAlloca, 1, "dummy_val");
                builder.BuildStore(LLVMValueRef.CreateConstNull(retValLlvmType), retValSlot);

                var retVal = builder.BuildLoad2(retLlvmType, earlyRetAlloca, "early_ret_none");
                builder.BuildRet(retVal);
            }

            // Emit Some branch
            builder.PositionAtEnd(someBB);
            var valSlot = builder.BuildStructGEP2(innerLlvmType, innerAlloca, 1, "try_some_slot");
            var valLlvmType = MapType(context, valueType.Name, ecs);
            return builder.BuildLoad2(valLlvmType, valSlot, "unwrapped_some");
        }

        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
    }
}
