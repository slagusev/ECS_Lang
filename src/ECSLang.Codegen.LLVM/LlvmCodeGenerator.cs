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

        // Run type checker first
        _typeChecker.CheckProgram(program);
        if (_diagnostics.HasErrors)
        {
            return false;
        }

        // Initialize ECS Runtime declarations
        var ecsEmitter = new EcsRuntimeEmitter(context, module, builder, _typeChecker, _diagnostics);
        ecsEmitter.EmitEcsDeclarations();

        // Emit ECS World Helpers (spawn, setters)
        EmitWorldHelpers(context, module, builder, ecsEmitter, reallocType, reallocFunc);

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

    private void EmitWorldHelpers(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef reallocType,
        LLVMValueRef reallocFunc)
    {
        // 1. world_spawn(): returns new entity index (i32)
        var spawnFuncType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
        var spawnFunc = module.AddFunction("world_spawn", spawnFuncType);
        var spawnBB = spawnFunc.AppendBasicBlock("entry");
        builder.PositionAtEnd(spawnBB);

        var curCount = builder.BuildLoad2(context.Int32Type, ecs.GetArchetypeCountGlobal(), "cur_count");
        var curCap = builder.BuildLoad2(context.Int32Type, ecs.GetArchetypeCapGlobal(), "cur_cap");

        var needGrow = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount, curCap, "need_grow");
        var growBB = spawnFunc.AppendBasicBlock("grow");
        var afterGrowBB = spawnFunc.AppendBasicBlock("after_grow");
        builder.BuildCondBr(needGrow, growBB, afterGrowBB);

        // Grow block
        builder.PositionAtEnd(growBB);
        var capIsZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "cap_is_zero");
        var doubleCap = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 2), "double_cap");
        var newCap = builder.BuildSelect(capIsZero, LLVMValueRef.CreateConstInt(context.Int32Type, 32), doubleCap, "new_cap");
        builder.BuildStore(newCap, ecs.GetArchetypeCapGlobal());

        var newCap64 = builder.BuildZExt(newCap, context.Int64Type, "new_cap64");

        // Reallocate each column
        foreach (var (compName, _) in _typeChecker.Components)
        {
            var compStructType = ecs.GetComponentStructType(compName);
            var colGlobal = ecs.GetArchetypeColGlobal(compName);
            var colPtr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(compStructType, 0), colGlobal, $"{compName}_ptr");
            var colPtrI8 = builder.BuildBitCast(colPtr, LLVMTypeRef.CreatePointer(context.Int8Type, 0), $"{compName}_i8");

            // sizeof(compStruct) * newCap
            // We approximate struct size or use 64 bytes
            var structSize = LLVMValueRef.CreateConstInt(context.Int64Type, 32);
            var allocBytes = builder.BuildMul(newCap64, structSize, "alloc_bytes");
            var reallocCall = builder.BuildCall2(reallocType, reallocFunc, new[] { colPtrI8, allocBytes }, "realloc_call");
            var newTypedPtr = builder.BuildBitCast(reallocCall, LLVMTypeRef.CreatePointer(compStructType, 0), "new_typed_ptr");
            builder.BuildStore(newTypedPtr, colGlobal);
        }
        builder.BuildBr(afterGrowBB);

        // After grow: increment count and return old count
        builder.PositionAtEnd(afterGrowBB);
        var newCount = builder.BuildAdd(curCount, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_count");
        builder.BuildStore(newCount, ecs.GetArchetypeCountGlobal());
        builder.BuildRet(curCount);

        // 2. Setters for each component: world_set_Position(index, x, y)
        foreach (var (compName, compSym) in _typeChecker.Components)
        {
            var structType = ecs.GetComponentStructType(compName);
            var paramTypes = new List<LLVMTypeRef> { context.Int32Type };
            foreach (var f in compSym.Fields)
            {
                paramTypes.Add(MapType(context, f.Type.Name));
            }

            var setterType = LLVMTypeRef.CreateFunction(context.VoidType, paramTypes.ToArray(), false);
            var setterFunc = module.AddFunction($"world_set_{compName}", setterType);
            var bb = setterFunc.AppendBasicBlock("entry");
            builder.PositionAtEnd(bb);

            var indexParam = setterFunc.GetParam(0);
            var colGlobal = ecs.GetArchetypeColGlobal(compName);
            var colBasePtr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(structType, 0), colGlobal, "col_base");
            var elemPtr = builder.BuildInBoundsGEP2(structType, colBasePtr, new[] { indexParam }, "elem_ptr");

            for (int i = 0; i < compSym.Fields.Count; i++)
            {
                var fieldVal = setterFunc.GetParam((uint)(i + 1));
                var fieldGEP = builder.BuildStructGEP2(structType, elemPtr, (uint)i, $"{compSym.Fields[i].Name}_gep");
                builder.BuildStore(fieldVal, fieldGEP);
            }
            builder.BuildRetVoid();
        }

        // 3. Setters for resources: world_set_Time(dt)
        foreach (var (resName, resSym) in _typeChecker.Resources)
        {
            var structType = ecs.GetComponentStructType(resName);
            var paramTypes = resSym.Fields.Select(f => MapType(context, f.Type.Name)).ToArray();
            var setterType = LLVMTypeRef.CreateFunction(context.VoidType, paramTypes, false);
            var setterFunc = module.AddFunction($"world_set_{resName}", setterType);
            var bb = setterFunc.AppendBasicBlock("entry");
            builder.PositionAtEnd(bb);

            var resGlobal = ecs.GetResourceGlobal(resName);
            for (int i = 0; i < resSym.Fields.Count; i++)
            {
                var fieldVal = setterFunc.GetParam((uint)i);
                var fieldGEP = builder.BuildStructGEP2(structType, resGlobal, (uint)i, $"{resSym.Fields[i].Name}_gep");
                builder.BuildStore(fieldVal, fieldGEP);
            }
            builder.BuildRetVoid();
        }

        // 4. world_sort_hierarchy(): sorts entities so parents appear before children
        if (_typeChecker.Components.ContainsKey("ChildOf"))
        {
            var sortType = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            var sortFunc = module.AddFunction("world_sort_hierarchy", sortType);
            var sortEntryBB = sortFunc.AppendBasicBlock("entry");
            builder.PositionAtEnd(sortEntryBB);

            var totalCount = builder.BuildLoad2(context.Int32Type, ecs.GetArchetypeCountGlobal(), "total_count");
            var canSort = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, totalCount, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "can_sort");
            var doSortBB = sortFunc.AppendBasicBlock("do_sort");
            var exitSortBB = sortFunc.AppendBasicBlock("exit_sort");
            builder.BuildCondBr(canSort, doSortBB, exitSortBB);

            builder.PositionAtEnd(doSortBB);
            var count64 = builder.BuildZExt(totalCount, context.Int64Type, "count64");
            var bytesNeeded = builder.BuildMul(count64, LLVMValueRef.CreateConstInt(context.Int64Type, 4), "bytes_needed");

            var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            var mallocFunc = module.GetNamedFunction("malloc");
            if (mallocFunc.Handle == IntPtr.Zero)
                mallocFunc = module.AddFunction("malloc", mallocType);

            var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            var freeFunc = module.GetNamedFunction("free");
            if (freeFunc.Handle == IntPtr.Zero)
                freeFunc = module.AddFunction("free", freeType);

            var depthsRaw = builder.BuildCall2(mallocType, mallocFunc, new[] { bytesNeeded }, "depths_raw");
            var depthsPtr = depthsRaw;

            var childOfStruct = ecs.GetComponentStructType("ChildOf");
            var childOfColGlobal = ecs.GetArchetypeColGlobal("ChildOf");
            var childOfCol = builder.BuildLoad2(LLVMTypeRef.CreatePointer(childOfStruct, 0), childOfColGlobal, "child_of_col");

            // Fill initial depths
            var dInitCondBB = sortFunc.AppendBasicBlock("d_init_cond");
            var dInitBodyBB = sortFunc.AppendBasicBlock("d_init_body");
            var dInitExitBB = sortFunc.AppendBasicBlock("d_init_exit");

            var initIdx = builder.BuildAlloca(context.Int32Type, "init_i");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), initIdx);
            builder.BuildBr(dInitCondBB);

            builder.PositionAtEnd(dInitCondBB);
            var curInitI = builder.BuildLoad2(context.Int32Type, initIdx, "cur_init_i");
            var hasInitMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curInitI, totalCount, "has_init_more");
            builder.BuildCondBr(hasInitMore, dInitBodyBB, dInitExitBB);

            builder.PositionAtEnd(dInitBodyBB);
            var childOfElem = builder.BuildInBoundsGEP2(childOfStruct, childOfCol, new[] { curInitI }, "child_elem");
            var parentGEP = builder.BuildStructGEP2(childOfStruct, childOfElem, 0, "parent_gep");
            var parentId = builder.BuildLoad2(context.Int32Type, parentGEP, "parent_id");
            var isRoot = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, parentId, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_root");
            var depthVal = builder.BuildSelect(isRoot, LLVMValueRef.CreateConstInt(context.Int32Type, 0), LLVMValueRef.CreateConstInt(context.Int32Type, 1), "depth_val");

            var curDepthSlot = builder.BuildInBoundsGEP2(context.Int32Type, depthsPtr, new[] { curInitI }, "depth_slot");
            builder.BuildStore(depthVal, curDepthSlot);

            var nextInitI = builder.BuildAdd(curInitI, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_init_i");
            builder.BuildStore(nextInitI, initIdx);
            builder.BuildBr(dInitCondBB);

            builder.PositionAtEnd(dInitExitBB);

            // Simple Sort: outer loop i from 0 to totalCount - 2
            var outerCondBB = sortFunc.AppendBasicBlock("sort_outer_cond");
            var outerBodyBB = sortFunc.AppendBasicBlock("sort_outer_body");
            var outerExitBB = sortFunc.AppendBasicBlock("sort_outer_exit");

            var sortIAlloca = builder.BuildAlloca(context.Int32Type, "sort_i");
            var sortJAlloca = builder.BuildAlloca(context.Int32Type, "sort_j");
            builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), sortIAlloca);
            builder.BuildBr(outerCondBB);

            builder.PositionAtEnd(outerCondBB);
            var curSortI = builder.BuildLoad2(context.Int32Type, sortIAlloca, "cur_sort_i");
            var outerLimit = builder.BuildSub(totalCount, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "outer_limit");
            var outerMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortI, outerLimit, "outer_more");
            builder.BuildCondBr(outerMore, outerBodyBB, outerExitBB);

            builder.PositionAtEnd(outerBodyBB);
            var innerStart = builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "inner_start");
            builder.BuildStore(innerStart, sortJAlloca);

            var innerCondBB = sortFunc.AppendBasicBlock("sort_inner_cond");
            var innerBodyBB = sortFunc.AppendBasicBlock("sort_inner_body");
            var innerStepBB = sortFunc.AppendBasicBlock("sort_inner_step");

            builder.BuildBr(innerCondBB);

            builder.PositionAtEnd(innerCondBB);
            var curSortJ = builder.BuildLoad2(context.Int32Type, sortJAlloca, "cur_sort_j");
            var innerMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortJ, totalCount, "inner_more");
            builder.BuildCondBr(innerMore, innerBodyBB, innerStepBB);

            builder.PositionAtEnd(innerBodyBB);
            var depthISlot = builder.BuildInBoundsGEP2(context.Int32Type, depthsPtr, new[] { curSortI }, "di_slot");
            var depthJSlot = builder.BuildInBoundsGEP2(context.Int32Type, depthsPtr, new[] { curSortJ }, "dj_slot");
            var di = builder.BuildLoad2(context.Int32Type, depthISlot, "di");
            var dj = builder.BuildLoad2(context.Int32Type, depthJSlot, "dj");

            var needSwap = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, di, dj, "need_swap");
            var doSwapBB = sortFunc.AppendBasicBlock("do_swap");
            var skipSwapBB = sortFunc.AppendBasicBlock("skip_swap");
            builder.BuildCondBr(needSwap, doSwapBB, skipSwapBB);

            // Swap block
            builder.PositionAtEnd(doSwapBB);
            builder.BuildStore(dj, depthISlot);
            builder.BuildStore(di, depthJSlot);

            // Swap all component columns at index i and j
            foreach (var (compName, _) in _typeChecker.Components)
            {
                var compStruct = ecs.GetComponentStructType(compName);
                var colGlob = ecs.GetArchetypeColGlobal(compName);
                var col = builder.BuildLoad2(LLVMTypeRef.CreatePointer(compStruct, 0), colGlob, $"{compName}_col_swap");
                var slotI = builder.BuildInBoundsGEP2(compStruct, col, new[] { curSortI }, $"{compName}_i");
                var slotJ = builder.BuildInBoundsGEP2(compStruct, col, new[] { curSortJ }, $"{compName}_j");

                var valI = builder.BuildLoad2(compStruct, slotI, $"{compName}_val_i");
                var valJ = builder.BuildLoad2(compStruct, slotJ, $"{compName}_val_j");
                builder.BuildStore(valJ, slotI);
                builder.BuildStore(valI, slotJ);
            }
            builder.BuildBr(skipSwapBB);

            builder.PositionAtEnd(skipSwapBB);
            var nextJ = builder.BuildAdd(curSortJ, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_j");
            builder.BuildStore(nextJ, sortJAlloca);
            builder.BuildBr(innerCondBB);

            builder.PositionAtEnd(innerStepBB);
            var nextI = builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_i");
            builder.BuildStore(nextI, sortIAlloca);
            builder.BuildBr(outerCondBB);

            builder.PositionAtEnd(outerExitBB);
            builder.BuildCall2(freeType, freeFunc, new[] { depthsRaw }, "");
            builder.BuildBr(exitSortBB);

            builder.PositionAtEnd(exitSortBB);
            builder.BuildRetVoid();
        }
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

        var totalCount = builder.BuildLoad2(context.Int32Type, ecs.GetArchetypeCountGlobal(), "total_count");
        var hasEntities = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, totalCount, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "has_entities");

        var loopCondBB = sysFunc.AppendBasicBlock("loop_cond");
        var loopBodyBB = sysFunc.AppendBasicBlock("loop_body");
        var exitBB = sysFunc.AppendBasicBlock("exit");

        builder.BuildCondBr(hasEntities, loopCondBB, exitBB);

        // Loop header
        builder.PositionAtEnd(loopCondBB);
        var idxAlloca = builder.BuildAlloca(context.Int32Type, "i");
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);
        builder.BuildBr(loopBodyBB);

        // Loop body
        builder.PositionAtEnd(loopBodyBB);
        var curIdx = builder.BuildLoad2(context.Int32Type, idxAlloca, "cur_i");

        // Map query parameters to pointers inside the loop
        var locals = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
        var varTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var qp in sys.QueryParams)
        {
            varTypes[qp.Name] = qp.TypeName;
            if (_typeChecker.Components.ContainsKey(qp.TypeName))
            {
                var structType = ecs.GetComponentStructType(qp.TypeName);
                var colGlobal = ecs.GetArchetypeColGlobal(qp.TypeName);
                var colBase = builder.BuildLoad2(LLVMTypeRef.CreatePointer(structType, 0), colGlobal, $"{qp.Name}_col");
                var elemPtr = builder.BuildInBoundsGEP2(structType, colBase, new[] { curIdx }, $"{qp.Name}_ptr");
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

        // Increment loop index
        var nextIdx = builder.BuildAdd(curIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_i");
        builder.BuildStore(nextIdx, idxAlloca);

        var continueCond = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, nextIdx, totalCount, "loop_more");
        builder.BuildCondBr(continueCond, loopBodyBB, exitBB);

        // Exit block
        builder.PositionAtEnd(exitBB);
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
