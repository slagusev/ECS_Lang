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

        // void* malloc(size_t size)
        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
        var mallocFunc = module.AddFunction("malloc", mallocType);

        // void free(void* ptr)
        var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
        var freeFunc = module.AddFunction("free", freeType);

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
        ecsEmitter.EmitMultiArchetypeRuntime(dataLayout, reallocType, reallocFunc, memcpyType, memcpyFunc, mallocType, mallocFunc, freeType, freeFunc);

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
                CompilePipeline(context, module, builder, pipe);
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
        var sysFuncType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
        var sysFunc = module.AddFunction($"system_{sys.Name}", sysFuncType);
        var entryBB = sysFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

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
        var numArchs = builder.BuildLoad2(context.Int32Type, ecs.GetArchetypeCountGlobal(), "num_archs");
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
        var tablesBase = builder.BuildLoad2(archPtrType, ecs.GetArchetypeTablesGlobal(), "tables_base");
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
                var resGlobal = ecs.GetResourceGlobal(qp.TypeName);
                locals[qp.Name] = resGlobal;
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
    }

    private void CompilePipeline(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        PipelineDeclaration pipe)
    {
        var pipeFuncType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
        var pipeFunc = module.AddFunction($"pipeline_{pipe.Name}", pipeFuncType);
        var entryBB = pipeFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        foreach (var stage in pipe.Stages)
        {
            foreach (var action in stage.Actions)
            {
                if (action is SystemCallAction call)
                {
                    var sysFunc = module.GetNamedFunction($"system_{call.SystemName}");
                    if (sysFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                        builder.BuildCall2(voidFuncType, sysFunc, Array.Empty<LLVMValueRef>());
                    }
                }
                else if (action is ParallelAction par)
                {
                    // For now execute each system sequentially or in parallel
                    foreach (var sCall in par.Systems)
                    {
                        var sysFunc = module.GetNamedFunction($"system_{sCall.SystemName}");
                        if (sysFunc.Handle != IntPtr.Zero)
                        {
                            var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                            builder.BuildCall2(voidFuncType, sysFunc, Array.Empty<LLVMValueRef>());
                        }
                    }
                }
                else if (action is SortHierarchyAction)
                {
                    var sortFunc = module.GetNamedFunction("world_sort_hierarchy");
                    if (sortFunc.Handle != IntPtr.Zero)
                    {
                        var voidFuncType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                        builder.BuildCall2(voidFuncType, sortFunc, Array.Empty<LLVMValueRef>());
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
        var returnType = MapType(context, fnDecl.ReturnType);
        var paramTypes = fnDecl.Parameters.Select(p => MapType(context, p.TypeName)).ToArray();
        var funcType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
        var function = module.AddFunction(fnDecl.Name, funcType);

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

        CompileBlock(context, module, builder, function, fnDecl.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

        // Ensure terminating return if not explicitly present
        var lastBlock = builder.InsertBlock;
        if (lastBlock.Terminator.Handle == IntPtr.Zero)
        {
            if (returnType == context.VoidType)
                builder.BuildRetVoid();
            else
                builder.BuildRet(LLVMValueRef.CreateConstInt(context.Int32Type, 0, false));
        }
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
        LLVMValueRef printfFunc)
    {
        foreach (var stmt in block.Statements)
        {
            if (builder.InsertBlock.Terminator.Handle != IntPtr.Zero)
                break;

            switch (stmt)
            {
                case VariableDeclarationStatement varDecl:
                    var tName = varDecl.TypeName ?? _typeChecker.GetNodeType(varDecl.Initializer).Name;
                    var varType = MapType(context, tName);
                    if (!locals.TryGetValue(varDecl.Name, out var alloca))
                    {
                        alloca = CreateEntryBlockAlloca(context, function, varType, varDecl.Name);
                        locals[varDecl.Name] = alloca;
                    }
                    var initVal = CompileExpression(context, module, builder, varDecl.Initializer, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    builder.BuildStore(initVal, alloca);
                    varTypes[varDecl.Name] = tName;
                    break;

                case AssignmentStatement assign:
                    if (locals.TryGetValue(assign.TargetName, out var targetPtr))
                    {
                        var newVal = CompileExpression(context, module, builder, assign.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        if (assign.MemberName != null && varTypes.TryGetValue(assign.TargetName, out var targetTypeName))
                        {
                            // Member assignment: pos.x = ... or pos.x += ...
                            var structType = ecs.GetComponentStructType(targetTypeName);
                            int fieldOffset = ecs.GetFieldOffset(targetTypeName, assign.MemberName);
                            var fieldGEP = builder.BuildStructGEP2(structType, targetPtr, (uint)fieldOffset, $"{assign.TargetName}_{assign.MemberName}_gep");

                            if (assign.Op == AssignmentOperator.Assign)
                            {
                                builder.BuildStore(newVal, fieldGEP);
                            }
                            else
                            {
                                var currentVal = builder.BuildLoad2(newVal.TypeOf, fieldGEP, "cur_fld");
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
                                builder.BuildStore(resVal, fieldGEP);
                            }
                        }
                        else
                        {
                            // Regular variable assignment
                            if (assign.Op == AssignmentOperator.Assign)
                            {
                                builder.BuildStore(newVal, targetPtr);
                            }
                            else
                            {
                                var currentVal = builder.BuildLoad2(newVal.TypeOf, targetPtr, $"{assign.TargetName}_cur");
                                var resVal = assign.Op switch
                                {
                                    AssignmentOperator.PlusAssign => newVal.TypeOf == context.FloatType
                                        ? builder.BuildFAdd(currentVal, newVal, "fadd")
                                        : builder.BuildAdd(currentVal, newVal, "add"),
                                    AssignmentOperator.MinusAssign => newVal.TypeOf == context.FloatType
                                        ? builder.BuildFSub(currentVal, newVal, "fsub")
                                        : builder.BuildSub(currentVal, newVal, "sub"),
                                    _ => newVal
                                };
                                builder.BuildStore(resVal, targetPtr);
                            }
                        }
                    }
                    break;

                case IfStatement ifStmt:
                    var condVal = CompileExpression(context, module, builder, ifStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var thenBlock = function.AppendBasicBlock("then");
                    var elseBlock = ifStmt.ElseBranch != null ? function.AppendBasicBlock("else") : default;
                    var mergeBlock = function.AppendBasicBlock("if_merge");

                    var falseDest = ifStmt.ElseBranch != null ? elseBlock : mergeBlock;
                    builder.BuildCondBr(condVal, thenBlock, falseDest);

                    builder.PositionAtEnd(thenBlock);
                    CompileBlock(context, module, builder, function, ifStmt.ThenBranch, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(mergeBlock);

                    if (ifStmt.ElseBranch != null)
                    {
                        builder.PositionAtEnd(elseBlock);
                        if (ifStmt.ElseBranch is BlockStatement elseStmtBlock)
                            CompileBlock(context, module, builder, function, elseStmtBlock, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

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
                    var loopCondVal = CompileExpression(context, module, builder, whileStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    builder.BuildCondBr(loopCondVal, whileBodyBB, whileExitBB);

                    builder.PositionAtEnd(whileBodyBB);
                    CompileBlock(context, module, builder, function, whileStmt.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(whileCondBB);

                    builder.PositionAtEnd(whileExitBB);
                    break;

                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                    {
                        var retVal = CompileExpression(context, module, builder, retStmt.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        builder.BuildRet(retVal);
                    }
                    else
                    {
                        builder.BuildRetVoid();
                    }
                    break;

                case ExpressionStatement exprStmt:
                    CompileExpression(context, module, builder, exprStmt.Expression, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    break;
            }
        }
    }

    private unsafe LLVMValueRef CompileExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        ExpressionNode expr,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
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
                    var llvmType = MapType(context, varType.Name);
                    return builder.BuildLoad2(llvmType, varPtr, ident.Name);
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case MemberAccessExpression mem:
                if (mem.Target is IdentifierExpression targetId && locals.TryGetValue(targetId.Name, out var structPtr))
                {
                    if (varTypes.TryGetValue(targetId.Name, out var structTypeName))
                    {
                        var structType = ecs.GetComponentStructType(structTypeName);
                        int offset = ecs.GetFieldOffset(structTypeName, mem.MemberName);
                        var fieldGEP = builder.BuildStructGEP2(structType, structPtr, (uint)offset, $"{targetId.Name}_{mem.MemberName}");
                        var fieldType = MapType(context, _typeChecker.GetNodeType(mem).Name);
                        return builder.BuildLoad2(fieldType, fieldGEP, $"{mem.MemberName}_val");
                    }
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case UnaryExpression un:
                var operand = CompileExpression(context, module, builder, un.Operand, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                return un.Operator switch
                {
                    UnaryOperator.Negate => operand.TypeOf == context.FloatType
                        ? builder.BuildFNeg(operand, "fneg_tmp")
                        : builder.BuildNeg(operand, "neg_tmp"),
                    UnaryOperator.LogicalNot => builder.BuildNot(operand, "not_tmp"),
                    _ => operand
                };

            case BinaryExpression bin:
                var left = CompileExpression(context, module, builder, bin.Left, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var right = CompileExpression(context, module, builder, bin.Right, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
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
                        var val = CompileExpression(context, module, builder, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        if (argType == TypeSymbol.String)
                        {
                            if (addNewline)
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

                    var argValues = call.Arguments.Select(a => CompileExpression(context, module, builder, a, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)).ToArray();
                    var targetFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(targetFunc);
                    string callName = targetFuncType.ReturnType == context.VoidType ? "" : $"{call.Callee}_call";
                    return builder.BuildCall2(targetFuncType, targetFunc, argValues, callName);
                }
        }

        return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
    }

    private static LLVMTypeRef MapType(LLVMContextRef context, string? typeName) => typeName switch
    {
        "f32" or "float" => context.FloatType,
        "f64" or "double" => context.DoubleType,
        "i64" or "u64" => context.Int64Type,
        "i32" or "u32" or "int" => context.Int32Type,
        "bool" => context.Int1Type,
        "string" or "str" => LLVMTypeRef.CreatePointer(context.Int8Type, 0),
        "void" => context.VoidType,
        _ => context.Int32Type
    };

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
}
