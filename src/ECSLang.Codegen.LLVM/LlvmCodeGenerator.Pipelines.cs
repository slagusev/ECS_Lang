using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private void CompileParallelAutoBlock(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        ParallelAutoBlockNode autoBlock,
        LLVMValueRef worldParam,
        LLVMTypeRef worldPtrType,
        LLVMTypeRef i8PtrType)
    {
        var batches = _typeChecker.GetParallelAutoBatches(autoBlock);
        foreach (var batch in batches)
        {
            CompileParallelBatch(context, module, builder, batch, worldParam, worldPtrType, i8PtrType);
        }
    }

    private void CompileParallelBatch(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        IReadOnlyList<SystemCallAction> batch,
        LLVMValueRef worldParam,
        LLVMTypeRef worldPtrType,
        LLVMTypeRef i8PtrType)
    {
        if (batch.Count == 0) return;

        if (batch.Count == 1)
        {
            var sName = batch[0].SystemName;
            var sysFunc = module.GetNamedFunction($"system_{sName}");
            if (sysFunc.Handle == IntPtr.Zero && sName.Contains("<"))
            {
                sysFunc = module.GetNamedFunction($"system_{TypeSymbol.ToMonomorphizedIdentifier(sName)}");
            }
            if (sysFunc.Handle != IntPtr.Zero)
            {
                var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
            }
        }
        else
        {
            if (_options.Target.IsWindows)
            {
                var createWorkFunc = module.GetNamedFunction("CreateThreadpoolWork");
                var submitWorkFunc = module.GetNamedFunction("SubmitThreadpoolWork");
                var waitWorkFunc = module.GetNamedFunction("WaitForThreadpoolWorkCallbacks");
                var closeWorkFunc = module.GetNamedFunction("CloseThreadpoolWork");

                var createWorkType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, i8PtrType }, false);
                var submitWorkType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                var waitWorkType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type }, false);
                var closeWorkType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);

                var worldI8 = builder.BuildBitCast(worldParam, i8PtrType, "world_i8");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);

                var workHandles = new List<LLVMValueRef>();

                foreach (var sCall in batch)
                {
                    var jobFunc = module.GetNamedFunction($"job_{sCall.SystemName}");
                    if (jobFunc.Handle != IntPtr.Zero)
                    {
                        var jobFuncI8 = builder.BuildBitCast(jobFunc, i8PtrType, $"job_fn_{sCall.SystemName}");
                        var workHandle = builder.BuildCall2(createWorkType, createWorkFunc, new[] { jobFuncI8, worldI8, nullPtr }, $"work_{sCall.SystemName}");
                        builder.BuildCall2(submitWorkType, submitWorkFunc, new[] { workHandle });
                        workHandles.Add(workHandle);
                    }
                }

                // Thread Barrier: wait for all systems in this batch to finish
                foreach (var workHandle in workHandles)
                {
                    builder.BuildCall2(waitWorkType, waitWorkFunc, new[] { workHandle, LLVMValueRef.CreateConstInt(context.Int32Type, 0) });
                    builder.BuildCall2(closeWorkType, closeWorkFunc, new[] { workHandle });
                }
            }
            else
            {
                foreach (var sCall in batch)
                {
                    var sName = sCall.SystemName;
                    var sysFunc = module.GetNamedFunction($"system_{sName}");
                    if (sysFunc.Handle == IntPtr.Zero && sName.Contains("<"))
                    {
                        sysFunc = module.GetNamedFunction($"system_{TypeSymbol.ToMonomorphizedIdentifier(sName)}");
                    }
                    if (sysFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                    }
                }
            }
        }
    }
}
