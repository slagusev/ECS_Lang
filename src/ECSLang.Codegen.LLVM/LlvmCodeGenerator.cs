using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed class LlvmCodeGenerator
{
    private readonly DiagnosticsBag _diagnostics;
    private readonly TypeChecker _typeChecker;

    static LlvmCodeGenerator()
    {
        LlvmApi.InitializeAllTargetInfos();
        LlvmApi.InitializeAllTargets();
        LlvmApi.InitializeAllTargetMCs();
        LlvmApi.InitializeAllAsmParsers();
        LlvmApi.InitializeAllAsmPrinters();
    }

    public LlvmCodeGenerator(DiagnosticsBag diagnostics, TypeChecker? typeChecker = null)
    {
        _diagnostics = diagnostics;
        _typeChecker = typeChecker ?? new TypeChecker(diagnostics);
    }

    public unsafe bool Compile(
        ProgramNode program,
        string outputObjPath,
        string? outputLlvmIrPath = null)
    {
        using var context = LLVMContextRef.Create();
        using var module = context.CreateModuleWithName("ecs_module");
        using var builder = context.CreateBuilder();

        string targetTriple = "x86_64-pc-windows-msvc";
        if (!LLVMTargetRef.TryGetTargetFromTriple(targetTriple, out var target, out var errorMessage))
        {
            _diagnostics.ReportError($"Failed to get LLVM target for '{targetTriple}': {errorMessage}", SourceSpan.None);
            return false;
        }

        var targetMachine = target.CreateTargetMachine(
            targetTriple,
            "generic",
            "",
            LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            LLVMRelocMode.LLVMRelocDefault,
            LLVMCodeModel.LLVMCodeModelDefault);

        module.Target = targetTriple;
        var dataLayout = targetMachine.CreateTargetDataLayout();
        sbyte* dataLayoutStr = LlvmApi.CopyStringRepOfTargetData(dataLayout);
        module.DataLayout = Marshal.PtrToStringAnsi((IntPtr)dataLayoutStr) ?? "";
        LlvmApi.DisposeMessage(dataLayoutStr);

        // Declare C runtime I/O functions
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        // int puts(const char* str)
        var putsType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType }, false);
        var putsFunc = module.AddFunction("puts", putsType);

        // int printf(const char* format, ...)
        var printfType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType }, true);
        var printfFunc = module.AddFunction("printf", printfType);

        // void* realloc(void* ptr, size_t size)
        var reallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int64Type }, false);
        var reallocFunc = module.AddFunction("realloc", reallocType);

        // int getchar()
        var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
        var getcharFunc = module.AddFunction("getchar", getcharType);

        // void* memcpy(void* dest, const void* src, size_t count)
        var memcpyType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, context.Int64Type }, false);
        var memcpyFunc = module.AddFunction("memcpy", memcpyType);

        // void* memset(void* dest, int ch, size_t count)
        var memsetType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int32Type, context.Int64Type }, false);
        var memsetFunc = module.AddFunction("memset", memsetType);

        // void* malloc(size_t size)
        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
        var mallocFunc = module.AddFunction("malloc", mallocType);

        // void free(void* ptr)
        var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
        var freeFunc = module.AddFunction("free", freeType);

        // Raylib C ABI declarations
        module.AddFunction("InitWindow", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, i8PtrType }, false));
        module.AddFunction("WindowShouldClose", LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("CloseWindow", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("SetTargetFPS", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false));
        module.AddFunction("GetFPS", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("GetFrameTime", LLVMTypeRef.CreateFunction(context.FloatType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("GetTime", LLVMTypeRef.CreateFunction(context.DoubleType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("BeginDrawing", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("EndDrawing", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("ClearBackground", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false));
        module.AddFunction("DrawText", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false));
        module.AddFunction("DrawRectangle", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false));
        module.AddFunction("DrawCircle", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.FloatType, context.Int32Type }, false));
        module.AddFunction("DrawLine", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false));
        module.AddFunction("IsKeyDown", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyPressed", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyReleased", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyUp", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));
        module.AddFunction("GetMouseX", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("GetMouseY", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("IsMouseButtonDown", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsMouseButtonPressed", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false));

        // Win32 ThreadPool API declarations (kernel32.lib)
        module.AddFunction("CreateThreadpoolWork", LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, i8PtrType }, false));
        module.AddFunction("SubmitThreadpoolWork", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("WaitForThreadpoolWorkCallbacks", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type }, false));
        module.AddFunction("CloseThreadpoolWork", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("GetTickCount", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));

        // Export NVIDIA Optimus and AMD PowerXpress enablement flags to force dedicated GPU
        var nvOptimus = module.AddGlobal(context.Int32Type, "NvOptimusEnablement");
        nvOptimus.Initializer = LLVMValueRef.CreateConstInt(context.Int32Type, 1);
        nvOptimus.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;

        var amdPower = module.AddGlobal(context.Int32Type, "AmdPowerXpressRequestHighPerformance");
        amdPower.Initializer = LLVMValueRef.CreateConstInt(context.Int32Type, 1);
        amdPower.DLLStorageClass = LLVMDLLStorageClass.LLVMDLLExportStorageClass;

        // Run type checker first
        _typeChecker.CheckProgram(program);
        if (_diagnostics.HasErrors)
        {
            return false;
        }

        // Initialize ECS Multi-Archetype Runtime declarations
        var ecsEmitter = new EcsRuntimeEmitter(context, module, builder, _typeChecker, _diagnostics);
        ecsEmitter.EmitEcsDeclarations(dataLayout);

        // Emit Multi-Archetype Runtime (spawn, add, remove, has, setters, sort)
        ecsEmitter.EmitMultiArchetypeRuntime(dataLayout, reallocType, reallocFunc, memcpyType, memcpyFunc, memsetType, memsetFunc, mallocType, mallocFunc, freeType, freeFunc);

        // Compile ECS Systems
        foreach (var decl in program.Declarations)
        {
            if (decl is SystemDeclaration sys)
            {
                CompileSystem(context, module, builder, sys, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
            }
        }

        // Compile ECS Pipelines
        foreach (var decl in program.Declarations)
        {
            if (decl is PipelineDeclaration pipe)
            {
                CompilePipeline(context, module, builder, pipe, ecsEmitter);
            }
        }

        // Forward-declare regular functions so any function or system can call any function
        foreach (var decl in program.Declarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                var returnType = MapType(context, fnDecl.ReturnType, ecsEmitter);
                var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName, ecsEmitter)).ToArray();
                var funcType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
                module.AddFunction(fnDecl.Name, funcType);
            }
        }

        // Compile regular functions
        foreach (var decl in program.Declarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                CompileFunction(context, module, builder, fnDecl, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
            }
        }

        // Verify module
        if (!module.TryVerify(LLVMVerifierFailureAction.LLVMPrintMessageAction, out var verifyMessage))
        {
            _diagnostics.ReportError($"LLVM Module verification failed: {verifyMessage}", SourceSpan.None);
            return false;
        }

        // Emit LLVM IR if requested
        if (!string.IsNullOrEmpty(outputLlvmIrPath))
        {
            File.WriteAllText(outputLlvmIrPath, module.PrintToString());
        }

        // Emit Object File (.obj)
        if (!targetMachine.TryEmitToFile(module, outputObjPath, LLVMCodeGenFileType.LLVMObjectFile, out var emitError))
        {
            _diagnostics.ReportError($"Failed to emit object file '{outputObjPath}': {emitError}", SourceSpan.None);
            return false;
        }

        return true;
    }

    private void CompileSystem(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        SystemDeclaration sys,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var sysFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var sysFunc = module.AddFunction($"system_{sys.Name}", sysFuncType);
        var worldParam = sysFunc.GetParam(0);
        worldParam.Name = "world";
        var entryBB = sysFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var worldAlloca = builder.BuildAlloca(worldPtrType, "world_alloca");
        builder.BuildStore(worldParam, worldAlloca);

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        if (sys.IsEventSystem)
        {
            var evParam = sys.ReadParams[0];
            string evTypeName = evParam.TypeName;
            int evBaseOffset = ecs.GetEventWorldOffset(evTypeName);
            var evStructType = ecs.GetComponentStructType(evTypeName);
            var evPtrType = LLVMTypeRef.CreatePointer(evStructType, 0);

            var rCountSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)(evBaseOffset + 0), "ev_rcount_slot");
            var rDataSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)(evBaseOffset + 2), "ev_rdata_slot");

            var rCount = builder.BuildLoad2(context.Int32Type, rCountSlot, "ev_rcount");
            var rDataRaw = builder.BuildLoad2(i8PtrType, rDataSlot, "ev_rdata_raw");
            var rDataTyped = builder.BuildBitCast(rDataRaw, evPtrType, "ev_rdata_typed");

            var evIdxAlloca = builder.BuildAlloca(context.Int32Type, "ev_idx");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), evIdxAlloca);

            var evCondBB = sysFunc.AppendBasicBlock("ev_cond");
            var evBodyBB = sysFunc.AppendBasicBlock("ev_body");
            var evIncBB = sysFunc.AppendBasicBlock("ev_inc");
            var evExitBB = sysFunc.AppendBasicBlock("ev_exit");

            builder.BuildBr(evCondBB);

            // ev_cond
            builder.PositionAtEnd(evCondBB);
            var curEvIdx = builder.BuildLoad2(context.Int32Type, evIdxAlloca, "cur_ev_idx");
            var hasMoreEvs = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curEvIdx, rCount, "has_more_evs");
            builder.BuildCondBr(hasMoreEvs, evBodyBB, evExitBB);

            // ev_body
            builder.PositionAtEnd(evBodyBB);
            var evElemPtr = builder.BuildInBoundsGEP2(evStructType, rDataTyped, new[] { curEvIdx }, "ev_elem");

            var evLocals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
            var evVarTypes = new Dictionary<string, string>(StringComparer.Ordinal);

            evLocals[evParam.Name] = evElemPtr;
            evVarTypes[evParam.Name] = evTypeName;

            evLocals["world"] = worldAlloca;
            evVarTypes["world"] = "World";

            // Bind any resources specified in read(...)
            for (int r = 1; r < sys.ReadParams.Count; r++)
            {
                var rp = sys.ReadParams[r];
                if (_typeChecker.Resources.ContainsKey(rp.TypeName))
                {
                    int resOffset = ecs.GetResourceOffset(rp.TypeName);
                    var resSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)resOffset, $"{rp.Name}_res_slot");
                    evLocals[rp.Name] = resSlot;
                    evVarTypes[rp.Name] = rp.TypeName;
                }
            }

            CompileBlock(context, module, builder, sysFunc, sys.Body, evLocals, evVarTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(evIncBB);
            }

            // ev_inc
            builder.PositionAtEnd(evIncBB);
            var nextEvIdx = builder.BuildAdd(curEvIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_ev_idx");
            builder.BuildStore(nextEvIdx, evIdxAlloca);
            builder.BuildBr(evCondBB);

            // ev_exit
            builder.PositionAtEnd(evExitBB);
            builder.BuildRetVoid();
            return;
        }

        // Compute required query component mask
        ulong requiredMask = 0;
        foreach (var qp in sys.QueryParams)
        {
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                requiredMask |= ecs.GetComponentMask(qp.TypeName);
            }
        }
        var reqMaskVal = LLVMValueRef.CreateConstInt(context.Int64Type, requiredMask);

        // Outer loop: iterate over all archetypes
        var archCountSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, 0, "world_arch_count_slot");
        var numArchs = builder.BuildLoad2(context.Int32Type, archCountSlot, "num_archs");
        var archIdxAlloca = builder.BuildAlloca(context.Int32Type, "arch_idx");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), archIdxAlloca);

        var archCondBB = sysFunc.AppendBasicBlock("arch_cond");
        var archBodyBB = sysFunc.AppendBasicBlock("arch_body");
        var nextArchBB = sysFunc.AppendBasicBlock("next_arch");
        var sysExitBB = sysFunc.AppendBasicBlock("sys_exit");

        builder.BuildBr(archCondBB);

        // arch_cond: while (arch_idx < num_archs)
        builder.PositionAtEnd(archCondBB);
        var curArchIdx = builder.BuildLoad2(context.Int32Type, archIdxAlloca, "cur_arch_idx");
        var hasMoreArchs = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdx, numArchs, "has_more_archs");
        builder.BuildCondBr(hasMoreArchs, archBodyBB, sysExitBB);

        // arch_body: check if archetype matches requiredMask
        builder.PositionAtEnd(archBodyBB);
        var archPtrType = LLVMTypeRef.CreatePointer(ecs.GetArchetypeStructType(), 0);
        var archTablesSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, 2, "world_arch_tables_slot");
        var tablesBase = builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var curArchPtr = builder.BuildInBoundsGEP2(ecs.GetArchetypeStructType(), tablesBase, new[] { curArchIdx }, "cur_arch_ptr");

        var maskSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 0, "mask_slot");
        var archMask = builder.BuildLoad2(context.Int64Type, maskSlot, "arch_mask");

        var andMask = builder.BuildAnd(archMask, reqMaskVal, "and_mask");
        var isMatch = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andMask, reqMaskVal, "is_match");

        var checkCountBB = sysFunc.AppendBasicBlock("check_count");
        builder.BuildCondBr(isMatch, checkCountBB, nextArchBB);

        // check_count: if (arch.count > 0)
        builder.PositionAtEnd(checkCountBB);
        var cntSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 1, "cnt_slot");
        var archCount = builder.BuildLoad2(context.Int32Type, cntSlot, "arch_count");
        var hasEntities = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, archCount, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "has_entities");

        var entLoopHeaderBB = sysFunc.AppendBasicBlock("ent_loop_header");
        builder.BuildCondBr(hasEntities, entLoopHeaderBB, nextArchBB);

        // entLoopHeaderBB: setup row = 0
        builder.PositionAtEnd(entLoopHeaderBB);
        var rowAlloca = builder.BuildAlloca(context.Int32Type, "row");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), rowAlloca);

        var entLoopCondBB = sysFunc.AppendBasicBlock("ent_loop_cond");
        var entLoopBodyBB = sysFunc.AppendBasicBlock("ent_loop_body");

        builder.BuildBr(entLoopCondBB);

        // ent_loop_cond: while (row < arch.count)
        builder.PositionAtEnd(entLoopCondBB);
        var curRow = builder.BuildLoad2(context.Int32Type, rowAlloca, "cur_row");
        var hasMoreRows = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curRow, archCount, "has_more_rows");
        builder.BuildCondBr(hasMoreRows, entLoopBodyBB, nextArchBB);

        // ent_loop_body
        builder.PositionAtEnd(entLoopBodyBB);
        var colsArrGEP = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 4, "cols_arr");

        var locals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
        var varTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        locals["world"] = worldAlloca;
        varTypes["world"] = "World";

        foreach (var qp in sys.QueryParams)
        {
            varTypes[qp.Name] = qp.TypeName;
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                int compId = ecs.GetComponentId(qp.TypeName);
                var compStructType = ecs.GetComponentStructType(qp.TypeName);
                var colSlot = builder.BuildInBoundsGEP2(ecs.GetColumnsArrayType(), colsArrGEP, new[]
                {
                    LLVMValueRef.CreateConstInt(context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)compId)
                }, $"{qp.Name}_col_slot");
                var colRaw = builder.BuildLoad2(i8PtrType, colSlot, $"{qp.Name}_raw");
                var colTyped = builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(compStructType, 0), $"{qp.Name}_col");
                var elemPtr = builder.BuildInBoundsGEP2(compStructType, colTyped, new[] { curRow }, $"{qp.Name}_elem");
                locals[qp.Name] = elemPtr;
            }
            else if (_typeChecker.Resources.ContainsKey(qp.TypeName))
            {
                int resOffset = ecs.GetResourceOffset(qp.TypeName);
                var resSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, (uint)resOffset, $"{qp.Name}_res_slot");
                locals[qp.Name] = resSlot;
            }
        }

        // Compile statements inside system body
        CompileBlock(context, module, builder, sysFunc, sys.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

        // Advance row: row++
        var nextRow = builder.BuildAdd(curRow, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_row");
        builder.BuildStore(nextRow, rowAlloca);
        builder.BuildBr(entLoopCondBB);

        // next_arch: arch_idx++
        builder.PositionAtEnd(nextArchBB);
        var nextArchIdx = builder.BuildAdd(curArchIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_arch_idx");
        builder.BuildStore(nextArchIdx, archIdxAlloca);
        builder.BuildBr(archCondBB);

        // sys_exit
        builder.PositionAtEnd(sysExitBB);
        builder.BuildRetVoid();

        // Generate Win32 Threadpool callback wrapper:
        // VOID CALLBACK job_{sys.Name}(PTP_CALLBACK_INSTANCE Instance, PVOID Context, PTP_WORK Work)
        var jobCallbackType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType, i8PtrType }, false);
        var jobFunc = module.AddFunction($"job_{sys.Name}", jobCallbackType);
        var jobBB = jobFunc.AppendBasicBlock("entry");
        var jobBuilder = context.CreateBuilder();
        jobBuilder.PositionAtEnd(jobBB);
        var contextArg = jobFunc.GetParam(1); // Context is pointer to world
        var worldArgTyped = jobBuilder.BuildBitCast(contextArg, worldPtrType, "world_typed");
        jobBuilder.BuildCall2(sysFuncType, sysFunc, new[] { worldArgTyped });
        jobBuilder.BuildRetVoid();
    }

    private void CompilePipeline(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        PipelineDeclaration pipe,
        EcsRuntimeEmitter ecs)
    {
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var pipeFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var pipeFunc = module.AddFunction($"pipeline_{pipe.Name}", pipeFuncType);
        var worldParam = pipeFunc.GetParam(0);
        worldParam.Name = "world";
        var entryBB = pipeFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        // Automatically swap event buffers at start of pipeline execution only if no explicit swap_events is in the pipeline
        bool hasExplicitSwap = pipe.Stages.Any(s => s.Actions.Any(a => a is SwapEventsAction));
        var swapFuncInit = module.GetNamedFunction("world_swap_events");
        if (!hasExplicitSwap && swapFuncInit.Handle != IntPtr.Zero)
        {
            var swapFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
            builder.BuildCall2(swapFuncType, swapFuncInit, new[] { worldParam });
        }

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        foreach (var stage in pipe.Stages)
        {
            foreach (var action in stage.Actions)
            {
                if (action is SystemCallAction call)
                {
                    var sysFunc = module.GetNamedFunction($"system_{call.SystemName}");
                    if (sysFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                    }
                }
                else if (action is ParallelAction par)
                {
                    if (par.Systems.Count == 1)
                    {
                        var sysFunc = module.GetNamedFunction($"system_{par.Systems[0].SystemName}");
                        if (sysFunc.Handle != IntPtr.Zero)
                        {
                            var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                            builder.BuildCall2(voidFuncType, sysFunc, new[] { worldParam });
                        }
                    }
                    else if (par.Systems.Count > 1)
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

                        foreach (var sCall in par.Systems)
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

                        // Wait for all parallel jobs to finish and close handles
                        foreach (var workHandle in workHandles)
                        {
                            builder.BuildCall2(waitWorkType, waitWorkFunc, new[] { workHandle, LLVMValueRef.CreateConstInt(context.Int32Type, 0) });
                            builder.BuildCall2(closeWorkType, closeWorkFunc, new[] { workHandle });
                        }
                    }
                }
                else if (action is SortHierarchyAction)
                {
                    var sortFunc = module.GetNamedFunction("world_sort_hierarchy");
                    if (sortFunc.Handle != IntPtr.Zero)
                    {
                        var sortFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(sortFuncType, sortFunc, new[] { worldParam });
                    }
                }
                else if (action is SwapEventsAction)
                {
                    var swapFunc = module.GetNamedFunction("world_swap_events");
                    if (swapFunc.Handle != IntPtr.Zero)
                    {
                        var swapFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(swapFuncType, swapFunc, new[] { worldParam });
                    }
                }
            }
        }

        builder.BuildRetVoid();
    }

    private void CompileFunction(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        FunctionDeclaration fnDecl,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var returnType = MapType(context, fnDecl.ReturnType, ecs);
        var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName, ecs)).ToArray();
        var function = module.GetNamedFunction(fnDecl.Name);

        var entryBlock = function.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBlock);

        var locals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
        var varTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Allocate function parameters
        for (int i = 0; i < fnDecl.Parameters.Count; i++)
        {
            var p = fnDecl.Parameters[i];
            var pVal = function.GetParam((uint)i);
            var alloca = builder.BuildAlloca(paramTypes[i], p.Name);
            builder.BuildStore(pVal, alloca);
            locals[p.Name] = alloca;
            varTypes[p.Name] = p.TypeName;
        }

        bool isMain = fnDecl.Name == "main";
        bool hasWaitKey = isMain && ContainsWaitKey(fnDecl.Body);

        CompileBlock(context, module, builder, function, fnDecl.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);

        // Ensure terminating return if not explicitly present
        var lastBlock = builder.InsertBlock;
        if (lastBlock.Terminator.Handle == IntPtr.Zero)
        {
            if (isMain && !hasWaitKey)
            {
                var msg = builder.BuildGlobalStringPtr("Press Enter to exit...", "prompt_exit");
                builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_exit");
                var getcharFunc = module.GetNamedFunction("getchar");
                var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "auto_wait_key");
            }
            if (returnType == context.VoidType)
                builder.BuildRetVoid();
            else
                builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int32Type, 0, false));
        }
    }

    private static bool ContainsWaitKey(BlockStatement block)
    {
        foreach (var s in block.Statements)
        {
            if (s is ExpressionStatement es && es.Expression is CallExpression c && c.Callee is "wait_key" or "readln" or "init_window" or "rl_init_window")
                return true;
            if (s is IfStatement ifStmt)
            {
                if (ContainsWaitKey(ifStmt.ThenBranch)) return true;
                if (ifStmt.ElseBranch is BlockStatement eb && ContainsWaitKey(eb)) return true;
            }
            if (s is WhileStatement ws && ContainsWaitKey(ws.Body)) return true;
            if (s is ForStatement fs && ContainsWaitKey(fs.Body)) return true;
        }
        return false;
    }

    private void CompileBlock(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        BlockStatement block,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        bool isMain = false,
        bool hasWaitKey = false)
    {
        foreach (var stmt in block.Statements)
        {
            if (builder.InsertBlock.Terminator.Handle != IntPtr.Zero)
                break;

            switch (stmt)
            {
                case VariableDeclarationStatement varDecl:
                    var tName = varDecl.TypeName ?? _typeChecker.GetNodeType(varDecl.Initializer).Name;
                    var varType = MapType(context, tName, ecs);
                    if (!locals.TryGetValue(varDecl.Name, out var alloca))
                    {
                        alloca = CreateEntryBlockAlloca(context, function, varType, varDecl.Name);
                        locals[varDecl.Name] = alloca;
                    }
                    var initVal = CompileExpression(context, module, builder, function, varDecl.Initializer, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    builder.BuildStore(initVal, alloca);
                    varTypes[varDecl.Name] = tName;
                    break;

                case AssignmentStatement assign:
                    if (locals.TryGetValue(assign.TargetName, out var targetPtr))
                    {
                        var newVal = CompileExpression(context, module, builder, function, assign.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        LLVMValueRef destPtr;
                        if (assign.MemberName != null && varTypes.TryGetValue(assign.TargetName, out var targetTypeName))
                        {
                            var structType = ecs.GetComponentStructType(targetTypeName);
                            int fieldOffset = ecs.GetFieldOffset(targetTypeName, assign.MemberName);
                            var fieldGEP = builder.BuildStructGEP2(structType, targetPtr, (uint)fieldOffset, $"{assign.TargetName}_{assign.MemberName}_gep");

                            if (assign.Index != null)
                            {
                                var memType = _typeChecker.GetMemberType(targetTypeName, assign.MemberName, assign.Span);
                                var fieldArrType = MapType(context, memType.Name, ecs);
                                var idxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                                var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                                destPtr = builder.BuildInBoundsGEP2(fieldArrType, fieldGEP, new[] { zero, idxVal }, $"{assign.TargetName}_{assign.MemberName}_elem_gep");
                            }
                            else
                            {
                                destPtr = fieldGEP;
                            }
                        }
                        else
                        {
                            if (assign.Index != null && varTypes.TryGetValue(assign.TargetName, out var arrTypeName))
                            {
                                var arrType = MapType(context, arrTypeName, ecs);
                                var idxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                                var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                                destPtr = builder.BuildInBoundsGEP2(arrType, targetPtr, new[] { zero, idxVal }, $"{assign.TargetName}_elem_gep");
                            }
                            else
                            {
                                destPtr = targetPtr;
                            }
                        }

                        if (assign.Op == AssignmentOperator.Assign)
                        {
                            builder.BuildStore(newVal, destPtr);
                        }
                        else
                        {
                            var currentVal = builder.BuildLoad2(newVal.TypeOf, destPtr, "cur_val");
                            var resVal = assign.Op switch
                            {
                                AssignmentOperator.PlusAssign => newVal.TypeOf == context.FloatType
                                    ? builder.BuildFAdd(currentVal, newVal, "fadd")
                                    : builder.BuildAdd(currentVal, newVal, "add"),
                                AssignmentOperator.MinusAssign => newVal.TypeOf == context.FloatType
                                    ? builder.BuildFSub(currentVal, newVal, "fsub")
                                    : builder.BuildSub(currentVal, newVal, "sub"),
                                AssignmentOperator.MulAssign => newVal.TypeOf == context.FloatType
                                    ? builder.BuildFMul(currentVal, newVal, "fmul")
                                    : builder.BuildMul(currentVal, newVal, "mul"),
                                AssignmentOperator.DivAssign => newVal.TypeOf == context.FloatType
                                    ? builder.BuildFDiv(currentVal, newVal, "fdiv")
                                    : builder.BuildSDiv(currentVal, newVal, "div"),
                                _ => newVal
                            };
                            builder.BuildStore(resVal, destPtr);
                        }
                    }
                    break;

                case IfStatement ifStmt:
                    var condVal = CompileExpression(context, module, builder, function, ifStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var thenBlock = function.AppendBasicBlock("then");
                    var elseBlock = ifStmt.ElseBranch != null ? function.AppendBasicBlock("else") : default;
                    var mergeBlock = function.AppendBasicBlock("if_merge");

                    var falseDest = ifStmt.ElseBranch != null ? elseBlock : mergeBlock;
                    builder.BuildCondBr(condVal, thenBlock, falseDest);

                    builder.PositionAtEnd(thenBlock);
                    CompileBlock(context, module, builder, function, ifStmt.ThenBranch, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(mergeBlock);

                    if (ifStmt.ElseBranch != null)
                    {
                        builder.PositionAtEnd(elseBlock);
                        if (ifStmt.ElseBranch is BlockStatement elseStmtBlock)
                            CompileBlock(context, module, builder, function, elseStmtBlock, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);

                        if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                            builder.BuildBr(mergeBlock);
                    }

                    builder.PositionAtEnd(mergeBlock);
                    break;

                case WhileStatement whileStmt:
                    var whileCondBB = function.AppendBasicBlock("while_cond");
                    var whileBodyBB = function.AppendBasicBlock("while_body");
                    var whileExitBB = function.AppendBasicBlock("while_exit");

                    builder.BuildBr(whileCondBB);

                    builder.PositionAtEnd(whileCondBB);
                    var loopCondVal = CompileExpression(context, module, builder, function, whileStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    builder.BuildCondBr(loopCondVal, whileBodyBB, whileExitBB);

                    builder.PositionAtEnd(whileBodyBB);
                    CompileBlock(context, module, builder, function, whileStmt.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(whileCondBB);

                    builder.PositionAtEnd(whileExitBB);
                    break;

                case ForStatement forStmt:
                    var startVal = CompileExpression(context, module, builder, function, forStmt.Start, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var endVal = CompileExpression(context, module, builder, function, forStmt.End, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                    var loopVarAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, forStmt.VariableName);
                    builder.BuildStore(startVal, loopVarAlloca);

                    var oldLocal = locals.TryGetValue(forStmt.VariableName, out var prevLocal) ? prevLocal : default;
                    var oldType = varTypes.TryGetValue(forStmt.VariableName, out var prevType) ? prevType : null;

                    locals[forStmt.VariableName] = loopVarAlloca;
                    varTypes[forStmt.VariableName] = "i32";

                    var forCondBB = function.AppendBasicBlock("for_cond");
                    var forBodyBB = function.AppendBasicBlock("for_body");
                    var forIncBB = function.AppendBasicBlock("for_inc");
                    var forExitBB = function.AppendBasicBlock("for_exit");

                    builder.BuildBr(forCondBB);

                    // for_cond: while (i < end)
                    builder.PositionAtEnd(forCondBB);
                    var curVal = builder.BuildLoad2(context.Int32Type, loopVarAlloca, forStmt.VariableName);
                    var cmpVal = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curVal, endVal, "for_cmp");
                    builder.BuildCondBr(cmpVal, forBodyBB, forExitBB);

                    // for_body
                    builder.PositionAtEnd(forBodyBB);
                    CompileBlock(context, module, builder, function, forStmt.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(forIncBB);

                    // for_inc: i++
                    builder.PositionAtEnd(forIncBB);
                    var curValInc = builder.BuildLoad2(context.Int32Type, loopVarAlloca, "for_cur");
                    var nextVal = builder.BuildAdd(curValInc, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "for_next");
                    builder.BuildStore(nextVal, loopVarAlloca);
                    builder.BuildBr(forCondBB);

                    // for_exit
                    builder.PositionAtEnd(forExitBB);

                    if (oldType != null)
                    {
                        locals[forStmt.VariableName] = oldLocal;
                        varTypes[forStmt.VariableName] = oldType;
                    }
                    else
                    {
                        locals.Remove(forStmt.VariableName);
                        varTypes.Remove(forStmt.VariableName);
                    }
                    break;

                case ReturnStatement retStmt:
                    if (isMain && !hasWaitKey)
                    {
                        var msg = builder.BuildGlobalStringPtr("Press Enter to exit...", "prompt_exit");
                        builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_exit");
                        var getcharFunc = module.GetNamedFunction("getchar");
                        var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                        builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "auto_wait_key");
                    }
                    if (retStmt.Value != null)
                    {
                        var retVal = CompileExpression(context, module, builder, function, retStmt.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        builder.BuildRet(retVal);
                    }
                    else
                    {
                        builder.BuildRetVoid();
                    }
                    break;

                case MatchStatement matchStmt:
                    CompileMatchStatement(context, module, builder, function, matchStmt, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    break;

                case ExpressionStatement exprStmt:
                    CompileExpression(context, module, builder, function, exprStmt.Expression, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    break;
            }
        }
    }

    private void CompileMatchStatement(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        MatchStatement match,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        bool isMain,
        bool hasWaitKey)
    {
        var scrutineeVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        scrutineeVal = EnsureInt32(context, builder, scrutineeVal, "match_scrut");

        var mergeBB = function.AppendBasicBlock("match_merge");
        var defaultBB = function.AppendBasicBlock("match_default");

        var nonWildcardArms = new List<(int Value, MatchArm Arm)>();
        MatchArm? wildcardArm = null;

        foreach (var arm in match.Arms)
        {
            if (arm.Pattern is WildcardExpression)
            {
                wildcardArm = arm;
            }
            else if (arm.Pattern is NumberLiteralExpression num)
            {
                int val = int.Parse(num.RawValue);
                nonWildcardArms.Add((val, arm));
            }
            else if (arm.Pattern is MemberAccessExpression mem && mem.Target is IdentifierExpression id && _typeChecker.Enums.TryGetValue(id.Name, out var eSym))
            {
                if (eSym.Members.TryGetValue(mem.MemberName, out var mSym))
                {
                    nonWildcardArms.Add((mSym.Value, arm));
                }
            }
        }

        var switchInst = builder.BuildSwitch(scrutineeVal, defaultBB, (uint)nonWildcardArms.Count);

        foreach (var (val, arm) in nonWildcardArms)
        {
            var armBB = function.AppendBasicBlock($"match_arm_{val}");
            switchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)val, false), armBB);

            builder.PositionAtEnd(armBB);
            CompileBlock(context, module, builder, function, arm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(mergeBB);
            }
        }

        // Default / wildcard block
        builder.PositionAtEnd(defaultBB);
        if (wildcardArm != null)
        {
            CompileBlock(context, module, builder, function, wildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
        }
        if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
        {
            builder.BuildBr(mergeBB);
        }

        builder.PositionAtEnd(mergeBB);
    }

    private unsafe LLVMValueRef CompileExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        ExpressionNode expr,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        switch (expr)
        {
            case NumberLiteralExpression num:
                if (num.IsFloatingPoint)
                {
                    double dVal = double.Parse(num.RawValue, System.Globalization.CultureInfo.InvariantCulture);
                    return LLVMValueRef.CreateConstReal(context.FloatType, dVal);
                }
                else
                {
                    long iVal = long.Parse(num.RawValue);
                    return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)iVal, false);
                }

            case BooleanLiteralExpression b:
                return LLVMValueRef.CreateConstInt(context.Int1Type, b.Value ? 1UL : 0UL, false);

            case StringLiteralExpression str:
                return builder.BuildGlobalStringPtr(str.Value, "str_lit");

            case IdentifierExpression ident:
                if (locals.TryGetValue(ident.Name, out var varPtr))
                {
                    var varType = _typeChecker.GetNodeType(ident);
                    var llvmType = MapType(context, varType.Name, ecs);
                    return builder.BuildLoad2(llvmType, varPtr, ident.Name);
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case MemberAccessExpression mem:
                if (mem.Target is IdentifierExpression enumId && _typeChecker.Enums.TryGetValue(enumId.Name, out var enumSym))
                {
                    if (enumSym.Members.TryGetValue(mem.MemberName, out var mSym))
                    {
                        return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)mSym.Value, false);
                    }
                }
                if (mem.Target is IdentifierExpression targetId && locals.TryGetValue(targetId.Name, out var structPtr))
                {
                    if (varTypes.TryGetValue(targetId.Name, out var structTypeName))
                    {
                        var structType = ecs.GetComponentStructType(structTypeName);
                        int offset = ecs.GetFieldOffset(structTypeName, mem.MemberName);
                        var fieldGEP = builder.BuildStructGEP2(structType, structPtr, (uint)offset, $"{targetId.Name}_{mem.MemberName}");
                        var fieldType = MapType(context, _typeChecker.GetNodeType(mem).Name, ecs);
                        return builder.BuildLoad2(fieldType, fieldGEP, $"{mem.MemberName}_val");
                    }
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case ArrayLiteralExpression arrLit:
                var arrTypeSym = _typeChecker.GetNodeType(arrLit);
                var llvmArrType = MapType(context, arrTypeSym.Name, ecs);
                var arrAlloca = CreateEntryBlockAlloca(context, function, llvmArrType, "arr_lit");
                var zeroConst = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                for (int i = 0; i < arrLit.Elements.Count; i++)
                {
                    var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                    var elemGEP = builder.BuildInBoundsGEP2(llvmArrType, arrAlloca, new[] { zeroConst, idxVal }, $"arr_elem_{i}");
                    builder.BuildStore(elemVal, elemGEP);
                }
                return builder.BuildLoad2(llvmArrType, arrAlloca, "arr_lit_val");

            case IndexExpression idxExpr:
                var elemType = MapType(context, _typeChecker.GetNodeType(idxExpr).Name, ecs);
                var indexVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                var zeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                if (idxExpr.Target is IdentifierExpression idxTargetId && locals.TryGetValue(idxTargetId.Name, out var targetArrPtr))
                {
                    var arrTypeName = varTypes[idxTargetId.Name];
                    var arrType = MapType(context, arrTypeName, ecs);
                    var elemGEP = builder.BuildInBoundsGEP2(arrType, targetArrPtr, new[] { zeroIdx, indexVal }, $"{idxTargetId.Name}_idx_gep");
                    return builder.BuildLoad2(elemType, elemGEP, $"{idxTargetId.Name}_elem_val");
                }
                else if (idxExpr.Target is MemberAccessExpression memAccess && memAccess.Target is IdentifierExpression memTargetId && locals.TryGetValue(memTargetId.Name, out var structPtr2))
                {
                    if (varTypes.TryGetValue(memTargetId.Name, out var structTypeName))
                    {
                        var structType = ecs.GetComponentStructType(structTypeName);
                        int offset = ecs.GetFieldOffset(structTypeName, memAccess.MemberName);
                        var fieldGEP = builder.BuildStructGEP2(structType, structPtr2, (uint)offset, $"{memTargetId.Name}_{memAccess.MemberName}");
                        var memType = _typeChecker.GetMemberType(structTypeName, memAccess.MemberName, memAccess.Span);
                        var fieldArrType = MapType(context, memType.Name, ecs);
                        var elemGEP = builder.BuildInBoundsGEP2(fieldArrType, fieldGEP, new[] { zeroIdx, indexVal }, $"{memAccess.MemberName}_idx_gep");
                        return builder.BuildLoad2(elemType, elemGEP, $"{memAccess.MemberName}_elem_val");
                    }
                }

                // Fallback: evaluate target expression into temporary
                var targetValExpr = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tmpArrAlloca = CreateEntryBlockAlloca(context, function, targetValExpr.TypeOf, "tmp_idx_arr");
                builder.BuildStore(targetValExpr, tmpArrAlloca);
                var fallbackElemGEP = builder.BuildInBoundsGEP2(targetValExpr.TypeOf, tmpArrAlloca, new[] { zeroIdx, indexVal }, "tmp_elem_gep");
                return builder.BuildLoad2(elemType, fallbackElemGEP, "tmp_elem_val");

            case MethodCallExpression methodCall:
                var targetVal = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                string mTargetName = $"world_{methodCall.MethodName}";

                if (methodCall.MethodName == "emit" && methodCall.Arguments.Count == 1)
                {
                    if (methodCall.Arguments[0] is CallExpression ctorCall)
                    {
                        mTargetName = $"world_emit_{ctorCall.Callee}";
                        var ctorFunc = module.GetNamedFunction(mTargetName);
                        if (ctorFunc.Handle != IntPtr.Zero)
                        {
                            var ctorArgs = new List<LLVMValueRef> { targetVal };
                            foreach (var arg in ctorCall.Arguments)
                            {
                                ctorArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                            }
                            var ctorFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ctorFunc);
                            return builder.BuildCall2(ctorFuncType, ctorFunc, ctorArgs.ToArray(), "");
                        }
                    }
                }

                var mFunc = module.GetNamedFunction(mTargetName);
                if (mFunc.Handle == IntPtr.Zero)
                {
                    _diagnostics.ReportError($"Undefined ECS world method '{methodCall.MethodName}'.", methodCall.Span);
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                var mArgs = new List<LLVMValueRef> { targetVal };
                foreach (var arg in methodCall.Arguments)
                {
                    mArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                }

                var mFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(mFunc);
                string mCallName = mFuncType.ReturnType == context.VoidType ? "" : $"{methodCall.MethodName}_call";
                return builder.BuildCall2(mFuncType, mFunc, mArgs.ToArray(), mCallName);

            case UnaryExpression un:
                var operand = CompileExpression(context, module, builder, function, un.Operand, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                return un.Operator switch
                {
                    UnaryOperator.Negate => operand.TypeOf == context.FloatType
                        ? builder.BuildFNeg(operand, "fneg_tmp")
                        : builder.BuildNeg(operand, "neg_tmp"),
                    UnaryOperator.LogicalNot => builder.BuildNot(operand, "not_tmp"),
                    _ => operand
                };

            case BinaryExpression bin:
                var left = CompileExpression(context, module, builder, function, bin.Left, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var right = CompileExpression(context, module, builder, function, bin.Right, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                bool isFloat = left.TypeOf == context.FloatType || right.TypeOf == context.FloatType;

                return bin.Operator switch
                {
                    BinaryOperator.Add => isFloat ? builder.BuildFAdd(left, right, "fadd") : builder.BuildAdd(left, right, "add"),
                    BinaryOperator.Subtract => isFloat ? builder.BuildFSub(left, right, "fsub") : builder.BuildSub(left, right, "sub"),
                    BinaryOperator.Multiply => isFloat ? builder.BuildFMul(left, right, "fmul") : builder.BuildMul(left, right, "mul"),
                    BinaryOperator.Divide => isFloat ? builder.BuildFDiv(left, right, "fdiv") : builder.BuildSDiv(left, right, "sdiv"),
                    BinaryOperator.Modulo => builder.BuildSRem(left, right, "srem"),

                    BinaryOperator.Equal => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOEQ, left, right, "feq")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, left, right, "eq"),
                    BinaryOperator.NotEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealONE, left, right, "fne")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, left, right, "ne"),
                    BinaryOperator.Less => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, left, right, "flt")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, left, right, "slt"),
                    BinaryOperator.LessOrEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLE, left, right, "fle")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, left, right, "sle"),
                    BinaryOperator.Greater => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, left, right, "fgt")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, left, right, "sgt"),
                    BinaryOperator.GreaterOrEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGE, left, right, "fge")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, left, right, "sge"),

                    BinaryOperator.LogicalAnd => builder.BuildAnd(left, right, "land"),
                    BinaryOperator.LogicalOr => builder.BuildOr(left, right, "lor"),
                    _ => left
                };

            case CallExpression call:
                if (call.Callee is "println" or "print")
                {
                    bool addNewline = call.Callee == "println";
                    if (call.Arguments.Count > 0)
                    {
                        var arg = call.Arguments[0];
                        var argType = _typeChecker.GetNodeType(arg);
                        var val = CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        if (argType == TypeSymbol.String)
                        {
                            if (call.Arguments.Count > 1)
                            {
                                var printfArgs = new List<LLVMValueRef> { val };
                                for (int i = 1; i < call.Arguments.Count; i++)
                                {
                                    var pArg = call.Arguments[i];
                                    var pArgType = _typeChecker.GetNodeType(pArg);
                                    var pVal = CompileExpression(context, module, builder, function, pArg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                                    if (pArgType.IsFloatingPoint)
                                    {
                                        pVal = builder.BuildFPExt(pVal, context.DoubleType, "f_to_d");
                                    }
                                    printfArgs.Add(pVal);
                                }
                                var res = builder.BuildCall2(printfType, printfFunc, printfArgs.ToArray(), "printf_call");
                                if (addNewline)
                                {
                                    var nlStr = builder.BuildGlobalStringPtr("\n", "nl_s");
                                    builder.BuildCall2(printfType, printfFunc, new[] { nlStr }, "printf_nl");
                                }
                                return res;
                            }
                            else if (addNewline)
                            {
                                return builder.BuildCall2(putsType, putsFunc, new[] { val }, "puts_call");
                            }
                            else
                            {
                                var fmt = builder.BuildGlobalStringPtr("%s", "fmt_s");
                                return builder.BuildCall2(printfType, printfFunc, new[] { fmt, val }, "printf_call");
                            }
                        }
                        else if (argType.IsFloatingPoint)
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%f\n" : "%f", "fmt_f");
                            var doubleVal = builder.BuildFPExt(val, context.DoubleType, "f_to_d");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, doubleVal }, "printf_call");
                        }
                        else if (argType == TypeSymbol.Bool)
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%d\n" : "%d", "fmt_b");
                            var i32Val = builder.BuildZExt(val, context.Int32Type, "b_to_i32");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, i32Val }, "printf_call");
                        }
                        else
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%d\n" : "%d", "fmt_d");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, val }, "printf_call");
                        }
                    }
                    else if (addNewline)
                    {
                        var emptyStr = builder.BuildGlobalStringPtr("", "empty_s");
                        return builder.BuildCall2(putsType, putsFunc, new[] { emptyStr }, "puts_call");
                    }
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
                }
                else if (call.Callee is "wait_key" or "readln")
                {
                    var getcharFunc = module.GetNamedFunction("getchar");
                    var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "key_input");
                }
                else if (call.Callee is "get_tick_count" or "time_ms")
                {
                    var f = module.GetNamedFunction("GetTickCount");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "tick_count");
                }
                else if (call.Callee is "ecs::create_world" or "create_world")
                {
                    var createWorldFunc = module.GetNamedFunction("ecs_create_world");
                    var createWorldType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0), Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(createWorldType, createWorldFunc, Array.Empty<LLVMValueRef>(), "new_world");
                }
                else if (call.Callee is "init_window" or "rl_init_window")
                {
                    var f = module.GetNamedFunction("InitWindow");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, i8PtrType }, false);
                    var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var h = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var title = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { w, h, title }, "");
                }
                else if (call.Callee is "window_should_close" or "rl_window_should_close")
                {
                    var f = module.GetNamedFunction("WindowShouldClose");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "should_close");
                }
                else if (call.Callee is "close_window" or "rl_close_window")
                {
                    var f = module.GetNamedFunction("CloseWindow");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "set_target_fps" or "rl_set_target_fps")
                {
                    var f = module.GetNamedFunction("SetTargetFPS");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                    var fps = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { fps }, "");
                }
                else if (call.Callee is "get_fps" or "rl_get_fps")
                {
                    var f = module.GetNamedFunction("GetFPS");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "fps");
                }
                else if (call.Callee is "get_frame_time" or "rl_get_frame_time")
                {
                    var f = module.GetNamedFunction("GetFrameTime");
                    var ft = LLVMTypeRef.CreateFunction(context.FloatType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "frame_time");
                }
                else if (call.Callee is "get_time" or "rl_get_time")
                {
                    var f = module.GetNamedFunction("GetTime");
                    var ft = LLVMTypeRef.CreateFunction(context.DoubleType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "cur_time");
                }
                else if (call.Callee is "begin_drawing" or "rl_begin_drawing")
                {
                    var f = module.GetNamedFunction("BeginDrawing");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "end_drawing" or "rl_end_drawing")
                {
                    var f = module.GetNamedFunction("EndDrawing");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "clear_background" or "rl_clear_background")
                {
                    var f = module.GetNamedFunction("ClearBackground");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 3)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return builder.BuildCall2(ft, f, new[] { colorVal }, "");
                }
                else if (call.Callee is "draw_rectangle" or "rl_draw_rectangle")
                {
                    var f = module.GetNamedFunction("DrawRectangle");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var x = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var w = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var h = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return builder.BuildCall2(ft, f, new[] { x, y, w, h, colorVal }, "");
                }
                else if (call.Callee is "draw_circle" or "rl_draw_circle")
                {
                    var f = module.GetNamedFunction("DrawCircle");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.FloatType, context.Int32Type }, false);
                    var cx = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var cy = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var rad = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 6)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return builder.BuildCall2(ft, f, new[] { cx, cy, rad, colorVal }, "");
                }
                else if (call.Callee is "draw_text" or "rl_draw_text")
                {
                    var f = module.GetNamedFunction("DrawText");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var txt = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var x = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var sz = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return builder.BuildCall2(ft, f, new[] { txt, x, y, sz, colorVal }, "");
                }
                else if (call.Callee is "draw_line" or "rl_draw_line")
                {
                    var f = module.GetNamedFunction("DrawLine");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var x1 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y1 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var x2 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y2 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return builder.BuildCall2(ft, f, new[] { x1, y1, x2, y2, colorVal }, "");
                }
                else if (call.Callee is "is_key_down" or "rl_is_key_down")
                {
                    var f = module.GetNamedFunction("IsKeyDown");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_down");
                }
                else if (call.Callee is "is_key_pressed" or "rl_is_key_pressed")
                {
                    var f = module.GetNamedFunction("IsKeyPressed");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_pressed");
                }
                else if (call.Callee is "is_key_released" or "rl_is_key_released")
                {
                    var f = module.GetNamedFunction("IsKeyReleased");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_rel");
                }
                else if (call.Callee is "is_key_up" or "rl_is_key_up")
                {
                    var f = module.GetNamedFunction("IsKeyUp");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_up");
                }
                else if (call.Callee is "get_mouse_x" or "rl_get_mouse_x")
                {
                    var f = module.GetNamedFunction("GetMouseX");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_x");
                }
                else if (call.Callee is "get_mouse_y" or "rl_get_mouse_y")
                {
                    var f = module.GetNamedFunction("GetMouseY");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_y");
                }
                else if (call.Callee is "is_mouse_button_down" or "rl_is_mouse_button_down")
                {
                    var f = module.GetNamedFunction("IsMouseButtonDown");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { btn }, "btn_down");
                }
                else if (call.Callee is "is_mouse_button_pressed" or "rl_is_mouse_button_pressed")
                {
                    var f = module.GetNamedFunction("IsMouseButtonPressed");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { btn }, "btn_pressed");
                }
                else if (call.Callee == "rl_color")
                {
                    var r = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var g = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var b = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var a = call.Arguments.Count > 3 ? CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc) : null;
                    return PackRgbaColor(context, builder, r, g, b, a);
                }
                else if (_typeChecker.Structs.TryGetValue(call.Callee, out var stSym))
                {
                    var stType = ecs.GetComponentStructType(call.Callee);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, stType, $"tmp_{call.Callee}");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        var argVal = CompileExpression(context, module, builder, function, call.Arguments[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fieldGEP = builder.BuildStructGEP2(stType, tmpAlloca, (uint)i, $"tmp_{call.Callee}_{stSym.Fields[i].Name}");
                        builder.BuildStore(argVal, fieldGEP);
                    }
                    return builder.BuildLoad2(stType, tmpAlloca, $"st_val_{call.Callee}");
                }
                else
                {
                    // Generic function call or ECS call (world_spawn, world_set_*, pipeline_*)
                    string funcName = call.Callee;
                    if (_typeChecker.Pipelines.Any(p => p.Name == call.Callee))
                    {
                        funcName = $"pipeline_{call.Callee}";
                    }
                    else if (_typeChecker.Systems.ContainsKey(call.Callee))
                    {
                        funcName = $"system_{call.Callee}";
                    }

                    var targetFunc = module.GetNamedFunction(funcName);
                    if (targetFunc.Handle == IntPtr.Zero)
                    {
                        _diagnostics.ReportError($"Undefined function or system '{funcName}'.", call.Span);
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    var argValues = call.Arguments.Select(a => CompileExpression(context, module, builder, function, a, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)).ToArray();
                    var targetFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(targetFunc);
                    string callName = targetFuncType.ReturnType == context.VoidType ? "" : $"{call.Callee}_call";
                    return builder.BuildCall2(targetFuncType, targetFunc, argValues, callName);
                }
        }

        return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
    }

    private LLVMTypeRef MapType(LLVMContextRef context, string? typeName, EcsRuntimeEmitter? ecs = null)
    {
        if (typeName != null && typeName.StartsWith("[") && typeName.EndsWith("]"))
        {
            var inner = typeName.Substring(1, typeName.Length - 2);
            var parts = inner.Split(';');
            if (parts.Length == 2 && uint.TryParse(parts[1].Trim(), out uint len))
            {
                var elemType = MapType(context, parts[0].Trim(), ecs);
                return LLVMTypeRef.CreateArray(elemType, len);
            }
        }

        if (typeName != null && _typeChecker.Structs.ContainsKey(typeName) && ecs != null)
        {
            return ecs.GetComponentStructType(typeName);
        }

        return typeName switch
        {
            "f32" or "float" => context.FloatType,
            "f64" or "double" => context.DoubleType,
            "i64" or "u64" => context.Int64Type,
            "i32" or "u32" or "int" => context.Int32Type,
            "bool" => context.Int1Type,
            "string" or "str" => LLVMTypeRef.CreatePointer(context.Int8Type, 0),
            "World" or "world" => ecs != null ? LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0) : LLVMTypeRef.CreatePointer(context.Int8Type, 0),
            "void" => context.VoidType,
            _ => context.Int32Type
        };
    }

    private static LLVMValueRef CreateEntryBlockAlloca(LLVMContextRef context, LLVMValueRef function, LLVMTypeRef type, string name)
    {
        var entryBB = function.EntryBasicBlock;
        using var tempBuilder = context.CreateBuilder();
        if (entryBB.FirstInstruction.Handle != IntPtr.Zero)
        {
            tempBuilder.PositionBefore(entryBB.FirstInstruction);
        }
        else
        {
            tempBuilder.PositionAtEnd(entryBB);
        }
        return tempBuilder.BuildAlloca(type, name);
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

    private static LLVMValueRef EnsureInt32(LLVMContextRef context, LLVMBuilderRef builder, LLVMValueRef val, string name = "to_i32")
    {
        if (val.TypeOf == context.FloatType || val.TypeOf == context.DoubleType)
            return builder.BuildFPToSI(val, context.Int32Type, name);
        if (val.TypeOf == context.Int64Type)
            return builder.BuildTrunc(val, context.Int32Type, name);
        return val;
    }

    private static LLVMValueRef EnsureFloat(LLVMContextRef context, LLVMBuilderRef builder, LLVMValueRef val, string name = "to_f32")
    {
        if (val.TypeOf == context.Int32Type || val.TypeOf == context.Int64Type)
            return builder.BuildSIToFP(val, context.FloatType, name);
        if (val.TypeOf == context.DoubleType)
            return builder.BuildFPTrunc(val, context.FloatType, name);
        return val;
    }
}
