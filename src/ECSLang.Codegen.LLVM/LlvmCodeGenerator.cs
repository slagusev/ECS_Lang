using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
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
    private int _lambdaCounter = 0;
    private readonly Stack<(LLVMBasicBlockRef CondBB, LLVMBasicBlockRef ExitBB)> _loopStack = new();

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
        _lambdaCounter = 0;
        _loopStack.Clear();

        using var context = LLVMContextRef.Create();
        using var module = context.CreateModuleWithName("ecs_module");
        using var builder = context.CreateBuilder();

        string targetTriple = _options.Target.Triple;
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

        // Debug Info Setup (CodeView on Windows, DWARF on Linux/macOS)
        if (_options.GenerateDebugInfo)
        {
            if (_options.Target.IsWindows)
            {
                module.AddModuleFlag("CodeView", LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, 1);
            }
            else
            {
                module.AddModuleFlag("Dwarf Version", LLVMModuleFlagBehavior.LLVMModuleFlagBehaviorWarning, 4);
            }
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

        if (_options.Target.IsWindows)
        {
            // int _getch()
            var getchType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
            module.AddFunction("_getch", getchType);
        }

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
        module.AddFunction("IsWindowReady", LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("WindowShouldClose", LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false));
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
        module.AddFunction("IsKeyDown", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyPressed", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyReleased", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsKeyUp", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));
        module.AddFunction("GetMouseX", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("GetMouseY", LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false));
        module.AddFunction("IsMouseButtonDown", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));
        module.AddFunction("IsMouseButtonPressed", LLVMTypeRef.CreateFunction(context.Int8Type, new[] { context.Int32Type }, false));

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
        var ecsEmitter = new EcsRuntimeEmitter(context, module, builder, _typeChecker, _diagnostics, _options);
        ecsEmitter.EmitEcsDeclarations(dataLayout);

        // Emit Multi-Archetype Runtime (spawn, add, remove, has, setters, sort)
        ecsEmitter.EmitMultiArchetypeRuntime(dataLayout, reallocType, reallocFunc, memcpyType, memcpyFunc, memsetType, memsetFunc, mallocType, mallocFunc, freeType, freeFunc);
        ecsEmitter.EmitProfilerRuntime(dataLayout);
        EmitNetworkDeclarations(context, module);

        var allDeclarations = program.Declarations
            .Concat(_typeChecker.MonomorphizedDeclarations)
            .ToList();

        // Forward-declare regular functions and impl methods so any function or system can call them
        foreach (var decl in allDeclarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                if (fnDecl.TypeParameters != null && fnDecl.TypeParameters.Count > 0)
                    continue;

                string fnName = fnDecl.Name;
                if (module.GetNamedFunction(fnName).Handle == IntPtr.Zero)
                {
                    var returnType = MapType(context, fnDecl.ReturnType, ecsEmitter);
                    var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName, ecsEmitter)).ToArray();
                    var funcType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
                    module.AddFunction(fnName, funcType);
                    var cleanName = TypeSymbol.ToMonomorphizedIdentifier(fnName);
                    if (cleanName != fnName && module.GetNamedFunction(cleanName).Handle == IntPtr.Zero)
                    {
                        module.AddFunction(cleanName, funcType);
                    }
                }
            }
            else if (decl is ImplDeclaration impl)
            {
                if (impl.TypeParameters != null && impl.TypeParameters.Count > 0)
                    continue;

                foreach (var method in impl.Methods)
                {
                    var mangledName = $"{impl.StructName}_{method.Name}";
                    var cleanMangledName = $"{TypeSymbol.ToMonomorphizedIdentifier(impl.StructName)}_{method.Name}";
                    if (module.GetNamedFunction(cleanMangledName).Handle != IntPtr.Zero)
                        continue;

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
                    module.AddFunction(cleanMangledName, funcType);
                    if (mangledName != cleanMangledName && module.GetNamedFunction(mangledName).Handle == IntPtr.Zero)
                    {
                        module.AddFunction(mangledName, funcType);
                    }
                }
            }
        }

        // Compile ECS Systems
        var compiledSystems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decl in allDeclarations)
        {
            if (decl is SystemDeclaration sys)
            {
                if (sys.TypeParameters != null && sys.TypeParameters.Count > 0)
                    continue;

                if (compiledSystems.Add(sys.Name))
                {
                    CompileSystem(context, module, builder, sys, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
                }
            }
        }

        // Compile ECS Pipelines
        foreach (var decl in allDeclarations)
        {
            if (decl is PipelineDeclaration pipe)
            {
                CompilePipeline(context, module, builder, pipe, ecsEmitter);
            }
        }

        // Compile regular functions
        var compiledFunctions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decl in allDeclarations)
        {
            if (decl is FunctionDeclaration fnDecl)
            {
                if (fnDecl.TypeParameters != null && fnDecl.TypeParameters.Count > 0)
                    continue;

                if (compiledFunctions.Add(fnDecl.Name))
                {
                    CompileFunction(context, module, builder, fnDecl, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
                }
            }
            else if (decl is ImplDeclaration impl)
            {
                if (impl.TypeParameters != null && impl.TypeParameters.Count > 0)
                    continue;

                foreach (var method in impl.Methods)
                {
                    var cleanMangledName = $"{TypeSymbol.ToMonomorphizedIdentifier(impl.StructName)}_{method.Name}";
                    if (compiledFunctions.Add(cleanMangledName))
                    {
                        CompileMethod(context, module, builder, impl.StructName, method, ecsEmitter, putsType, putsFunc, printfType, printfFunc);
                    }
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
        if (function.Handle == IntPtr.Zero)
        {
            function = module.GetNamedFunction(TypeSymbol.ToMonomorphizedIdentifier(fnDecl.Name));
        }

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
                var msg = builder.BuildGlobalStringPtr("Press any key to exit...", "prompt_exit");
                builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_exit");
                if (_options.Target.IsWindows)
                {
                    var getchFunc = module.GetNamedFunction("_getch");
                    var getchType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    builder.BuildCall2(getchType, getchFunc, Array.Empty<LLVMValueRef>(), "auto_wait_key");
                }
                else
                {
                    var getcharFunc = module.GetNamedFunction("getchar");
                    var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "auto_wait_key");
                }
            }
            if (returnType == context.VoidType)
                builder.BuildRetVoid();
            else
                builder.BuildRet(LLVMValueRef.CreateConstNull(returnType));
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
        if (function.Handle == IntPtr.Zero)
        {
            mangledName = $"{TypeSymbol.ToMonomorphizedIdentifier(structName)}_{methodDecl.Name}";
            function = module.GetNamedFunction(mangledName);
        }
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
                builder.BuildRet(LLVMValueRef.CreateConstNull(returnType));
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

    private static LLVMValueRef CoerceValue(LLVMBuilderRef builder, LLVMContextRef context, LLVMValueRef val, LLVMTypeRef targetType)
    {
        if (val.TypeOf == targetType) return val;

        if (targetType == context.FloatType)
        {
            if (val.TypeOf == context.DoubleType)
                return builder.BuildFPTrunc(val, context.FloatType, "fptrunc");
            if (val.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
                return builder.BuildSIToFP(val, context.FloatType, "sitofp");
        }
        else if (targetType == context.DoubleType)
        {
            if (val.TypeOf == context.FloatType)
                return builder.BuildFPExt(val, context.DoubleType, "fpext");
            if (val.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
                return builder.BuildSIToFP(val, context.DoubleType, "sitofp");
        }
        else if (targetType.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (val.TypeOf == context.FloatType || val.TypeOf == context.DoubleType)
                return builder.BuildFPToSI(val, targetType, "fptosi");
            if (val.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            {
                if (val.TypeOf.IntWidth < targetType.IntWidth)
                    return builder.BuildSExt(val, targetType, "sext");
                if (val.TypeOf.IntWidth > targetType.IntWidth)
                    return builder.BuildTrunc(val, targetType, "trunc");
            }
        }

        return val;
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
                return context.GetStructType(new[] { elemPtrType, context.Int32Type, context.Int32Type }, false);
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
            return context.GetStructType(new[] { i8Ptr, context.Int32Type, context.Int32Type }, false);
        }

        if (typeName != null && typeName.StartsWith("Option<") && typeName.EndsWith(">"))
        {
            var optTypeSym = TypeSymbol.FromName(typeName);
            optTypeSym.TryGetOptionInfo(out var valSym);
            var valLlvmType = MapType(context, valSym.Name, ecs);
            return context.GetStructType(new[] { context.Int32Type, valLlvmType }, false);
        }

        if (typeName != null && typeName.StartsWith("Result<") && typeName.EndsWith(">"))
        {
            var resTypeSym = TypeSymbol.FromName(typeName);
            resTypeSym.TryGetResultInfo(out var okSym, out var errSym);
            var okLlvmType = MapType(context, okSym.Name, ecs);
            var errLlvmType = MapType(context, errSym.Name, ecs);
            return context.GetStructType(new[] { context.Int32Type, okLlvmType, errLlvmType }, false);
        }

        if (typeName != null && (typeName.StartsWith("fn(") || typeName.StartsWith("closure(") || TypeSymbol.FromName(typeName).IsFunction))
        {
            var i8Ptr = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
            return context.GetStructType(new[] { i8Ptr, i8Ptr }, false);
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

    private unsafe LLVMValueRef GetOrCreateToStringI64(LLVMContextRef context, LLVMModuleRef module, LLVMTypeRef i8PtrType, EcsRuntimeEmitter? ecs)
    {
        var fn = module.GetNamedFunction("rt_to_string_i64");
        if (fn.Handle != IntPtr.Zero) return fn;

        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int64Type }, false);
        fn = module.AddFunction("rt_to_string_i64", fnType);
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
        var fmt = b.BuildGlobalStringPtr("%lld", "fmt_lld");
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
        if (val.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (val.TypeOf == context.Int1Type)
            {
                var fnBool = GetOrCreateToStringBool(context, module, i8PtrType);
                var fnBoolType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fnBool);
                return builder.BuildCall2(fnBoolType, fnBool, new[] { val }, "str_bool");
            }
            if (val.TypeOf == context.Int64Type)
            {
                var fn64 = GetOrCreateToStringI64(context, module, i8PtrType, ecs);
                var fn64Type = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn64);
                return builder.BuildCall2(fn64Type, fn64, new[] { worldPtr, val }, "str_i64");
            }
            var i32Val = val.TypeOf == context.Int32Type ? val : builder.BuildZExt(val, context.Int32Type, "zext_i32");
            var fn = GetOrCreateToStringI32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, i32Val }, "str_i32");
        }

        if (val.TypeOf == context.FloatType || val.TypeOf == context.DoubleType)
        {
            var fn = GetOrCreateToStringF32(context, module, i8PtrType, ecs);
            var fnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fn);
            return builder.BuildCall2(fnType, fn, new[] { worldPtr, val }, "str_f32");
        }

        if (type == TypeSymbol.String || val.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
        {
            return val;
        }

        return val;
    }

}
