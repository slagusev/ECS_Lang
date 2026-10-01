using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class EcsRuntimeEmitter
{
    private static int MakeColor(int r, int g, int b, int a) => r | (g << 8) | (b << 16) | (a << 24);

    public unsafe void EmitProfilerRuntime(LLVMTargetDataRef dataLayout)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var worldPtrType = LLVMTypeRef.CreatePointer(_worldStructType, 0);

        // Global flag for overlay visibility
        var profilerVisibleVar = _module.AddGlobal(_context.Int32Type, "g_ecs_profiler_visible");
        profilerVisibleVar.Initializer = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        profilerVisibleVar.Linkage = LLVMLinkage.LLVMInternalLinkage;

        var profilerFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
        var profilerFunc = _module.AddFunction("world_render_profiler", profilerFuncType);
        var worldArg = profilerFunc.GetParam(0);
        worldArg.Name = "world";

        var entryBB = profilerFunc.AppendBasicBlock("entry");
        var checkKeyBB = profilerFunc.AppendBasicBlock("check_key");
        var renderCheckBB = profilerFunc.AppendBasicBlock("render_check");
        var doRenderBB = profilerFunc.AppendBasicBlock("do_render");
        var exitBB = profilerFunc.AppendBasicBlock("exit");

        _builder.PositionAtEnd(entryBB);

        var isWinReadyFunc = _module.GetNamedFunction("IsWindowReady");
        var isWinReadyType = LLVMTypeRef.CreateFunction(_context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
        var winReadyRaw = _builder.BuildCall2(isWinReadyType, isWinReadyFunc, Array.Empty<LLVMValueRef>(), "win_ready_raw");
        var winReady = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, winReadyRaw, LLVMValueRef.CreateConstInt(_context.Int8Type, 0), "win_ready");
        _builder.BuildCondBr(winReady, checkKeyBB, exitBB);

        // check_key: F1 is keycode 290
        _builder.PositionAtEnd(checkKeyBB);
        var isKeyPressedFunc = _module.GetNamedFunction("IsKeyPressed");
        var isKeyPressedType = LLVMTypeRef.CreateFunction(_context.Int8Type, new[] { _context.Int32Type }, false);
        var f1PressedRaw = _builder.BuildCall2(isKeyPressedType, isKeyPressedFunc, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 290) }, "f1_pressed_raw");
        var f1Pressed = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, f1PressedRaw, LLVMValueRef.CreateConstInt(_context.Int8Type, 0), "f1_pressed");

        var toggleBB = profilerFunc.AppendBasicBlock("toggle_vis");
        _builder.BuildCondBr(f1Pressed, toggleBB, renderCheckBB);

        _builder.PositionAtEnd(toggleBB);
        var curVis = _builder.BuildLoad2(_context.Int32Type, profilerVisibleVar, "cur_vis");
        var toggledVis = _builder.BuildSub(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), curVis, "toggled_vis");
        _builder.BuildStore(toggledVis, profilerVisibleVar);
        _builder.BuildBr(renderCheckBB);

        _builder.PositionAtEnd(renderCheckBB);
        var isVis = _builder.BuildLoad2(_context.Int32Type, profilerVisibleVar, "is_vis");
        var shouldRender = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, isVis, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "should_render");
        _builder.BuildCondBr(shouldRender, doRenderBB, exitBB);

        // do_render:
        _builder.PositionAtEnd(doRenderBB);
        var drawRectFunc = _module.GetNamedFunction("DrawRectangle");
        var drawRectType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { _context.Int32Type, _context.Int32Type, _context.Int32Type, _context.Int32Type, _context.Int32Type }, false);
        var drawRectLinesFunc = _module.GetNamedFunction("DrawRectangleLines");
        var drawTextFunc = _module.GetNamedFunction("DrawText");
        var drawTextType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType, _context.Int32Type, _context.Int32Type, _context.Int32Type, _context.Int32Type }, false);
        var sprintfFunc = _module.GetNamedFunction("sprintf");
        var sprintfType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { i8PtrType, i8PtrType }, true);
        var getFpsFunc = _module.GetNamedFunction("GetFPS");
        var getFpsType = LLVMTypeRef.CreateFunction(_context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
        var getFtFunc = _module.GetNamedFunction("GetFrameTime");
        var getFtType = LLVMTypeRef.CreateFunction(_context.FloatType, Array.Empty<LLVMTypeRef>(), false);

        int bgColor = MakeColor(15, 20, 30, 235);
        int borderColor = MakeColor(50, 140, 240, 255);
        int titleColor = MakeColor(120, 210, 255, 255);
        int textColor = MakeColor(220, 225, 235, 255);
        int fpsColor = MakeColor(80, 220, 120, 255);
        int hintColor = MakeColor(140, 150, 170, 255);

        // Draw HUD background panel
        _builder.BuildCall2(drawRectType, drawRectFunc, new[]
        {
            LLVMValueRef.CreateConstInt(_context.Int32Type, 10),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 10),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 500),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 320),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)bgColor)
        }, "");

        _builder.BuildCall2(drawRectType, drawRectLinesFunc, new[]
        {
            LLVMValueRef.CreateConstInt(_context.Int32Type, 10),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 10),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 500),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 320),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)borderColor)
        }, "");

        // Title
        var titleStr = _builder.BuildGlobalStringPtr("[ ECS ARCHETYPE PROFILER & INSPECTOR (F1) ]", "p_title");
        _builder.BuildCall2(drawTextType, drawTextFunc, new[]
        {
            titleStr,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 22),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 20),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 18),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)titleColor)
        }, "");

        // Buffer for formatted strings
        var bufAlloca = _builder.BuildAlloca(LLVMTypeRef.CreateArray(_context.Int8Type, 256), "str_buf");
        var bufPtr = _builder.BuildBitCast(bufAlloca, i8PtrType, "buf_ptr");

        // FPS and Frame Time
        var fpsVal = _builder.BuildCall2(getFpsType, getFpsFunc, Array.Empty<LLVMValueRef>(), "cur_fps");
        var ftSecVal = _builder.BuildCall2(getFtType, getFtFunc, Array.Empty<LLVMValueRef>(), "cur_ft_sec");
        var ftMsVal = _builder.BuildFMul(ftSecVal, LLVMValueRef.CreateConstReal(_context.FloatType, 1000.0), "ft_ms");
        var ftMsDouble = _builder.BuildFPExt(ftMsVal, _context.DoubleType, "ft_ms_d");

        var fpsFmt = _builder.BuildGlobalStringPtr("Performance: %d FPS (%.2f ms / frame)", "fps_fmt");
        _builder.BuildCall2(sprintfType, sprintfFunc, new[] { bufPtr, fpsFmt, fpsVal, ftMsDouble }, "");
        _builder.BuildCall2(drawTextType, drawTextFunc, new[]
        {
            bufPtr,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 24),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 48),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 16),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)fpsColor)
        }, "");

        // World Entity and Archetype stats
        var archCountSlot = _builder.BuildStructGEP2(_worldStructType, worldArg, 0, "p_arch_count_slot");
        var archTablesSlot = _builder.BuildStructGEP2(_worldStructType, worldArg, 2, "p_tables_slot");
        var entCountSlot = _builder.BuildStructGEP2(_worldStructType, worldArg, 3, "p_ent_count_slot");

        var archCountVal = _builder.BuildLoad2(_context.Int32Type, archCountSlot, "p_arch_count");
        var entCountVal = _builder.BuildLoad2(_context.Int32Type, entCountSlot, "p_ent_count");

        var statsFmt = _builder.BuildGlobalStringPtr("World Stats: %d live entities in %d archetypes", "stats_fmt");
        _builder.BuildCall2(sprintfType, sprintfFunc, new[] { bufPtr, statsFmt, entCountVal, archCountVal }, "");
        _builder.BuildCall2(drawTextType, drawTextFunc, new[]
        {
            bufPtr,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 24),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 72),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 16),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)textColor)
        }, "");

        // Loop over archetypes (up to 7)
        var archIdxAlloca = _builder.BuildAlloca(_context.Int32Type, "p_a_i");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), archIdxAlloca);

        var aLoopCondBB = profilerFunc.AppendBasicBlock("p_a_cond");
        var aLoopBodyBB = profilerFunc.AppendBasicBlock("p_a_body");
        var aLoopExitBB = profilerFunc.AppendBasicBlock("p_a_exit");

        _builder.BuildBr(aLoopCondBB);

        // aLoopCondBB:
        _builder.PositionAtEnd(aLoopCondBB);
        var curA = _builder.BuildLoad2(_context.Int32Type, archIdxAlloca, "cur_a");
        var aMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curA, archCountVal, "a_more");
        var aLimit = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curA, LLVMValueRef.CreateConstInt(_context.Int32Type, 7), "a_limit");
        var aCond = _builder.BuildAnd(aMore, aLimit, "a_cond");
        _builder.BuildCondBr(aCond, aLoopBodyBB, aLoopExitBB);

        // aLoopBodyBB:
        _builder.PositionAtEnd(aLoopBodyBB);
        var tablesPtr = _builder.BuildLoad2(LLVMTypeRef.CreatePointer(_archStructType, 0), archTablesSlot, "p_tables");
        var archPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesPtr, new[] { curA }, "p_arch");

        var aMaskSlot = _builder.BuildStructGEP2(_archStructType, archPtr, 0, "p_a_mask_slot");
        var aCountSlot = _builder.BuildStructGEP2(_archStructType, archPtr, 1, "p_a_count_slot");
        var aCapSlot = _builder.BuildStructGEP2(_archStructType, archPtr, 2, "p_a_cap_slot");

        var aMask = _builder.BuildLoad2(_context.Int64Type, aMaskSlot, "p_a_mask");
        var aCount = _builder.BuildLoad2(_context.Int32Type, aCountSlot, "p_a_count");
        var aCap = _builder.BuildLoad2(_context.Int32Type, aCapSlot, "p_a_cap");

        // Y coordinate: 104 + curA * 24
        var yOffset = _builder.BuildAdd(LLVMValueRef.CreateConstInt(_context.Int32Type, 104),
            _builder.BuildMul(curA, LLVMValueRef.CreateConstInt(_context.Int32Type, 24), "a_y_mul"), "a_y");

        var archFmt = _builder.BuildGlobalStringPtr("  Archetype #%d: count=%d, cap=%d, mask=0x%llX", "arch_fmt");
        _builder.BuildCall2(sprintfType, sprintfFunc, new[] { bufPtr, archFmt, curA, aCount, aCap, aMask }, "");
        _builder.BuildCall2(drawTextType, drawTextFunc, new[]
        {
            bufPtr,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 24),
            yOffset,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 15),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)textColor)
        }, "");

        var nextA = _builder.BuildAdd(curA, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_a");
        _builder.BuildStore(nextA, archIdxAlloca);
        _builder.BuildBr(aLoopCondBB);

        // aLoopExitBB:
        _builder.PositionAtEnd(aLoopExitBB);
        var hintStr = _builder.BuildGlobalStringPtr("[F1] Toggle ECS Inspector HUD", "p_hint");
        _builder.BuildCall2(drawTextType, drawTextFunc, new[]
        {
            hintStr,
            LLVMValueRef.CreateConstInt(_context.Int32Type, 24),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 290),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 14),
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)hintColor)
        }, "");

        _builder.BuildBr(exitBB);

        // exitBB:
        _builder.PositionAtEnd(exitBB);
        _builder.BuildRetVoid();
    }

}
