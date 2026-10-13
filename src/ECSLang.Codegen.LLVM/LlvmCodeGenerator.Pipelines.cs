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
                // POSIX (Linux): use pthread_create and pthread_join
                var pthreadCreateFunc = module.GetNamedFunction("pthread_create");
                var pthreadCreateType = LLVMTypeRef.CreateFunction(
                    context.Int32Type,
                    new[] { i8PtrType, i8PtrType, i8PtrType, i8PtrType },
                    false);
                if (pthreadCreateFunc.Handle == IntPtr.Zero)
                {
                    pthreadCreateFunc = module.AddFunction("pthread_create", pthreadCreateType);
                }

                var pthreadJoinFunc = module.GetNamedFunction("pthread_join");
                var pthreadJoinType = LLVMTypeRef.CreateFunction(
                    context.Int32Type,
                    new[] { context.Int64Type, i8PtrType },
                    false);
                if (pthreadJoinFunc.Handle == IntPtr.Zero)
                {
                    pthreadJoinFunc = module.AddFunction("pthread_join", pthreadJoinType);
                }

                var worldI8 = builder.BuildBitCast(worldParam, i8PtrType, "world_i8");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var nullPtrPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);

                // For M systems:
                // Systems 0 .. M-2: spawn via pthread_create
                // If spawn fails (rc != 0), fallback to inline synchronous execution
                // System M-1: always execute inline on the calling thread
                // Join barrier: only join systems that were successfully spawned via pthread_create
                var spawnedThreads = new List<LLVMValueRef>();

                for (int i = 0; i < batch.Count - 1; i++)
                {
                    var sCall = batch[i];
                    var sName = sCall.SystemName;
                    var jobFunc = module.GetNamedFunction($"job_{sName}");
                    var sysFunc = module.GetNamedFunction($"system_{sName}");
                    if (sysFunc.Handle == IntPtr.Zero && sName.Contains("<"))
                    {
                        var monoName = TypeSymbol.ToMonomorphizedIdentifier(sName);
                        jobFunc = module.GetNamedFunction($"job_{monoName}");
                        sysFunc = module.GetNamedFunction($"system_{monoName}");
                    }

                    if (jobFunc.Handle != IntPtr.Zero && sysFunc.Handle != IntPtr.Zero)
                    {
                        var threadIdAlloca = builder.BuildAlloca(context.Int64Type, $"tid_{sName}");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int64Type, 0), threadIdAlloca);
                        var threadIdPtr = builder.BuildBitCast(threadIdAlloca, i8PtrType, $"tid_ptr_{sName}");
                        var jobFuncI8 = builder.BuildBitCast(jobFunc, i8PtrType, $"job_fn_{sName}");

                        var createRc = builder.BuildCall2(
                            pthreadCreateType,
                            pthreadCreateFunc,
                            new[] { threadIdPtr, nullPtr, jobFuncI8, worldI8 },
                            $"create_rc_{sName}");

                        var isSuccess = builder.BuildICmp(
                            LLVMIntPredicate.LLVMIntEQ,
                            createRc,
                            LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                            $"spawn_ok_{sName}");

                        var parentFunc = builder.InsertBlock.Parent;
                        var spawnOkBB = parentFunc.AppendBasicBlock($"spawn_ok_{sName}");
                        var fallbackBB = parentFunc.AppendBasicBlock($"spawn_fallback_{sName}");
                        var contBB = parentFunc.AppendBasicBlock($"spawn_cont_{sName}");

                        builder.BuildCondBr(isSuccess, spawnOkBB, fallbackBB);

                        // Fallback block: execute inline on calling thread
                        builder.PositionAtEnd(fallbackBB);
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                        builder.BuildBr(contBB);

                        // Success block: thread started in background
                        builder.PositionAtEnd(spawnOkBB);
                        builder.BuildBr(contBB);

                        builder.PositionAtEnd(contBB);

                        // Store tid for joining only if spawned successfully:
                        // Load threadId in join phase if spawned, or track via PHI
                        var threadIdVal = builder.BuildLoad2(context.Int64Type, threadIdAlloca, $"tid_val_{sName}");
                        var joinTidPhi = builder.BuildPhi(context.Int64Type, $"join_tid_{sName}");
                        joinTidPhi.AddIncoming(
                            new[] { threadIdVal, LLVMValueRef.CreateConstInt(context.Int64Type, 0) },
                            new[] { spawnOkBB, fallbackBB },
                            2);

                        spawnedThreads.Add(joinTidPhi);
                    }
                    else if (sysFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                    }
                }

                // System M-1: execute inline on the calling thread
                var lastCall = batch[batch.Count - 1];
                var lastSysName = lastCall.SystemName;
                var lastSysFunc = module.GetNamedFunction($"system_{lastSysName}");
                if (lastSysFunc.Handle == IntPtr.Zero && lastSysName.Contains("<"))
                {
                    lastSysFunc = module.GetNamedFunction($"system_{TypeSymbol.ToMonomorphizedIdentifier(lastSysName)}");
                }
                if (lastSysFunc.Handle != IntPtr.Zero)
                {
                    var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                    builder.BuildCall2(voidFuncType, lastSysFunc, new[] { worldParam });
                }

                // Thread Barrier: pthread_join only for successfully spawned threads (joinTid != 0)
                var currentFn = builder.InsertBlock.Parent;
                for (int i = 0; i < spawnedThreads.Count; i++)
                {
                    var tidVal = spawnedThreads[i];
                    var hasThread = builder.BuildICmp(
                        LLVMIntPredicate.LLVMIntNE,
                        tidVal,
                        LLVMValueRef.CreateConstInt(context.Int64Type, 0),
                        $"has_thread_{i}");

                    var joinBB = currentFn.AppendBasicBlock($"join_block_{i}");
                    var joinDoneBB = currentFn.AppendBasicBlock($"join_done_{i}");

                    builder.BuildCondBr(hasThread, joinBB, joinDoneBB);

                    builder.PositionAtEnd(joinBB);
                    builder.BuildCall2(pthreadJoinType, pthreadJoinFunc, new[] { tidVal, nullPtrPtr });
                    builder.BuildBr(joinDoneBB);

                    builder.PositionAtEnd(joinDoneBB);
                }
            }
        }
    }
}
