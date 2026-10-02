using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef GenerateCastExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        CastExpressionNode cast,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var srcVal = CompileExpression(context, module, builder, function, cast.Expr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        var srcType = _typeChecker.GetNodeType(cast.Expr);
        var castTargetType = TypeSymbol.FromName(cast.TargetTypeName);
        var castTargetLlvmType = MapType(context, castTargetType.Name, ecs);

        if (srcVal.TypeOf == castTargetLlvmType)
        {
            return srcVal;
        }

        bool srcIsFp = srcType.IsFloatingPoint || srcVal.TypeOf == context.FloatType || srcVal.TypeOf == context.DoubleType;
        bool targetIsFp = castTargetType.IsFloatingPoint || castTargetLlvmType == context.FloatType || castTargetLlvmType == context.DoubleType;

        if (srcIsFp && !targetIsFp)
        {
            return builder.BuildFPToSI(srcVal, castTargetLlvmType, "fptosi");
        }
        if (!srcIsFp && targetIsFp)
        {
            return builder.BuildSIToFP(srcVal, castTargetLlvmType, "sitofp");
        }
        if (srcIsFp && targetIsFp)
        {
            if (srcVal.TypeOf == context.DoubleType && castTargetLlvmType == context.FloatType)
                return builder.BuildFPTrunc(srcVal, castTargetLlvmType, "fptrunc");
            if (srcVal.TypeOf == context.FloatType && castTargetLlvmType == context.DoubleType)
                return builder.BuildFPExt(srcVal, castTargetLlvmType, "fpext");
            return srcVal;
        }

        // Integer / boolean casts
        uint srcWidth = srcVal.TypeOf.IntWidth;
        uint targetWidth = castTargetLlvmType.IntWidth;

        if (srcWidth > targetWidth)
        {
            return builder.BuildTrunc(srcVal, castTargetLlvmType, "trunc");
        }
        if (srcWidth < targetWidth)
        {
            if (srcVal.TypeOf == context.Int1Type || srcType == TypeSymbol.Bool)
                return builder.BuildZExt(srcVal, castTargetLlvmType, "zext");
            return builder.BuildSExt(srcVal, castTargetLlvmType, "sext");
        }

        return srcVal;
    }
}
