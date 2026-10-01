using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private static LLVMValueRef EmitGuardedRaylibVoidCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        Action emitCall)
    {
        var isWinReadyFunc = module.GetNamedFunction("IsWindowReady");
        var isWinReadyType = LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
        var rawByte = builder.BuildCall2(isWinReadyType, isWinReadyFunc, Array.Empty<LLVMValueRef>(), "win_ready_raw");
        var winReady = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, rawByte, LLVMValueRef.CreateConstInt(context.Int8Type, 0), "win_ready");

        var callBB = function.AppendBasicBlock("rl_draw_call");
        var mergeBB = function.AppendBasicBlock("rl_draw_merge");

        builder.BuildCondBr(winReady, callBB, mergeBB);

        builder.PositionAtEnd(callBB);
        emitCall();
        builder.BuildBr(mergeBB);

        builder.PositionAtEnd(mergeBB);
        return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
    }


    private static LLVMValueRef PackRgbaColor(LLVMContextRef context, LLVMBuilderRef builder, LLVMValueRef r, LLVMValueRef g, LLVMValueRef b, LLVMValueRef? a = null)
    {
        var rMask = builder.BuildAnd(r, LLVMValueRef.CreateConstInt(context.Int32Type, 0xFF), "r_byte");
        var gMask = builder.BuildAnd(g, LLVMValueRef.CreateConstInt(context.Int32Type, 0xFF), "g_byte");
        var bMask = builder.BuildAnd(b, LLVMValueRef.CreateConstInt(context.Int32Type, 0xFF), "b_byte");
        var aVal = a ?? LLVMValueRef.CreateConstInt(context.Int32Type, 0xFF);
        var aMask = builder.BuildAnd(aVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0xFF), "a_byte");

        var gShl = builder.BuildShl(gMask, LLVMValueRef.CreateConstInt(context.Int32Type, 8), "g_shl");
        var bShl = builder.BuildShl(bMask, LLVMValueRef.CreateConstInt(context.Int32Type, 16), "b_shl");
        var aShl = builder.BuildShl(aMask, LLVMValueRef.CreateConstInt(context.Int32Type, 24), "a_shl");

        var rg = builder.BuildOr(rMask, gShl, "rg");
        var rgb = builder.BuildOr(rg, bShl, "rgb");
        return builder.BuildOr(rgb, aShl, "rgba_packed");
    }


}
