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
    private CompilerOptions _options = new();
    private LLVMDIBuilderRef? _diBuilder;
    private LLVMMetadataRef _diCompileUnit;
    private LLVMMetadataRef _diFile;
    private LLVMMetadataRef? _currentSubprogram;
    private LLVMTargetDataRef _dataLayout;
    private HashMapEmitter? _mapEmitter;
    private StringArenaEmitter? _arenaEmitter;

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
        string? outputLlvmIrPath = null,
        CompilerOptions? options = null)
    {
        _options = options ?? new CompilerOptions();
        _diBuilder = null;
        _currentSubprogram = null;

        using var context = LLVMContextRef.Create();
        using var module = context.CreateModuleWithName("ecs_module");
        using var builder = context.CreateBuilder();

        string targetTriple = "x86_64-pc-windows-msvc";
        if (!LLVMTargetRef.TryGetTargetFromTriple(targetTriple, out var target, out var errorMessage))
        {
            _diagnostics.ReportError($"Failed to get LLVM target for '{targetTriple}': {errorMessage}", SourceSpan.None);
            return false;
        }

        var optLevel = _options.OptimizationLevel switch
        {
            OptimizationLevel.O0 => LLVMCodeGenOptLevel.LLVMCodeGenLevelNone,
            OptimizationLevel.O1 => LLVMCodeGenOptLevel.LLVMCodeGenLevelLess,
            OptimizationLevel.O2 => LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            OptimizationLevel.O3 => LLVMCodeGenOptLevel.LLVMCodeGenLevelAggressive,
            OptimizationLevel.Os => LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            OptimizationLevel.Oz => LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            _ => LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault
        };

        var targetMachine = target.CreateTargetMachine(
            targetTriple,
            "generic",
            "",
            optLevel,
            LLVMRelocMode.LLVMRelocDefault,
            LLVMCodeModel.LLVMCodeModelDefault);

        module.Target = targetTriple;
        var dataLayout = targetMachine.CreateTargetDataLayout();
        _dataLayout = dataLayout;
        sbyte* dataLayoutStr = LlvmApi.CopyStringRepOfTargetData(dataLayout);
        module.DataLayout = Marshal.PtrToStringAnsi((IntPtr)dataLayoutStr) ?? "";
        LlvmApi.DisposeMessage(dataLayoutStr);
        _mapEmitter = new HashMapEmitter(context, module, _dataLayout);
        _arenaEmitter = new StringArenaEmitter(context, module);

        // Debug Info Setup (CodeView / PDB on Windows)
        if (_options.GenerateDebugInfo)
        {
            module.AddModuleFlag("CodeView", LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, 1);
            module.AddModuleFlag("Debug Info Version", LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, 3);

            var diBuilder = module.CreateDIBuilder();
            _diBuilder = diBuilder;

            string sourceFile = !string.IsNullOrEmpty(program.Span.FilePath) ? program.Span.FilePath : "source.ecs";
            string fullPath = Path.GetFullPath(sourceFile);
            string fileName = Path.GetFileName(fullPath);
            string dirName = Path.GetDirectoryName(fullPath) ?? "";

            _diFile = diBuilder.CreateFile(fileName, dirName);
            _diCompileUnit = diBuilder.CreateCompileUnit(
                LLVMDWARFSourceLanguage.LLVMDWARFSourceLanguageC99,
                _diFile,
                "ECS-Lang",
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0,
                "",
                0,
                "",
                LLVMDWARFEmissionKind.LLVMDWARFEmissionFull,
                0,
                0,
                0,
                "",
                "");
        }

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

        // C runtime string & formatting functions
        var sprintfType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType, i8PtrType }, true);
        module.AddFunction("sprintf", sprintfType);

        var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
        module.AddFunction("strlen", strlenType);

        var strcmpType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType, i8PtrType }, false);
        module.AddFunction("strcmp", strcmpType);

        // C runtime math functions
        module.AddFunction("sqrtf", LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false));
        module.AddFunction("sinf", LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false));
        module.AddFunction("cosf", LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false));
        module.AddFunction("floorf", LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false));
        module.AddFunction("ceilf", LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false));
        module.AddFunction("rand", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));

        // Raylib C ABI declarations
        module.AddFunction("InitWindow", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, i8PtrType }, false));
        module.AddFunction("IsWindowReady", LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false));
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
        module.AddFunction("DrawRectangleLines", LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false));
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

        // Raylib Media C ABI declarations (Textures, Audio, Camera2D)
        module.AddFunction("LoadTexture", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false));
        module.AddFunction("DrawTexture", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type }, false));
        module.AddFunction("DrawTexturePro", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType, i8PtrType, context.Int64Type, context.FloatType, context.Int32Type }, false));
        module.AddFunction("UnloadTexture", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));

        module.AddFunction("InitAudioDevice", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("CloseAudioDevice", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("IsAudioDeviceReady", LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("LoadSound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false));
        module.AddFunction("PlaySound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("StopSound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("PauseSound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("ResumeSound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("IsSoundPlaying", LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType }, false));
        module.AddFunction("SetSoundVolume", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.FloatType }, false));
        module.AddFunction("UnloadSound", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));

        module.AddFunction("BeginMode2D", LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false));
        module.AddFunction("EndMode2D", LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false));

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
        ecsEmitter.EmitProfilerRuntime(dataLayout);

        // Forward-declare regular functions and impl methods so any function or system can call them
        foreach (var decl in program.Declarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                var returnType = MapType(context, fnDecl.ReturnType, ecsEmitter);
                var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName, ecsEmitter)).ToArray();
                var funcType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
                module.AddFunction(fnDecl.Name, funcType);
            }
            else if (decl is ImplDeclaration impl)
            {
                foreach (var method in impl.Methods)
                {
                    var mangledName = $"{impl.StructName}_{method.Name}";
                    var returnType = MapType(context, method.ReturnType, ecsEmitter);
                    var paramTypes = new List<LLVMTypeRef>();
                    for (int i = 0; i < method.Parameters.Count; i++)
                    {
                        var p = method.Parameters[i];
                        if (i == 0 && p.Name == "self")
                        {
                            var structType = ecsEmitter.GetComponentStructType(impl.StructName);
                            paramTypes.Add(LLVMTypeRef.CreatePointer(structType, 0));
                        }
                        else
                        {
                            paramTypes.Add(MapType(context, p.TypeName, ecsEmitter));
                        }
                    }
                    var funcType = LLVMTypeRef.CreateFunction(returnType, paramTypes.ToArray(), false);
                    module.AddFunction(mangledName, funcType);
                }
            }
        }

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

        // Compile regular functions
        foreach (var decl in program.Declarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                CompileFunction(context, module, builder, fnDecl, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
            }
            else if (decl is ImplDeclaration impl)
            {
                foreach (var method in impl.Methods)
                {
                    CompileMethod(context, module, builder, impl.StructName, method, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
                }
            }
        }

        // Finalize Debug Information if active
        if (_diBuilder.HasValue)
        {
            _diBuilder.Value.DIBuilderFinalize();
        }

        // Verify module
        if (!module.TryVerify(LLVMVerifierFailureAction.LLVMPrintMessageAction, out var verifyMessage))
        {
            _diagnostics.ReportError($"LLVM Module verification failed: {verifyMessage}", SourceSpan.None);
            return false;
        }

        // Run LLVM Optimization Passes (New Pass Manager)
        if (_options.OptimizationLevel != OptimizationLevel.O0)
        {
            var passOptions = LlvmApi.CreatePassBuilderOptions();
            LlvmApi.PassBuilderOptionsSetLoopVectorization(passOptions, 1);
            LlvmApi.PassBuilderOptionsSetSLPVectorization(passOptions, 1);
            LlvmApi.PassBuilderOptionsSetLoopUnrolling(passOptions, 1);
            LlvmApi.PassBuilderOptionsSetMergeFunctions(passOptions, 1);

            string pipeline = _options.GetPassPipelineString();
            IntPtr pipelinePtr = Marshal.StringToHGlobalAnsi(pipeline);
            try
            {
                var err = LlvmApi.RunPasses(module, (sbyte*)pipelinePtr, targetMachine, passOptions);
                if ((IntPtr)err != IntPtr.Zero)
                {
                    var msg = Marshal.PtrToStringAnsi((IntPtr)LlvmApi.GetErrorMessage(err));
                    _diagnostics.ReportWarning($"LLVM pass optimization warning: {msg}", SourceSpan.None);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pipelinePtr);
                LlvmApi.DisposePassBuilderOptions(passOptions);
            }
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

    private unsafe void CompileSystem(
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
        builder.CurrentDebugLocation = default;
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var sysFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var sysFunc = module.AddFunction($"system_{sys.Name}", sysFuncType);
        var worldParam = sysFunc.GetParam(0);
        worldParam.Name = "world";

        if (_diBuilder.HasValue)
        {
            var subroutineType = _diBuilder.Value.CreateSubroutineType(_diFile, Array.Empty<LLVMMetadataRef>(), LLVMDIFlags.LLVMDIFlagZero);
            uint line = (uint)Math.Max(1, sys.Span.Line);
            var subprogram = _diBuilder.Value.CreateFunction(
                _diFile,
                $"system_{sys.Name}",
                $"system_{sys.Name}",
                _diFile,
                line,
                subroutineType,
                0,
                1,
                line,
                LLVMDIFlags.LLVMDIFlagZero,
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0);
            LlvmApi.SetSubprogram(sysFunc, subprogram);
            _currentSubprogram = subprogram;
            var fnLoc = LlvmApi.DIBuilderCreateDebugLocation(context, line, 1, subprogram, null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, fnLoc);
        }

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
                else if (rp.TypeName == "Commands")
                {
                    evLocals[rp.Name] = worldAlloca;
                    evVarTypes[rp.Name] = "Commands";
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

        // Compute required query component mask and without mask
        ulong requiredMask = 0;
        foreach (var qp in sys.QueryParams)
        {
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                requiredMask |= ecs.GetComponentMask(qp.TypeName);
            }
        }
        ulong withoutMask = 0;
        foreach (var filter in sys.Filters)
        {
            if (filter.Kind == QueryFilterKind.With && _typeChecker.Components.ContainsKey(filter.ComponentName))
            {
                requiredMask |= ecs.GetComponentMask(filter.ComponentName);
            }
            else if (filter.Kind == QueryFilterKind.Without && _typeChecker.Components.ContainsKey(filter.ComponentName))
            {
                withoutMask |= ecs.GetComponentMask(filter.ComponentName);
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

        // arch_body: check if archetype matches requiredMask and does not contain withoutMask
        builder.PositionAtEnd(archBodyBB);
        var archPtrType = LLVMTypeRef.CreatePointer(ecs.GetArchetypeStructType(), 0);
        var archTablesSlot = builder.BuildStructGEP2(ecs.GetWorldStructType(), worldParam, 2, "world_arch_tables_slot");
        var tablesBase = builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var curArchPtr = builder.BuildInBoundsGEP2(ecs.GetArchetypeStructType(), tablesBase, new[] { curArchIdx }, "cur_arch_ptr");

        var maskSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 0, "mask_slot");
        var archMask = builder.BuildLoad2(context.Int64Type, maskSlot, "arch_mask");

        var andMask = builder.BuildAnd(archMask, reqMaskVal, "and_mask");
        var hasReq = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andMask, reqMaskVal, "has_req");
        LLVMValueRef isMatch;
        if (withoutMask != 0)
        {
            var withoutMaskVal = LLVMValueRef.CreateConstInt(context.Int64Type, withoutMask);
            var andWithout = builder.BuildAnd(archMask, withoutMaskVal, "and_without");
            var hasNoWithout = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, andWithout, LLVMValueRef.CreateConstInt(context.Int64Type, 0), "has_no_without");
            isMatch = builder.BuildAnd(hasReq, hasNoWithout, "is_match");
        }
        else
        {
            isMatch = hasReq;
        }

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
            else if (qp.TypeName == "Entity")
            {
                var i32PtrType = LLVMTypeRef.CreatePointer(context.Int32Type, 0);
                var entArrSlot = builder.BuildStructGEP2(ecs.GetArchetypeStructType(), curArchPtr, 3, $"{qp.Name}_arr_slot");
                var entArrRaw = builder.BuildLoad2(i8PtrType, entArrSlot, $"{qp.Name}_arr_raw");
                var entArrTyped = builder.BuildBitCast(entArrRaw, i32PtrType, $"{qp.Name}_arr_typed");
                var entElemPtr = builder.BuildInBoundsGEP2(context.Int32Type, entArrTyped, new[] { curRow }, $"{qp.Name}_elem_ptr");
                var curEntityVal = builder.BuildLoad2(context.Int32Type, entElemPtr, $"{qp.Name}_val");
                var entAlloca = CreateEntryBlockAlloca(context, sysFunc, context.Int32Type, $"{qp.Name}_alloca");
                builder.BuildStore(curEntityVal, entAlloca);
                locals[qp.Name] = entAlloca;
            }
            else if (qp.TypeName == "Commands")
            {
                locals[qp.Name] = worldAlloca;
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

        builder.CurrentDebugLocation = default;
        _currentSubprogram = null;
    }

    private unsafe void CompilePipeline(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        PipelineDeclaration pipe,
        EcsRuntimeEmitter ecs)
    {
        builder.CurrentDebugLocation = default;
        var worldPtrType = LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0);
        var pipeFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
        var pipeFunc = module.AddFunction($"pipeline_{pipe.Name}", pipeFuncType);
        var worldParam = pipeFunc.GetParam(0);
        worldParam.Name = "world";

        if (_diBuilder.HasValue)
        {
            var subroutineType = _diBuilder.Value.CreateSubroutineType(_diFile, Array.Empty<LLVMMetadataRef>(), LLVMDIFlags.LLVMDIFlagZero);
            uint line = (uint)Math.Max(1, pipe.Span.Line);
            var subprogram = _diBuilder.Value.CreateFunction(
                _diFile,
                pipe.Name,
                pipe.Name,
                _diFile,
                line,
                subroutineType,
                0,
                1,
                line,
                LLVMDIFlags.LLVMDIFlagZero,
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0);
            LlvmApi.SetSubprogram(pipeFunc, subprogram);
            _currentSubprogram = subprogram;
            var fnLoc = LlvmApi.DIBuilderCreateDebugLocation(context, line, 1, subprogram, null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, fnLoc);
        }

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
                SetDebugLocation(context, builder, action.Span);
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
                else if (action is ApplyCommandsAction)
                {
                    var applyFunc = module.GetNamedFunction("world_apply_commands");
                    if (applyFunc.Handle != IntPtr.Zero)
                    {
                        var applyFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                        builder.BuildCall2(applyFuncType, applyFunc, new[] { worldParam });
                    }
                }
            }

            // Automatically apply any remaining deferred commands at stage boundary
            var stageApplyFunc = module.GetNamedFunction("world_apply_commands");
            if (stageApplyFunc.Handle != IntPtr.Zero)
            {
                var applyFuncType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { worldPtrType }, false);
                builder.BuildCall2(applyFuncType, stageApplyFunc, new[] { worldParam });
            }
        }

        builder.BuildRetVoid();

        builder.CurrentDebugLocation = default;
        _currentSubprogram = null;
    }

    private unsafe void CompileFunction(
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
        builder.CurrentDebugLocation = default;
        var returnType = MapType(context, fnDecl.ReturnType, ecs);
        var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName, ecs)).ToArray();
        var function = module.GetNamedFunction(fnDecl.Name);

        if (_diBuilder.HasValue)
        {
            var subroutineType = _diBuilder.Value.CreateSubroutineType(_diFile, Array.Empty<LLVMMetadataRef>(), LLVMDIFlags.LLVMDIFlagZero);
            uint line = (uint)Math.Max(1, fnDecl.Span.Line);
            var subprogram = _diBuilder.Value.CreateFunction(
                _diFile,
                fnDecl.Name,
                fnDecl.Name,
                _diFile,
                line,
                subroutineType,
                0,
                1,
                line,
                LLVMDIFlags.LLVMDIFlagZero,
                _options.OptimizationLevel != OptimizationLevel.O0 ? 1 : 0);
            LlvmApi.SetSubprogram(function, subprogram);
            _currentSubprogram = subprogram;
            var fnLoc = LlvmApi.DIBuilderCreateDebugLocation(context, line, 1, subprogram, null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, fnLoc);
        }

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
            if (isMain && !hasWaitKey && !_options.NoWaitOnExit && !_options.IsRelease)
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

        builder.CurrentDebugLocation = default;
        _currentSubprogram = null;
    }

    private unsafe void CompileMethod(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        string structName,
        FunctionDeclaration methodDecl,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        builder.CurrentDebugLocation = default;
        var mangledName = $"{structName}_{methodDecl.Name}";
        var function = module.GetNamedFunction(mangledName);
        var returnType = MapType(context, methodDecl.ReturnType, ecs);

        var entryBlock = function.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBlock);

        var locals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
        var varTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Allocate function parameters
        for (int i = 0; i < methodDecl.Parameters.Count; i++)
        {
            var p = methodDecl.Parameters[i];
            var pVal = function.GetParam((uint)i);
            if (i == 0 && p.Name == "self")
            {
                // 'self' is passed as a direct pointer (Struct*)
                locals[p.Name] = pVal;
                varTypes[p.Name] = structName;
            }
            else
            {
                var pType = MapType(context, p.TypeName, ecs);
                var alloca = builder.BuildAlloca(pType, p.Name);
                builder.BuildStore(pVal, alloca);
                locals[p.Name] = alloca;
                varTypes[p.Name] = p.TypeName;
            }
        }

        CompileBlock(context, module, builder, function, methodDecl.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, false, false);

        // Ensure terminating return if not explicitly present
        var lastBlock = builder.InsertBlock;
        if (lastBlock.Terminator.Handle == IntPtr.Zero)
        {
            if (returnType == context.VoidType)
                builder.BuildRetVoid();
            else
                builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int32Type, 0, false));
        }

        builder.CurrentDebugLocation = default;
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

    private unsafe void CompileBlock(
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

            SetDebugLocation(context, builder, stmt.Span);

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
                                var arrTypeSym = TypeSymbol.FromName(arrTypeName);
                                var idxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));

                                if (arrTypeSym.IsMap)
                                {
                                    arrTypeSym.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                                    var keyVal = CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                                    var insertFunc = _mapEmitter!.GetOrCreateInsert(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                                    var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                                    builder.BuildCall2(ifType, insertFunc, new[] { targetPtr, keyVal, newVal }, "");
                                    break;
                                }
                                else if (arrTypeSym.IsDynamicArray)
                                {
                                    arrTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                                    var dynArrStructType = MapType(context, arrTypeSym.Name, ecs);
                                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                                    var dataSlot = builder.BuildStructGEP2(dynArrStructType, targetPtr, 0, $"{assign.TargetName}_data_slot");
                                    var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, $"{assign.TargetName}_data_ptr");
                                    destPtr = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { idxVal }, $"{assign.TargetName}_elem_gep");
                                }
                                else
                                {
                                    var arrType = MapType(context, arrTypeName, ecs);
                                    var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                                    destPtr = builder.BuildInBoundsGEP2(arrType, targetPtr, new[] { zero, idxVal }, $"{assign.TargetName}_elem_gep");
                                }
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
                    if (isMain && !hasWaitKey && !_options.NoWaitOnExit && !_options.IsRelease)
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
        var sType = _typeChecker.GetNodeType(match.Scrutinee);
        if (sType.IsOption)
        {
            sType.TryGetOptionInfo(out var elemType);
            var elemLlvmType = MapType(context, elemType.Name, ecs);
            var optLlvmType = MapType(context, sType.Name, ecs);

            var scrutVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var scrutAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "match_opt_scrut");
            builder.BuildStore(scrutVal, scrutAlloca);

            var tagGEP = builder.BuildStructGEP2(optLlvmType, scrutAlloca, 0, "opt_tag_gep");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagGEP, "opt_tag");

            var optMergeBB = function.AppendBasicBlock("opt_match_merge");
            var optDefaultBB = function.AppendBasicBlock("opt_match_default");

            MatchArm? someArm = null;
            string? someBindVar = null;
            MatchArm? noneArm = null;
            MatchArm? optWildcardArm = null;

            foreach (var arm in match.Arms)
            {
                if (arm.Pattern is WildcardExpression)
                {
                    optWildcardArm = arm;
                }
                else if (arm.Pattern is IdentifierExpression { Name: "None" } || arm.Pattern is CallExpression { Callee: "None" } ||
                         (arm.Pattern is IdentifierExpression idN && idN.Name.EndsWith("::None")))
                {
                    noneArm = arm;
                }
                else if (arm.Pattern is CallExpression callSome && (callSome.Callee == "Some" || callSome.Callee.EndsWith("::Some")) && callSome.Arguments.Count == 1 && callSome.Arguments[0] is IdentifierExpression bindId)
                {
                    someArm = arm;
                    someBindVar = bindId.Name;
                }
            }

            var optSwitchInst = builder.BuildSwitch(tagVal, optDefaultBB, 2);

            if (someArm != null)
            {
                var someBB = function.AppendBasicBlock("match_arm_some");
                optSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 1), someBB);

                builder.PositionAtEnd(someBB);
                if (someBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(optLlvmType, scrutAlloca, 1, $"{someBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(elemLlvmType, valGEP, someBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, elemLlvmType, someBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[someBindVar] = bindAlloca;
                    varTypes[someBindVar] = elemType.Name;
                }

                CompileBlock(context, module, builder, function, someArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (someBindVar != null)
                {
                    locals.Remove(someBindVar);
                    varTypes.Remove(someBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(optMergeBB);
                }
            }

            if (noneArm != null)
            {
                var noneBB = function.AppendBasicBlock("match_arm_none");
                optSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 0), noneBB);

                builder.PositionAtEnd(noneBB);
                CompileBlock(context, module, builder, function, noneArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(optMergeBB);
                }
            }

            builder.PositionAtEnd(optDefaultBB);
            if (optWildcardArm != null)
            {
                CompileBlock(context, module, builder, function, optWildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            }
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(optMergeBB);
            }

            builder.PositionAtEnd(optMergeBB);
            return;
        }

        if (sType.IsResult)
        {
            sType.TryGetResultInfo(out var okType, out var errType);
            var okLlvmType = MapType(context, okType.Name, ecs);
            var errLlvmType = MapType(context, errType.Name, ecs);
            var resLlvmType = MapType(context, sType.Name, ecs);

            var scrutVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var scrutAlloca = CreateEntryBlockAlloca(context, function, resLlvmType, "match_res_scrut");
            builder.BuildStore(scrutVal, scrutAlloca);

            var tagGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 0, "res_tag_gep");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagGEP, "res_tag");

            var resMergeBB = function.AppendBasicBlock("res_match_merge");
            var resDefaultBB = function.AppendBasicBlock("res_match_default");

            MatchArm? okArm = null;
            string? okBindVar = null;
            MatchArm? errArm = null;
            string? errBindVar = null;
            MatchArm? resWildcardArm = null;

            foreach (var arm in match.Arms)
            {
                if (arm.Pattern is WildcardExpression)
                {
                    resWildcardArm = arm;
                }
                else if (arm.Pattern is CallExpression callOk && (callOk.Callee == "Ok" || callOk.Callee.EndsWith("::Ok")) && callOk.Arguments.Count == 1 && callOk.Arguments[0] is IdentifierExpression bindOk)
                {
                    okArm = arm;
                    okBindVar = bindOk.Name;
                }
                else if (arm.Pattern is CallExpression callErr && (callErr.Callee == "Err" || callErr.Callee.EndsWith("::Err")) && callErr.Arguments.Count == 1 && callErr.Arguments[0] is IdentifierExpression bindErr)
                {
                    errArm = arm;
                    errBindVar = bindErr.Name;
                }
            }

            var resSwitchInst = builder.BuildSwitch(tagVal, resDefaultBB, 2);

            if (okArm != null)
            {
                var okBB = function.AppendBasicBlock("match_arm_ok");
                resSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 0), okBB); // Tag 0 = Ok

                builder.PositionAtEnd(okBB);
                if (okBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 1, $"{okBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(okLlvmType, valGEP, okBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, okLlvmType, okBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[okBindVar] = bindAlloca;
                    varTypes[okBindVar] = okType.Name;
                }

                CompileBlock(context, module, builder, function, okArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (okBindVar != null)
                {
                    locals.Remove(okBindVar);
                    varTypes.Remove(okBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(resMergeBB);
                }
            }

            if (errArm != null)
            {
                var errBB = function.AppendBasicBlock("match_arm_err");
                resSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 1), errBB); // Tag 1 = Err

                builder.PositionAtEnd(errBB);
                if (errBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 2, $"{errBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(errLlvmType, valGEP, errBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, errLlvmType, errBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[errBindVar] = bindAlloca;
                    varTypes[errBindVar] = errType.Name;
                }

                CompileBlock(context, module, builder, function, errArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (errBindVar != null)
                {
                    locals.Remove(errBindVar);
                    varTypes.Remove(errBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(resMergeBB);
                }
            }

            builder.PositionAtEnd(resDefaultBB);
            if (resWildcardArm != null)
            {
                CompileBlock(context, module, builder, function, resWildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            }
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(resMergeBB);
            }

            builder.PositionAtEnd(resMergeBB);
            return;
        }

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
                if (ident.Name == "None" || (ident.Name.StartsWith("Option<") && ident.Name.EndsWith("::None")))
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
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case MemberAccessExpression mem:
                var memTargetType = _typeChecker.GetNodeType(mem.Target);
                if (memTargetType == TypeSymbol.String && mem.MemberName is "len" or "length")
                {
                    var strVal = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { strVal }, "slen64");
                    return builder.BuildTrunc(len64, context.Int32Type, "slen32");
                }
                if (memTargetType.IsDynamicArray && mem.MemberName is "len" or "length" or "capacity")
                {
                    var dynArrStructType = MapType(context, memTargetType.Name, ecs);
                    LLVMValueRef dynStructPtr;
                    if (mem.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    uint fieldIdx = mem.MemberName == "capacity" ? 2u : 1u;
                    var fieldSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, fieldIdx, $"dyn_{mem.MemberName}_slot");
                    return builder.BuildLoad2(context.Int32Type, fieldSlot, $"dyn_{mem.MemberName}_val");
                }
                if (memTargetType.IsMap && mem.MemberName is "len" or "length" or "count" or "capacity")
                {
                    var mapStructType = MapType(context, memTargetType.Name, ecs);
                    LLVMValueRef mapStructPtr;
                    if (mem.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    uint fieldIdx = mem.MemberName == "capacity" ? 2u : 1u;
                    var fieldSlot = builder.BuildStructGEP2(mapStructType, mapStructPtr, fieldIdx, $"map_{mem.MemberName}_slot");
                    return builder.BuildLoad2(context.Int32Type, fieldSlot, $"map_{mem.MemberName}_val");
                }
                if (mem.Target is IdentifierExpression enumId && _typeChecker.Enums.TryGetValue(enumId.Name, out var enumSym))
                {
                    if (enumSym.Members.TryGetValue(mem.MemberName, out var mSym))
                    {
                        return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)mSym.Value, false);
                    }
                }
                if (mem.Target is IdentifierExpression targetId && locals.TryGetValue(targetId.Name, out var structPtr))
                {
                    if (varTypes.TryGetValue(targetId.Name, out var structTypeName) &&
                        (_typeChecker.Structs.ContainsKey(structTypeName) ||
                         _typeChecker.Components.ContainsKey(structTypeName) ||
                         _typeChecker.Resources.ContainsKey(structTypeName) ||
                         _typeChecker.Events.ContainsKey(structTypeName)))
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
                if (arrTypeSym.IsDynamicArray)
                {
                    arrTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, arrTypeSym.Name, ecs);
                    var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_arr");

                    int elemCount = arrLit.Elements.Count;
                    if (elemCount == 0)
                    {
                        var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
                        var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                        var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                        builder.BuildStore(dataZero, p0);
                        var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                        builder.BuildStore(zeroInt, p1);
                        var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                        builder.BuildStore(zeroInt, p2);
                    }
                    else
                    {
                        ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                        ulong totalBytes = (ulong)elemCount * elemSize;
                        var mallocFunc = module.GetNamedFunction("malloc");
                        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                        var rawBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, totalBytes) }, "dyn_buf");
                        var typedBuf = builder.BuildBitCast(rawBuf, LLVMTypeRef.CreatePointer(dynElemType, 0), "dyn_buf_typed");

                        for (int i = 0; i < elemCount; i++)
                        {
                            var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                            var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                            var elemGEP = builder.BuildInBoundsGEP2(dynElemType, typedBuf, new[] { idxVal }, $"dyn_elem_{i}");
                            builder.BuildStore(elemVal, elemGEP);
                        }

                        var countVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)elemCount);
                        var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                        builder.BuildStore(typedBuf, p0);
                        var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                        builder.BuildStore(countVal, p1);
                        var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                        builder.BuildStore(countVal, p2);
                    }

                    return builder.BuildLoad2(dynArrStructType, dynArrAlloca, "dyn_arr_val");
                }
                else
                {
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
                }

            case IndexExpression idxExpr:
                var elemType = MapType(context, _typeChecker.GetNodeType(idxExpr).Name, ecs);
                var indexVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                var zeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                var idxTargetType = _typeChecker.GetNodeType(idxExpr.Target);
                if (idxTargetType.IsMap)
                {
                    idxTargetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                    var mapKeyVal = CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mapStructType = MapType(context, idxTargetType.Name, ecs);

                    LLVMValueRef mapStructPtr;
                    if (idxExpr.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                    return builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, mapKeyVal }, "map_get_idx");
                }

                if (idxTargetType.IsDynamicArray)
                {
                    idxTargetType.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, idxTargetType.Name, ecs);
                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                    LLVMValueRef dynStructPtr;
                    if (idxExpr.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                    var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "dyn_data_ptr");
                    var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { indexVal }, "dyn_elem_gep");
                    return builder.BuildLoad2(dynElemType, elemGEP, "dyn_elem_val");
                }

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
                var targetType = _typeChecker.GetNodeType(methodCall.Target);

                if (targetType.IsDynamicArray)
                {
                    targetType.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, targetType.Name, ecs);
                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                    LLVMValueRef dynStructPtr;
                    if (methodCall.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    if (methodCall.MethodName is "len" or "length" or "capacity")
                    {
                        uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                        var fieldSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, fieldIdx, $"dyn_{methodCall.MethodName}_slot");
                        return builder.BuildLoad2(context.Int32Type, fieldSlot, $"dyn_{methodCall.MethodName}_val");
                    }

                    if (methodCall.MethodName == "clear")
                    {
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), lenSlot);
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "push")
                    {
                        var itemVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var capSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 2, "dyn_cap_slot");

                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var curCap = builder.BuildLoad2(context.Int32Type, capSlot, "cur_cap");

                        var isFull = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curLen, curCap, "is_full");

                        var growBB = function.AppendBasicBlock("dyn_grow");
                        var insertBB = function.AppendBasicBlock("dyn_insert");

                        builder.BuildCondBr(isFull, growBB, insertBB);

                        // growBB:
                        builder.PositionAtEnd(growBB);
                        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_cap_zero");
                        var doubleCap = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 2), "double_cap");
                        var newCap = builder.BuildSelect(isCapZero, LLVMValueRef.CreateConstInt(context.Int32Type, 4), doubleCap, "new_cap");
                        builder.BuildStore(newCap, capSlot);

                        // realloc(data, newCap * sizeof(elem))
                        ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                        var newCap64 = builder.BuildZExt(newCap, context.Int64Type, "new_cap_64");
                        var newSizeBytes = builder.BuildMul(newCap64, LLVMValueRef.CreateConstInt(context.Int64Type, elemSize), "new_size_bytes");

                        var oldData = builder.BuildLoad2(elemPtrType, dataSlot, "old_data");
                        var oldDataRaw = builder.BuildBitCast(oldData, i8PtrType, "old_data_raw");

                        var reallocFunc = module.GetNamedFunction("realloc");
                        var reallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int64Type }, false);
                        var newDataRaw = builder.BuildCall2(reallocType, reallocFunc, new[] { oldDataRaw, newSizeBytes }, "new_data_raw");
                        var newDataTyped = builder.BuildBitCast(newDataRaw, elemPtrType, "new_data_typed");
                        builder.BuildStore(newDataTyped, dataSlot);

                        builder.BuildBr(insertBB);

                        // insertBB:
                        builder.PositionAtEnd(insertBB);
                        var curDataAfter = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data_after");
                        var curLenAfter = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len_after");
                        var insertGEP = builder.BuildInBoundsGEP2(dynElemType, curDataAfter, new[] { curLenAfter }, "insert_elem_gep");
                        builder.BuildStore(itemVal, insertGEP);

                        var nextLen = builder.BuildAdd(curLenAfter, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_len");
                        builder.BuildStore(nextLen, lenSlot);

                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "pop")
                    {
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");

                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var newLen = builder.BuildSub(curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_len");
                        builder.BuildStore(newLen, lenSlot);

                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");
                        var elemGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { newLen }, "pop_elem_gep");
                        return builder.BuildLoad2(dynElemType, elemGEP, "pop_val");
                    }

                    if (methodCall.MethodName == "get")
                    {
                        var idxVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        idxVal = EnsureInt32(context, builder, idxVal, "arr_get_idx");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var geZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "ge_zero");
                        var ltLen = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "lt_len");
                        var inBounds = builder.BuildAnd(geZero, ltLen, "in_bounds");

                        var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, dynElemType }, false);
                        var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_arr_get_res");

                        var inBB = function.AppendBasicBlock("arr_get_in");
                        var outBB = function.AppendBasicBlock("arr_get_out");
                        var mergeBB = function.AppendBasicBlock("arr_get_merge");

                        builder.BuildCondBr(inBounds, inBB, outBB);

                        builder.PositionAtEnd(inBB);
                        var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { idxVal }, "elem_gep");
                        var elemVal = builder.BuildLoad2(dynElemType, elemGEP, "elem_val");
                        var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                        var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                        builder.BuildStore(elemVal, val1);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(outBB);
                        var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                        var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                        builder.BuildStore(LLVMValueRef.CreateConstNull(dynElemType), val0);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(optStructType, retAlloca, "opt_arr_val");
                    }
                }

                if (targetType.IsMap)
                {
                    targetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                    var mapStructType = MapType(context, targetType.Name, ecs);

                    LLVMValueRef mapStructPtr;
                    if (methodCall.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    if (methodCall.MethodName is "len" or "length" or "count" or "capacity")
                    {
                        uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                        var fieldSlot = builder.BuildStructGEP2(mapStructType, mapStructPtr, fieldIdx, $"map_{methodCall.MethodName}_slot");
                        return builder.BuildLoad2(context.Int32Type, fieldSlot, $"map_{methodCall.MethodName}_val");
                    }

                    if (methodCall.MethodName is "insert" or "put")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var vArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var insertFunc = _mapEmitter!.GetOrCreateInsert(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                        builder.BuildCall2(ifType, insertFunc, new[] { mapStructPtr, kArg, vArg }, "");
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "get")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                        return builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "map_get_val");
                    }

                    if (methodCall.MethodName is "find" or "get_opt")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                        var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);

                        var hasIt = builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_has_key");
                        var valLlvmType = MapType(context, mapValSym.Name, ecs);
                        var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, valLlvmType }, false);
                        var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_map_find_res");

                        var foundBB = function.AppendBasicBlock("map_find_found");
                        var notFoundBB = function.AppendBasicBlock("map_find_not_found");
                        var mergeBB = function.AppendBasicBlock("map_find_merge");

                        builder.BuildCondBr(hasIt, foundBB, notFoundBB);

                        builder.PositionAtEnd(foundBB);
                        var valRes = builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "found_val");
                        var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                        var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                        builder.BuildStore(valRes, val1);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(notFoundBB);
                        var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                        var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                        builder.BuildStore(LLVMValueRef.CreateConstNull(valLlvmType), val0);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(optStructType, retAlloca, "opt_map_val");
                    }

                    if (methodCall.MethodName is "contains" or "has")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                        return builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_contains_val");
                    }

                    if (methodCall.MethodName == "remove")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var removeFunc = _mapEmitter!.GetOrCreateRemove(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var rfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(removeFunc);
                        return builder.BuildCall2(rfType, removeFunc, new[] { mapStructPtr, kArg }, "map_remove_val");
                    }

                    if (methodCall.MethodName == "clear")
                    {
                        var clearFunc = _mapEmitter!.GetOrCreateClear(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(clearFunc);
                        builder.BuildCall2(cfType, clearFunc, new[] { mapStructPtr }, "");
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }
                }

                var targetVal = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                if (methodCall.MethodName == "to_string")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    return EmitToString(context, module, builder, targetVal, targetType, i8PtrType, ecs, worldPtr);
                }

                if (methodCall.MethodName is "len" or "length")
                {
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "slen64");
                    return builder.BuildTrunc(len64, context.Int32Type, "slen32");
                }

                if (targetType.IsOption)
                {
                    targetType.TryGetOptionInfo(out var optElemType);
                    var elemValType = MapType(context, optElemType.Name, ecs);
                    var optStructType = MapType(context, targetType.Name, ecs);

                    var tagVal = builder.BuildExtractValue(targetVal, 0, "opt_tag");

                    if (methodCall.MethodName == "is_some")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_some");
                    }

                    if (methodCall.MethodName == "is_none")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_none");
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
                        return builder.BuildExtractValue(targetVal, 1, "opt_unwrapped");
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
                        return builder.BuildLoad2(elemValType, retAlloca, "unwrap_or_val");
                    }
                }

                if (targetType.IsResult)
                {
                    targetType.TryGetResultInfo(out var okType, out var errType);
                    var okLlvmType = MapType(context, okType.Name, ecs);
                    var errLlvmType = MapType(context, errType.Name, ecs);
                    var resStructType = MapType(context, targetType.Name, ecs);

                    var tagVal = builder.BuildExtractValue(targetVal, 0, "res_tag");

                    if (methodCall.MethodName == "is_ok")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_ok");
                    }

                    if (methodCall.MethodName == "is_err")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_err");
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
                        return builder.BuildExtractValue(targetVal, 1, "res_unwrapped_ok");
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
                        return builder.BuildExtractValue(targetVal, 2, "res_unwrapped_err");
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
                        return builder.BuildLoad2(okLlvmType, retAlloca, "res_unwrap_or_val");
                    }
                }

                // Check if target is a struct/component with implemented method
                var structMethodName = $"{targetType.Name}_{methodCall.MethodName}";
                var structMethodFunc = module.GetNamedFunction(structMethodName);
                if (structMethodFunc.Handle != IntPtr.Zero)
                {
                    var smArgs = new List<LLVMValueRef>();

                    // Pass pointer to self
                    LLVMValueRef selfPtr;
                    if (methodCall.Target is IdentifierExpression smTargetId && locals.TryGetValue(smTargetId.Name, out var idPtr))
                    {
                        selfPtr = idPtr;
                    }
                    else if (methodCall.Target is MemberAccessExpression memAccess &&
                             memAccess.Target is IdentifierExpression memTargetId &&
                             locals.TryGetValue(memTargetId.Name, out var basePtr) &&
                             varTypes.TryGetValue(memTargetId.Name, out var baseTypeName))
                    {
                        var baseStructType = ecs.GetComponentStructType(baseTypeName);
                        int offset = ecs.GetFieldOffset(baseTypeName, memAccess.MemberName);
                        selfPtr = builder.BuildStructGEP2(baseStructType, basePtr, (uint)offset, $"{memTargetId.Name}_{memAccess.MemberName}_ptr");
                    }
                    else
                    {
                        var stType = ecs.GetComponentStructType(targetType.Name);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, stType, "self_temp");
                        builder.BuildStore(targetVal, tempAlloca);
                        selfPtr = tempAlloca;
                    }
                    smArgs.Add(selfPtr);

                    foreach (var arg in methodCall.Arguments)
                    {
                        smArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    }

                    var smFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(structMethodFunc);
                    string smCallName = smFuncType.ReturnType == context.VoidType ? "" : $"{methodCall.MethodName}_call";
                    return builder.BuildCall2(smFuncType, structMethodFunc, smArgs.ToArray(), smCallName);
                }

                if (methodCall.MethodName == "set_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var entArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var insertFunc = _mapEmitter!.GetOrCreateInsert("string", "Entity", (t) => MapType(context, t, ecs));
                    var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                    builder.BuildCall2(ifType, insertFunc, new[] { nameMapPtr, nameArg, entArg }, "");
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                if (methodCall.MethodName == "get_by_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var getFunc = _mapEmitter!.GetOrCreateGet("string", "Entity", (t) => MapType(context, t, ecs));
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                    return builder.BuildCall2(gfType, getFunc, new[] { nameMapPtr, nameArg }, "ent_by_name");
                }

                if ((methodCall.MethodName is "find" or "find_entity") && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var containsFunc = _mapEmitter!.GetOrCreateContains("string", "Entity", (t) => MapType(context, t, ecs));
                    var getFunc = _mapEmitter!.GetOrCreateGet("string", "Entity", (t) => MapType(context, t, ecs));
                    var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);

                    var hasIt = builder.BuildCall2(cfType, containsFunc, new[] { nameMapPtr, nameArg }, "find_has_key");
                    var entLlvmType = MapType(context, "Entity", ecs);
                    var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, entLlvmType }, false);
                    var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_find_res");

                    var foundBB = function.AppendBasicBlock("find_found");
                    var missingBB = function.AppendBasicBlock("find_missing");
                    var mergeBB = function.AppendBasicBlock("find_merge");

                    builder.BuildCondBr(hasIt, foundBB, missingBB);

                    builder.PositionAtEnd(foundBB);
                    var entVal = builder.BuildCall2(gfType, getFunc, new[] { nameMapPtr, nameArg }, "find_ent_val");
                    var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                    var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                    builder.BuildStore(entVal, val1);
                    builder.BuildBr(mergeBB);

                    builder.PositionAtEnd(missingBB);
                    var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                    var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                    builder.BuildStore(LLVMValueRef.CreateConstNull(entLlvmType), val0);
                    builder.BuildBr(mergeBB);

                    builder.PositionAtEnd(mergeBB);
                    return builder.BuildLoad2(optStructType, retAlloca, "opt_find_val");
                }

                if (methodCall.MethodName == "has_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var containsFunc = _mapEmitter!.GetOrCreateContains("string", "Entity", (t) => MapType(context, t, ecs));
                    var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                    return builder.BuildCall2(cfType, containsFunc, new[] { nameMapPtr, nameArg }, "has_name_val");
                }

                if (methodCall.MethodName == "reset_string_arena" && ecs != null && _arenaEmitter != null)
                {
                    var resetFunc = _arenaEmitter.GetOrCreateArenaReset(ecs);
                    var rfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(resetFunc);
                    var wPtrRaw = builder.BuildBitCast(targetVal, LLVMTypeRef.CreatePointer(context.Int8Type, 0), "w_raw");
                    builder.BuildCall2(rfType, resetFunc, new[] { wPtrRaw }, "");
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                if (methodCall.MethodName == "alloc_string" && ecs != null && _arenaEmitter != null)
                {
                    var capArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var cap64 = builder.BuildZExt(capArg, context.Int64Type, "cap_64");
                    var allocFunc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
                    var afType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(allocFunc);
                    var wPtrRaw = builder.BuildBitCast(targetVal, LLVMTypeRef.CreatePointer(context.Int8Type, 0), "w_raw");
                    return builder.BuildCall2(afType, allocFunc, new[] { wPtrRaw, cap64 }, "arena_str");
                }

                string mTargetName = $"world_{methodCall.MethodName}";
                if (methodCall.MethodName == "render_debug_overlay")
                {
                    mTargetName = "world_render_profiler";
                }

                bool isCommandsTarget = methodCall.Target is IdentifierExpression targetIdent &&
                                       varTypes.TryGetValue(targetIdent.Name, out var tType) &&
                                       tType == "Commands";

                if (isCommandsTarget)
                {
                    if (methodCall.MethodName == "spawn")
                    {
                        mTargetName = "world_cmd_spawn";
                    }
                    else if (methodCall.MethodName == "despawn")
                    {
                        mTargetName = "world_cmd_despawn";
                    }
                    else if ((methodCall.MethodName == "add" || methodCall.MethodName == "set") &&
                             methodCall.Arguments.Count == 2 &&
                             methodCall.Arguments[1] is CallExpression ctorCall)
                    {
                        mTargetName = $"world_cmd_set_{ctorCall.Callee}";
                        var ctorFunc = module.GetNamedFunction(mTargetName);
                        if (ctorFunc.Handle != IntPtr.Zero)
                        {
                            var ctorArgs = new List<LLVMValueRef>
                            {
                                targetVal,
                                CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)
                            };
                            foreach (var arg in ctorCall.Arguments)
                            {
                                ctorArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                            }
                            var ctorFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ctorFunc);
                            return builder.BuildCall2(ctorFuncType, ctorFunc, ctorArgs.ToArray(), "");
                        }
                    }
                    else
                    {
                        mTargetName = $"world_cmd_{methodCall.MethodName}";
                    }
                }
                else
                {
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
                var leftType = _typeChecker.GetNodeType(bin.Left);
                var rightType = _typeChecker.GetNodeType(bin.Right);
                var left = CompileExpression(context, module, builder, function, bin.Left, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var right = CompileExpression(context, module, builder, function, bin.Right, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                // String operations
                if (leftType == TypeSymbol.String || rightType == TypeSymbol.String)
                {
                    if (bin.Operator == BinaryOperator.Add)
                    {
                        var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                        var leftStr = EmitToString(context, module, builder, left, leftType, i8PtrType, ecs, worldPtr);
                        var rightStr = EmitToString(context, module, builder, right, rightType, i8PtrType, ecs, worldPtr);
                        var concatFn = GetOrCreateStringConcatFunction(context, module, i8PtrType, ecs);
                        var concatFnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(concatFn);
                        return builder.BuildCall2(concatFnType, concatFn, new[] { worldPtr, leftStr, rightStr }, "str_add");
                    }
                    if (bin.Operator == BinaryOperator.Equal)
                    {
                        var eqFn = GetOrCreateStringEq(context, module, i8PtrType);
                        var eqFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, i8PtrType }, false);
                        return builder.BuildCall2(eqFnType, eqFn, new[] { left, right }, "str_eq");
                    }
                    if (bin.Operator == BinaryOperator.NotEqual)
                    {
                        var eqFn = GetOrCreateStringEq(context, module, i8PtrType);
                        var eqFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, i8PtrType }, false);
                        var eq = builder.BuildCall2(eqFnType, eqFn, new[] { left, right }, "str_eq");
                        return builder.BuildNot(eq, "str_neq");
                    }
                }

                if (leftType.IsOption || rightType.IsOption)
                {
                    LLVMValueRef optVal = leftType.IsOption ? left : right;
                    var optType = leftType.IsOption ? leftType : rightType;
                    var optStructType = MapType(context, optType.Name, ecs);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_cmp_tmp");
                    builder.BuildStore(optVal, tmpAlloca);
                    var tagSlot = builder.BuildStructGEP2(optStructType, tmpAlloca, 0, "tag_slot");
                    var tagVal = builder.BuildLoad2(context.Int32Type, tagSlot, "tag_val");

                    if (bin.Operator == BinaryOperator.Equal)
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "opt_is_none");
                    }
                    if (bin.Operator == BinaryOperator.NotEqual)
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "opt_is_some");
                    }
                }

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
                if (call.Callee == "Some" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::Some")))
                {
                    var optType = _typeChecker.GetNodeType(call);
                    optType.TryGetOptionInfo(out var elemSym);
                    var elemLlvmType = MapType(context, elemSym.Name, ecs);
                    var optStructType = MapType(context, optType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "some_init");

                    var tagSlot = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot); // 1 = Some
                    var valSlot = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val_slot");
                    builder.BuildStore(argVal, valSlot);

                    return builder.BuildLoad2(optStructType, optAlloca, "some_val");
                }

                if (call.Callee == "None" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::None")))
                {
                    var optType = _typeChecker.GetNodeType(call);
                    optType.TryGetOptionInfo(out var elemSym);
                    var elemLlvmType = MapType(context, elemSym.Name, ecs);
                    var optStructType = MapType(context, optType.Name, ecs);

                    var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "none_init");

                    var tagSlot = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot); // 0 = None
                    var valSlot = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(elemLlvmType), valSlot);

                    return builder.BuildLoad2(optStructType, optAlloca, "none_val");
                }

                if (call.Callee == "Ok" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Ok")))
                {
                    var resType = _typeChecker.GetNodeType(call);
                    resType.TryGetResultInfo(out var okSym, out var errSym);
                    var okLlvmType = MapType(context, okSym.Name, ecs);
                    var errLlvmType = MapType(context, errSym.Name, ecs);
                    var resStructType = MapType(context, resType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "ok_init");

                    var tagSlot = builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot); // 0 = Ok
                    var okSlot = builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_slot");
                    builder.BuildStore(argVal, okSlot);
                    var errSlot = builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(errLlvmType), errSlot);

                    return builder.BuildLoad2(resStructType, resAlloca, "ok_val");
                }

                if (call.Callee == "Err" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Err")))
                {
                    var resType = _typeChecker.GetNodeType(call);
                    resType.TryGetResultInfo(out var okSym, out var errSym);
                    var okLlvmType = MapType(context, okSym.Name, ecs);
                    var errLlvmType = MapType(context, errSym.Name, ecs);
                    var resStructType = MapType(context, resType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "err_init");

                    var tagSlot = builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot); // 1 = Err
                    var okSlot = builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(okLlvmType), okSlot);
                    var errSlot = builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_slot");
                    builder.BuildStore(argVal, errSlot);

                    return builder.BuildLoad2(resStructType, resAlloca, "err_val");
                }

                if (call.Callee.StartsWith("Vec<") || call.Callee.StartsWith("List<"))
                {
                    var dynTypeSym = TypeSymbol.FromName(call.Callee);
                    dynTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, dynTypeSym.Name, ecs);
                    var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "vec_init");

                    var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
                    var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                    builder.BuildStore(dataZero, p0);
                    var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                    builder.BuildStore(zeroInt, p1);
                    var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                    builder.BuildStore(zeroInt, p2);

                    return builder.BuildLoad2(dynArrStructType, dynArrAlloca, "vec_val");
                }

                if (call.Callee.StartsWith("Map<") || call.Callee.StartsWith("HashMap<"))
                {
                    var mapTypeSym = TypeSymbol.FromName(call.Callee);
                    mapTypeSym.TryGetMapInfo(out var kSym, out var vSym);
                    var mapStructType = MapType(context, mapTypeSym.Name, ecs);
                    var entryStructType = _mapEmitter!.GetEntryType(kSym.Name, vSym.Name, (t) => MapType(context, t, ecs));
                    var mapAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_init");

                    var nullEntries = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(entryStructType, 0));
                    var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    var p0 = builder.BuildStructGEP2(mapStructType, mapAlloca, 0, "map_entries_slot");
                    builder.BuildStore(nullEntries, p0);
                    var p1 = builder.BuildStructGEP2(mapStructType, mapAlloca, 1, "map_count_slot");
                    builder.BuildStore(zeroInt, p1);
                    var p2 = builder.BuildStructGEP2(mapStructType, mapAlloca, 2, "map_cap_slot");
                    builder.BuildStore(zeroInt, p2);

                    return builder.BuildLoad2(mapStructType, mapAlloca, "map_val");
                }

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
                else if (call.Callee == "to_string")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var argType = _typeChecker.GetNodeType(call.Arguments[0]);
                    return EmitToString(context, module, builder, arg, argType, i8PtrType, ecs, worldPtr);
                }
                else if (call.Callee == "str_concat")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    var arg1 = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var arg2 = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var t1 = _typeChecker.GetNodeType(call.Arguments[0]);
                    var t2 = _typeChecker.GetNodeType(call.Arguments[1]);
                    var s1 = EmitToString(context, module, builder, arg1, t1, i8PtrType, ecs, worldPtr);
                    var s2 = EmitToString(context, module, builder, arg2, t2, i8PtrType, ecs, worldPtr);
                    var concatFn = GetOrCreateStringConcatFunction(context, module, i8PtrType, ecs);
                    var concatFnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(concatFn);
                    return builder.BuildCall2(concatFnType, concatFn, new[] { worldPtr, s1, s2 }, "str_concat");
                }
                else if (call.Callee is "str_len" or "string_length")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { arg }, "strlen_call");
                    return builder.BuildTrunc(len64, context.Int32Type, "len_i32");
                }
                else if (call.Callee is "sqrt" or "sin" or "cos" or "floor" or "ceil")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var fVal = EnsureFloat(context, builder, arg);
                    string cName = call.Callee switch
                    {
                        "sqrt" => "sqrtf",
                        "sin" => "sinf",
                        "cos" => "cosf",
                        "floor" => "floorf",
                        "ceil" => "ceilf",
                        _ => "sqrtf"
                    };
                    var fn = module.GetNamedFunction(cName);
                    var fnType = LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false);
                    return builder.BuildCall2(fnType, fn, new[] { fVal }, $"{call.Callee}_call");
                }
                else if (call.Callee == "abs")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (arg.TypeOf == context.FloatType || arg.TypeOf == context.DoubleType)
                    {
                        var fVal = EnsureFloat(context, builder, arg);
                        var zero = LLVMValueRef.CreateConstReal(context.FloatType, 0.0);
                        var isNeg = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fVal, zero, "is_neg_f");
                        var negVal = builder.BuildFNeg(fVal, "fneg_val");
                        return builder.BuildSelect(isNeg, negVal, fVal, "abs_res_f");
                    }
                    else
                    {
                        var iVal = EnsureInt32(context, builder, arg);
                        var isNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, iVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_neg");
                        var negVal = builder.BuildNeg(iVal, "neg_val");
                        return builder.BuildSelect(isNeg, negVal, iVal, "abs_res");
                    }
                }
                else if (call.Callee == "min")
                {
                    var a = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var bVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (a.TypeOf == context.FloatType || bVal.TypeOf == context.FloatType)
                    {
                        var fa = EnsureFloat(context, builder, a);
                        var fb = EnsureFloat(context, builder, bVal);
                        var cmp = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fa, fb, "min_cmp");
                        return builder.BuildSelect(cmp, fa, fb, "min_val");
                    }
                    else
                    {
                        var ia = EnsureInt32(context, builder, a);
                        var ib = EnsureInt32(context, builder, bVal);
                        var cmp = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, ia, ib, "min_cmp");
                        return builder.BuildSelect(cmp, ia, ib, "min_val");
                    }
                }
                else if (call.Callee == "max")
                {
                    var a = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var bVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (a.TypeOf == context.FloatType || bVal.TypeOf == context.FloatType)
                    {
                        var fa = EnsureFloat(context, builder, a);
                        var fb = EnsureFloat(context, builder, bVal);
                        var cmp = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, fa, fb, "max_cmp");
                        return builder.BuildSelect(cmp, fa, fb, "max_val");
                    }
                    else
                    {
                        var ia = EnsureInt32(context, builder, a);
                        var ib = EnsureInt32(context, builder, bVal);
                        var cmp = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, ia, ib, "max_cmp");
                        return builder.BuildSelect(cmp, ia, ib, "max_val");
                    }
                }
                else if (call.Callee == "clamp")
                {
                    var valArg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var minArg = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var maxArg = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (valArg.TypeOf == context.FloatType || minArg.TypeOf == context.FloatType || maxArg.TypeOf == context.FloatType)
                    {
                        var fv = EnsureFloat(context, builder, valArg);
                        var fmin = EnsureFloat(context, builder, minArg);
                        var fmax = EnsureFloat(context, builder, maxArg);
                        var cmpMin = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fv, fmin, "cmp_min");
                        var clamp1 = builder.BuildSelect(cmpMin, fmin, fv, "clamp1");
                        var cmpMax = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, clamp1, fmax, "cmp_max");
                        return builder.BuildSelect(cmpMax, fmax, clamp1, "clamp_res");
                    }
                    else
                    {
                        var iv = EnsureInt32(context, builder, valArg);
                        var imin = EnsureInt32(context, builder, minArg);
                        var imax = EnsureInt32(context, builder, maxArg);
                        var cmpMin = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, iv, imin, "cmp_min");
                        var clamp1 = builder.BuildSelect(cmpMin, imin, iv, "clamp1");
                        var cmpMax = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, clamp1, imax, "cmp_max");
                        return builder.BuildSelect(cmpMax, imax, clamp1, "clamp_res");
                    }
                }
                else if (call.Callee == "rand")
                {
                    var randFn = module.GetNamedFunction("rand");
                    var randFnType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(randFnType, randFn, Array.Empty<LLVMValueRef>(), "rand_val");
                }
                else if (call.Callee == "rand_range")
                {
                    var minVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var maxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var randFn = module.GetNamedFunction("rand");
                    var randFnType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    var r = builder.BuildCall2(randFnType, randFn, Array.Empty<LLVMValueRef>(), "rand_r");
                    var diff = builder.BuildSub(maxVal, minVal, "diff");
                    var span = builder.BuildAdd(diff, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "span");
                    var rem = builder.BuildSRem(r, span, "rem");
                    return builder.BuildAdd(minVal, rem, "rand_in_range");
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
                else if (call.Callee is "render_profiler" or "render_debug_overlay" or "ecs::render_profiler")
                {
                    var f = module.GetNamedFunction("world_render_profiler");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0) }, false);
                    var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { w }, "");
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
                else if (call.Callee is "load_texture" or "rl_load_texture")
                {
                    var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mallocFunc = module.GetNamedFunction("malloc");
                    var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                    var texBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "tex_buf");

                    var loadTexFunc = module.GetNamedFunction("LoadTexture");
                    var loadTexType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
                    builder.BuildCall2(loadTexType, loadTexFunc, new[] { texBuf, pathVal }, "");

                    return builder.BuildPtrToInt(texBuf, context.Int64Type, "tex_handle");
                }
                else if (call.Callee is "get_texture_width" or "rl_get_texture_width")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var wPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "tex_w_ptr");
                    return builder.BuildLoad2(context.Int32Type, wPtr, "tex_w");
                }
                else if (call.Callee is "get_texture_height" or "rl_get_texture_height")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var hPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 2) }, "tex_h_ptr");
                    return builder.BuildLoad2(context.Int32Type, hPtr, "tex_h");
                }
                else if (call.Callee is "draw_texture" or "rl_draw_texture")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var posX = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var posY = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef tintVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var a = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        tintVal = PackRgbaColor(context, builder, r, g, b, a);
                    }
                    else if (call.Arguments.Count == 4)
                    {
                        tintVal = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    else
                    {
                        tintVal = LLVMValueRef.CreateConstInt(context.Int32Type, 0xFFFFFFFF);
                    }
                    var f = module.GetNamedFunction("DrawTexture");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    return builder.BuildCall2(ft, f, new[] { texPtr, posX, posY, tintVal }, "");
                }
                else if (call.Callee is "draw_texture_pro" or "rl_draw_texture_pro")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");

                    var rectType = LLVMTypeRef.CreateArray(context.FloatType, 4);
                    var srcRect = CreateEntryBlockAlloca(context, function, rectType, "src_rect");
                    var dstRect = CreateEntryBlockAlloca(context, function, rectType, "dst_rect");
                    var rectZeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    // source rect: args 1, 2, 3, 4
                    for (int i = 0; i < 4; i++)
                    {
                        var v = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1 + i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                        var slot = builder.BuildInBoundsGEP2(rectType, srcRect, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"src_{i}");
                        builder.BuildStore(v, slot);
                    }

                    // dest rect: args 5, 6, 7, 8
                    for (int i = 0; i < 4; i++)
                    {
                        var v = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[5 + i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                        var slot = builder.BuildInBoundsGEP2(rectType, dstRect, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"dst_{i}");
                        builder.BuildStore(v, slot);
                    }

                    // origin: args 9, 10
                    var ox = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[9], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var oy = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[10], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var vec2Type = LLVMTypeRef.CreateArray(context.FloatType, 2);
                    var originAlloca = CreateEntryBlockAlloca(context, function, vec2Type, "origin_alloca");
                    builder.BuildStore(ox, builder.BuildInBoundsGEP2(vec2Type, originAlloca, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 0) }, "ox_slot"));
                    builder.BuildStore(oy, builder.BuildInBoundsGEP2(vec2Type, originAlloca, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "oy_slot"));
                    var originI64 = builder.BuildLoad2(context.Int64Type, builder.BuildBitCast(originAlloca, LLVMTypeRef.CreatePointer(context.Int64Type, 0), "orig_i64_ptr"), "orig_i64");

                    // rotation: arg 11
                    var rot = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[11], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));

                    // tint: arg 12 (or 12..15 for r, g, b, a)
                    LLVMValueRef tintVal;
                    if (call.Arguments.Count >= 16)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[12], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[13], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[14], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var a = CompileExpression(context, module, builder, function, call.Arguments[15], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        tintVal = PackRgbaColor(context, builder, r, g, b, a);
                    }
                    else if (call.Arguments.Count > 12)
                    {
                        tintVal = CompileExpression(context, module, builder, function, call.Arguments[12], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    else
                    {
                        tintVal = LLVMValueRef.CreateConstInt(context.Int32Type, 0xFFFFFFFF);
                    }

                    var f = module.GetNamedFunction("DrawTexturePro");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType, i8PtrType, context.Int64Type, context.FloatType, context.Int32Type }, false);
                    var srcPtr = builder.BuildBitCast(srcRect, i8PtrType, "src_ptr");
                    var dstPtr = builder.BuildBitCast(dstRect, i8PtrType, "dst_ptr");
                    return builder.BuildCall2(ft, f, new[] { texPtr, srcPtr, dstPtr, originI64, rot, tintVal }, "");
                }
                else if (call.Callee is "unload_texture" or "rl_unload_texture")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var f = module.GetNamedFunction("UnloadTexture");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    builder.BuildCall2(ft, f, new[] { texPtr }, "");
                    var freeFunc = module.GetNamedFunction("free");
                    var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(freeType, freeFunc, new[] { texPtr }, "");
                }
                else if (call.Callee is "init_audio_device" or "rl_init_audio_device")
                {
                    var f = module.GetNamedFunction("InitAudioDevice");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "close_audio_device" or "rl_close_audio_device")
                {
                    var f = module.GetNamedFunction("CloseAudioDevice");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "is_audio_device_ready" or "rl_is_audio_device_ready")
                {
                    var f = module.GetNamedFunction("IsAudioDeviceReady");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "audio_ready");
                }
                else if (call.Callee is "load_sound" or "rl_load_sound")
                {
                    var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mallocFunc = module.GetNamedFunction("malloc");
                    var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                    var sndBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 64) }, "snd_buf");

                    var loadSndFunc = module.GetNamedFunction("LoadSound");
                    var loadSndType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
                    builder.BuildCall2(loadSndType, loadSndFunc, new[] { sndBuf, pathVal }, "");

                    return builder.BuildPtrToInt(sndBuf, context.Int64Type, "snd_handle");
                }
                else if (call.Callee is "play_sound" or "rl_play_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("PlaySound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "stop_sound" or "rl_stop_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("StopSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "pause_sound" or "rl_pause_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("PauseSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "resume_sound" or "rl_resume_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("ResumeSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "is_sound_playing" or "rl_is_sound_playing")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("IsSoundPlaying");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "snd_playing");
                }
                else if (call.Callee is "set_sound_volume" or "rl_set_sound_volume")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var vol = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var f = module.GetNamedFunction("SetSoundVolume");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.FloatType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr, vol }, "");
                }
                else if (call.Callee is "unload_sound" or "rl_unload_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("UnloadSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                    var freeFunc = module.GetNamedFunction("free");
                    var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(freeType, freeFunc, new[] { sndPtr }, "");
                }
                else if (call.Callee is "begin_mode_2d" or "rl_begin_mode_2d")
                {
                    // Camera2D: Vector2 offset (0..1), Vector2 target (2..3), float rotation (4), float zoom (5)
                    var camType = LLVMTypeRef.CreateArray(context.FloatType, 6);
                    var camAlloca = CreateEntryBlockAlloca(context, function, camType, "cam_alloca");
                    var camZeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    // offset.x, offset.y
                    var ox = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var oy = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    // target.x, target.y
                    var tx = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var ty = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    // rotation, zoom
                    var rot = call.Arguments.Count > 4 ? EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)) : LLVMValueRef.CreateConstReal(context.FloatType, 0.0);
                    var zoom = call.Arguments.Count > 5 ? EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)) : LLVMValueRef.CreateConstReal(context.FloatType, 1.0);

                    var vals = new[] { ox, oy, tx, ty, rot, zoom };
                    for (int i = 0; i < 6; i++)
                    {
                        var slot = builder.BuildInBoundsGEP2(camType, camAlloca, new[] { camZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"cam_{i}");
                        builder.BuildStore(vals[i], slot);
                    }

                    var camPtr = builder.BuildBitCast(camAlloca, i8PtrType, "cam_ptr");
                    var f = module.GetNamedFunction("BeginMode2D");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { camPtr }, "");
                }
                else if (call.Callee is "end_mode_2d" or "rl_end_mode_2d")
                {
                    var f = module.GetNamedFunction("EndMode2D");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
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
                    if (call.Callee.Contains("::"))
                    {
                        funcName = call.Callee.Replace("::", "_");
                    }
                    else if (_typeChecker.Pipelines.Any(p => p.Name == call.Callee))
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
            else
            {
                // Dynamic array: [T] => { T* data, i32 length, i32 capacity }
                var elemType = MapType(context, inner.Trim(), ecs);
                var elemPtrType = LLVMTypeRef.CreatePointer(elemType, 0);
                return LLVMTypeRef.CreateStruct(new[] { elemPtrType, context.Int32Type, context.Int32Type }, false);
            }
        }

        if (typeName != null && (typeName.StartsWith("Map<") || typeName.StartsWith("HashMap<")) && typeName.EndsWith(">"))
        {
            var mapTypeSym = TypeSymbol.FromName(typeName);
            mapTypeSym.TryGetMapInfo(out var kSym, out var vSym);
            if (_mapEmitter != null)
            {
                return _mapEmitter.GetMapType(kSym.Name, vSym.Name, (t) => MapType(context, t, ecs));
            }
            var i8Ptr = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
            return LLVMTypeRef.CreateStruct(new[] { i8Ptr, context.Int32Type, context.Int32Type }, false);
        }

        if (typeName != null && typeName.StartsWith("Option<") && typeName.EndsWith(">"))
        {
            var optTypeSym = TypeSymbol.FromName(typeName);
            optTypeSym.TryGetOptionInfo(out var valSym);
            var valLlvmType = MapType(context, valSym.Name, ecs);
            return LLVMTypeRef.CreateStruct(new[] { context.Int32Type, valLlvmType }, false);
        }

        if (typeName != null && typeName.StartsWith("Result<") && typeName.EndsWith(">"))
        {
            var resTypeSym = TypeSymbol.FromName(typeName);
            resTypeSym.TryGetResultInfo(out var okSym, out var errSym);
            var okLlvmType = MapType(context, okSym.Name, ecs);
            var errLlvmType = MapType(context, errSym.Name, ecs);
            return LLVMTypeRef.CreateStruct(new[] { context.Int32Type, okLlvmType, errLlvmType }, false);
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
            "Commands" or "commands" => ecs != null ? LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0) : LLVMTypeRef.CreatePointer(context.Int8Type, 0),
            "Entity" or "entity" => context.Int32Type,
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

    private unsafe void SetDebugLocation(LLVMContextRef context, LLVMBuilderRef builder, SourceSpan span)
    {
        if (_diBuilder.HasValue && _currentSubprogram.HasValue && span.Line > 0)
        {
            var loc = LlvmApi.DIBuilderCreateDebugLocation(
                context,
                (uint)Math.Max(1, span.Line),
                (uint)Math.Max(1, span.Column),
                _currentSubprogram.Value,
                null);
            builder.CurrentDebugLocation = LlvmApi.MetadataAsValue(context, loc);
        }
    }

    private static LLVMValueRef FindWorldPointer(
        LLVMContextRef context,
        LLVMBuilderRef builder,
        EcsRuntimeEmitter? ecs,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        if (ecs == null) return LLVMValueRef.CreateConstPointerNull(i8PtrType);

        foreach (var (name, allocaPtr) in locals)
        {
            if (varTypes.TryGetValue(name, out var tName) && (tName is "World" or "Commands"))
            {
                var worldPtrType = LLVMTypeRef.CreatePointer(ecs.WorldStructType, 0);
                var loaded = builder.BuildLoad2(worldPtrType, allocaPtr, $"{name}_wptr");
                return builder.BuildBitCast(loaded, i8PtrType, "w_i8");
            }
        }
        return LLVMValueRef.CreateConstPointerNull(i8PtrType);
    }

    private unsafe LLVMValueRef GetOrCreateStringConcatFunction(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType, EcsRuntimeEmitter? ecs)
    {
        var fn = module.GetNamedFunction("rt_str_concat");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, i8PtrType }, false);
        fn = module.AddFunction("rt_str_concat", fnType);
        var entry = fn.AppendBasicBlock("entry");
        var b = context.CreateBuilder();
        b.PositionAtEnd(entry);

        var worldParam = fn.GetParam(0);
        var s1 = fn.GetParam(1);
        var s2 = fn.GetParam(2);

        var strlenFunc = module.GetNamedFunction("strlen");
        var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);

        var len1 = b.BuildCall2(strlenType, strlenFunc, new[] { s1 }, "len1");
        var len2 = b.BuildCall2(strlenType, strlenFunc, new[] { s2 }, "len2");
        var totalLen = b.BuildAdd(len1, len2, "totallen");
        var allocSize = b.BuildAdd(totalLen, LLVMValueRef.CreateConstInt(context.Int64Type, 1), "allocsize");

        LLVMValueRef newBuf;
        if (_arenaEmitter != null && ecs != null)
        {
            var arenaAlloc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
            var allocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(arenaAlloc);
            newBuf = b.BuildCall2(allocType, arenaAlloc, new[] { worldParam, allocSize }, "arena_buf");
        }
        else
        {
            var mallocFunc = module.GetNamedFunction("malloc");
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            newBuf = b.BuildCall2(mallocType, mallocFunc, new[] { allocSize }, "newbuf");
        }

        var memcpyFunc = module.GetNamedFunction("memcpy");
        var memcpyType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, context.Int64Type }, false);

        b.BuildCall2(memcpyType, memcpyFunc, new[] { newBuf, s1, len1 }, "");

        var dest2 = b.BuildInBoundsGEP2(context.Int8Type, newBuf, new[] { len1 }, "dest2");
        b.BuildCall2(memcpyType, memcpyFunc, new[] { dest2, s2, len2 }, "");

        var nullPos = b.BuildInBoundsGEP2(context.Int8Type, newBuf, new[] { totalLen }, "nullpos");
        b.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), nullPos);

        b.BuildRet(newBuf);
        b.Dispose();
        return fn;
    }

    private unsafe LLVMValueRef GetOrCreateToStringI32(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType, EcsRuntimeEmitter? ecs)
    {
        var fn = module.GetNamedFunction("rt_to_string_i32");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int32Type }, false);
        fn = module.AddFunction("rt_to_string_i32", fnType);
        var entry = fn.AppendBasicBlock("entry");
        var b = context.CreateBuilder();
        b.PositionAtEnd(entry);

        var worldParam = fn.GetParam(0);
        var val = fn.GetParam(1);

        LLVMValueRef buf;
        if (_arenaEmitter != null && ecs != null)
        {
            var arenaAlloc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
            var allocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(arenaAlloc);
            buf = b.BuildCall2(allocType, arenaAlloc, new[] { worldParam, LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "arena_buf");
        }
        else
        {
            var mallocFunc = module.GetNamedFunction("malloc");
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            buf = b.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "buf");
        }

        var sprintfFunc = module.GetNamedFunction("sprintf");
        var sprintfType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType, i8PtrType }, true);
        var fmt = b.BuildGlobalStringPtr("%d", "fmt_d");
        b.BuildCall2(sprintfType, sprintfFunc, new[] { buf, fmt, val }, "");

        b.BuildRet(buf);
        b.Dispose();
        return fn;
    }

    private unsafe LLVMValueRef GetOrCreateToStringF32(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType, EcsRuntimeEmitter? ecs)
    {
        var fn = module.GetNamedFunction("rt_to_string_f32");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.FloatType }, false);
        fn = module.AddFunction("rt_to_string_f32", fnType);
        var entry = fn.AppendBasicBlock("entry");
        var b = context.CreateBuilder();
        b.PositionAtEnd(entry);

        var worldParam = fn.GetParam(0);
        var val = fn.GetParam(1);
        var valDbl = b.BuildFPExt(val, context.DoubleType, "val_dbl");

        LLVMValueRef buf;
        if (_arenaEmitter != null && ecs != null)
        {
            var arenaAlloc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
            var allocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(arenaAlloc);
            buf = b.BuildCall2(allocType, arenaAlloc, new[] { worldParam, LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "arena_buf");
        }
        else
        {
            var mallocFunc = module.GetNamedFunction("malloc");
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            buf = b.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "buf");
        }

        var sprintfFunc = module.GetNamedFunction("sprintf");
        var sprintfType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType, i8PtrType }, true);
        var fmt = b.BuildGlobalStringPtr("%.2f", "fmt_f");
        b.BuildCall2(sprintfType, sprintfFunc, new[] { buf, fmt, valDbl }, "");

        b.BuildRet(buf);
        b.Dispose();
        return fn;
    }

    private unsafe LLVMValueRef GetOrCreateToStringBool(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType)
    {
        var fn = module.GetNamedFunction("rt_to_string_bool");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int1Type }, false);
        fn = module.AddFunction("rt_to_string_bool", fnType);
        var entry = fn.AppendBasicBlock("entry");
        var b = context.CreateBuilder();
        b.PositionAtEnd(entry);

        var val = fn.GetParam(0);
        var strTrue = b.BuildGlobalStringPtr("true", "str_true");
        var strFalse = b.BuildGlobalStringPtr("false", "str_false");
        var res = b.BuildSelect(val, strTrue, strFalse, "sel_bool");

        b.BuildRet(res);
        b.Dispose();
        return fn;
    }

    private unsafe LLVMValueRef GetOrCreateStringEq(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType)
    {
        var fn = module.GetNamedFunction("rt_str_eq");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, i8PtrType }, false);
        fn = module.AddFunction("rt_str_eq", fnType);
        var entry = fn.AppendBasicBlock("entry");
        var b = context.CreateBuilder();
        b.PositionAtEnd(entry);

        var s1 = fn.GetParam(0);
        var s2 = fn.GetParam(1);
        var strcmpFunc = module.GetNamedFunction("strcmp");
        var strcmpType = LLVMTypeRef.CreateFunction(context.Int32Type, new[] { i8PtrType, i8PtrType }, false);
        var cmp = b.BuildCall2(strcmpType, strcmpFunc, new[] { s1, s2 }, "cmp");
        var eq = b.BuildICmp(LLVMIntPredicate.LLVMIntEQ, cmp, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "eq");

        b.BuildRet(eq);
        b.Dispose();
        return fn;
    }

    private unsafe LLVMValueRef EmitToString(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef val,
        TypeSymbol type,
        LLVMTypeRef i8PtrType,
        EcsRuntimeEmitter? ecs,
        LLVMValueRef worldPtr)
    {
        if (type == TypeSymbol.String)
        {
            return val;
        }

        if (type == TypeSymbol.I32 || type == TypeSymbol.Entity)
        {
            var fn = GetOrCreateToStringI32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, val }, "str_i32");
        }

        if (type == TypeSymbol.F32)
        {
            var fn = GetOrCreateToStringF32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, val }, "str_f32");
        }

        if (type == TypeSymbol.Bool)
        {
            var fn = GetOrCreateToStringBool(context, module, i8PtrType);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { val }, "str_bool");
        }

        if (val.TypeOf == context.Int32Type)
        {
            var fn = GetOrCreateToStringI32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, val }, "str_i32");
        }
        if (val.TypeOf == context.FloatType)
        {
            var fn = GetOrCreateToStringF32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, val }, "str_f32");
        }
        if (val.TypeOf == context.Int1Type)
        {
            var fn = GetOrCreateToStringBool(context, module, i8PtrType);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { val }, "str_bool");
        }

        return val;
    }
}
