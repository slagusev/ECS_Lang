using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef CompileLambdaExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        LambdaExpression lambda,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var lambdaTypeSym = _typeChecker.GetNodeType(lambda);
        lambdaTypeSym.TryGetFunctionInfo(out var lParams, out var lRet);

        string lambdaName = $"__lambda_{_lambdaCounter++}";
        var lambdaRetLlvmType = MapType(context, lRet?.Name ?? "void", ecs);

        var lambdaFnParamTypes = new List<LLVMTypeRef> { i8PtrType }; // env pointer
        foreach (var p in lambda.Parameters)
        {
            var pTypeSym = p.TypeName != null ? TypeSymbol.FromName(p.TypeName) : TypeSymbol.I32;
            lambdaFnParamTypes.Add(MapType(context, pTypeSym.Name, ecs));
        }

        var lambdaFnType = LLVMTypeRef.CreateFunction(lambdaRetLlvmType, lambdaFnParamTypes.ToArray(), false);
        var lambdaFunc = module.AddFunction(lambdaName, lambdaFnType);

        // Build Lambda Body
        var lambdaEntry = lambdaFunc.AppendBasicBlock("entry");
        var lambdaBuilder = context.CreateBuilder();
        lambdaBuilder.PositionAtEnd(lambdaEntry);

        var lambdaLocals = new Dictionary<string, LLVMValueRef>();
        var lambdaVarTypes = new Dictionary<string, string>();

        // 1. Environment unpacking (if captures exist)
        LLVMTypeRef envStructType = context.GetStructType(Array.Empty<LLVMTypeRef>(), false);
        if (lambda.Captures.Count > 0)
        {
            var envFieldTypes = new List<LLVMTypeRef>();
            for (int i = 0; i < lambda.Captures.Count; i++)
            {
                var capName = lambda.Captures[i];
                string capTypeName = varTypes.TryGetValue(capName, out var ct) ? ct : "i32";
                var capLlvmType = MapType(context, capTypeName, ecs);
                envFieldTypes.Add(LLVMTypeRef.CreatePointer(capLlvmType, 0));
            }

            envStructType = context.GetStructType(envFieldTypes.ToArray(), false);
            var envStructPtrType = LLVMTypeRef.CreatePointer(envStructType, 0);

            var envParam = lambdaFunc.GetParam(0);
            var typedEnv = lambdaBuilder.BuildBitCast(envParam, envStructPtrType, "env_typed");

            for (int i = 0; i < lambda.Captures.Count; i++)
            {
                var capName = lambda.Captures[i];
                string capTypeName = varTypes.TryGetValue(capName, out var ct) ? ct : "i32";
                var gep = lambdaBuilder.BuildStructGEP2(envStructType, typedEnv, (uint)i, $"{capName}_slot");
                var capPtr = lambdaBuilder.BuildLoad2(envFieldTypes[i], gep, $"{capName}_ptr");
                lambdaLocals[capName] = capPtr;
                lambdaVarTypes[capName] = capTypeName;
            }
        }

        // 2. Setup Lambda parameters
        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            var p = lambda.Parameters[i];
            var pVal = lambdaFunc.GetParam((uint)(1 + i));
            var pType = lambdaFnParamTypes[1 + i];
            var pAlloca = CreateEntryBlockAlloca(context, lambdaFunc, pType, p.Name);
            lambdaBuilder.BuildStore(pVal, pAlloca);
            lambdaLocals[p.Name] = pAlloca;
            lambdaVarTypes[p.Name] = p.TypeName ?? "i32";
        }

        // 3. Compile lambda body
        CompileBlock(context, module, lambdaBuilder, lambdaFunc, lambda.Body, lambdaLocals, lambdaVarTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain: false, hasWaitKey: false);
        if (lambdaBuilder.InsertBlock.Terminator.Handle == IntPtr.Zero)
        {
            if (lambdaRetLlvmType == context.VoidType)
            {
                lambdaBuilder.BuildRetVoid();
            }
            else
            {
                lambdaBuilder.BuildRet(LLVMValueRef.CreateConstNull(lambdaRetLlvmType));
            }
        }
        lambdaBuilder.Dispose();

        // 4. In caller function: instantiate closure fat pointer { ptr fn, ptr env }
        var closureStructType = context.GetStructType(new[] { i8PtrType, i8PtrType }, false);
        var closureAlloca = CreateEntryBlockAlloca(context, function, closureStructType, "closure_tmp");
        var fnSlot = builder.BuildStructGEP2(closureStructType, closureAlloca, 0, "fn_slot");
        var envSlot = builder.BuildStructGEP2(closureStructType, closureAlloca, 1, "env_slot");

        var rawFn = builder.BuildBitCast(lambdaFunc, i8PtrType, "raw_lambda_fn");
        builder.BuildStore(rawFn, fnSlot);

        if (lambda.Captures.Count > 0)
        {
            var callerEnvAlloca = CreateEntryBlockAlloca(context, function, envStructType, "closure_env");
            for (int i = 0; i < lambda.Captures.Count; i++)
            {
                var capName = lambda.Captures[i];
                if (locals.TryGetValue(capName, out var capLocalPtr))
                {
                    var gep = builder.BuildStructGEP2(envStructType, callerEnvAlloca, (uint)i, $"{capName}_env_gep");
                    builder.BuildStore(capLocalPtr, gep);
                }
            }
            var rawEnv = builder.BuildBitCast(callerEnvAlloca, i8PtrType, "raw_env");
            builder.BuildStore(rawEnv, envSlot);
        }
        else
        {
            builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), envSlot);
        }

        return builder.BuildLoad2(closureStructType, closureAlloca, "closure_val");
    }

    private unsafe LLVMValueRef CompileIndirectCallExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        IndirectCallExpression ind,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var indTargetVal = CompileExpression(context, module, builder, function, ind.Callee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        var indFnTypeSym = _typeChecker.GetNodeType(ind.Callee);
        indFnTypeSym.TryGetFunctionInfo(out var indParamTypes, out var indRetTypeSym);

        var indRetLlvm = MapType(context, indRetTypeSym?.Name ?? "void", ecs);
        var indParamLlvm = new List<LLVMTypeRef> { i8PtrType };
        if (indParamTypes != null)
        {
            foreach (var pt in indParamTypes)
            {
                indParamLlvm.Add(MapType(context, pt.Name, ecs));
            }
        }

        var indSignature = LLVMTypeRef.CreateFunction(indRetLlvm, indParamLlvm.ToArray(), false);
        var indSigPtr = LLVMTypeRef.CreatePointer(indSignature, 0);

        var indFnRaw = builder.BuildExtractValue(indTargetVal, 0, "ind_fn_raw");
        var indEnvPtr = builder.BuildExtractValue(indTargetVal, 1, "ind_env_ptr");
        var indTypedFn = builder.BuildBitCast(indFnRaw, indSigPtr, "ind_typed_fn");

        var indCallArgs = new List<LLVMValueRef> { indEnvPtr };
        foreach (var arg in ind.Arguments)
        {
            indCallArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
        }

        string indCallName = indRetLlvm == context.VoidType ? "" : "ind_call";
        return builder.BuildCall2(indSignature, indTypedFn, indCallArgs.ToArray(), indCallName);
    }

    private unsafe bool TryCompileClosureVariableCall(
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
        if (locals.TryGetValue(call.Callee, out var closureLocalPtr) &&
            varTypes.TryGetValue(call.Callee, out var cTypeName) &&
            (cTypeName.StartsWith("fn(") || cTypeName.StartsWith("closure(") || TypeSymbol.FromName(cTypeName).IsFunction))
        {
            var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
            var cTypeSym = TypeSymbol.FromName(cTypeName);
            cTypeSym.TryGetFunctionInfo(out var cParamTypes, out var cRetTypeSym);

            var cRetLlvm = MapType(context, cRetTypeSym?.Name ?? "void", ecs);
            var cParamLlvm = new List<LLVMTypeRef> { i8PtrType };
            if (cParamTypes != null)
            {
                foreach (var pt in cParamTypes)
                {
                    cParamLlvm.Add(MapType(context, pt.Name, ecs));
                }
            }

            var cSignature = LLVMTypeRef.CreateFunction(cRetLlvm, cParamLlvm.ToArray(), false);
            var cSigPtr = LLVMTypeRef.CreatePointer(cSignature, 0);

            var closureVal = builder.BuildLoad2(context.GetStructType(new[] { i8PtrType, i8PtrType }, false), closureLocalPtr, $"{call.Callee}_val");
            var cFnRaw = builder.BuildExtractValue(closureVal, 0, "c_fn_raw");
            var cEnvPtr = builder.BuildExtractValue(closureVal, 1, "c_env_ptr");
            var cTypedFn = builder.BuildBitCast(cFnRaw, cSigPtr, "c_typed_fn");

            var cCallArgs = new List<LLVMValueRef> { cEnvPtr };
            foreach (var arg in call.Arguments)
            {
                cCallArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
            }

            string cCallName = cRetLlvm == context.VoidType ? "" : $"{call.Callee}_call";
            result = builder.BuildCall2(cSignature, cTypedFn, cCallArgs.ToArray(), cCallName);
            return true;
        }

        result = default;
        return false;
    }
}
