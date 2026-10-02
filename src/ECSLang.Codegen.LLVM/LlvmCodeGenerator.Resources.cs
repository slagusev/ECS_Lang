using System.Collections.Generic;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef GenerateResourceGetExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        ResourceGetExpressionNode resGet,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        LLVMValueRef worldPtr;
        if (resGet.Target != null)
        {
            worldPtr = CompileExpression(context, module, builder, function, resGet.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        }
        else if (locals.TryGetValue("world", out var defaultWorld))
        {
            worldPtr = defaultWorld;
        }
        else
        {
            return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
        }

        int resOffset = ecs.GetResourceOffset(resGet.ResourceName);
        if (resOffset < 0)
        {
            return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
        }

        var structType = ecs.GetComponentStructType(resGet.ResourceName);
        var resSlotPtr = builder.BuildStructGEP2(ecs.WorldStructType, worldPtr, (uint)resOffset, $"res_{resGet.ResourceName}_ptr");
        return builder.BuildLoad2(structType, resSlotPtr, $"res_{resGet.ResourceName}_val");
    }
}
