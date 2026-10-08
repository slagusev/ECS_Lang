using System;
using System.Collections.Generic;
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

    private unsafe bool TryGenerateRaylibCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        CallExpression call,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        out LLVMValueRef result)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        if (call.Callee is "init_window" or "rl_init_window")
        {
            var f = module.GetNamedFunction("InitWindow");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, i8PtrType }, false);
            var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var h = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var title = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { w, h, title }, "");
            return true;
        }

        if (call.Callee is "is_window_ready" or "rl_is_window_ready")
        {
            var f = module.GetNamedFunction("IsWindowReady");
            var ft = LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
            var raw = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "win_ready_raw");
            result = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, raw, LLVMValueRef.CreateConstInt(context.Int8Type, 0), "win_ready");
            return true;
        }

        if (call.Callee is "window_should_close" or "rl_window_should_close")
        {
            var f = module.GetNamedFunction("WindowShouldClose");
            var ft = LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
            var raw = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "should_close_raw");
            result = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, raw, LLVMValueRef.CreateConstInt(context.Int8Type, 0), "should_close");
            return true;
        }

        if (call.Callee is "render_profiler" or "render_debug_overlay" or "ecs::render_profiler")
        {
            var f = module.GetNamedFunction("world_render_profiler");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0) }, false);
            var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { w }, "");
            return true;
        }

        if (call.Callee is "close_window" or "rl_close_window")
        {
            var f = module.GetNamedFunction("CloseWindow");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            return true;
        }

        if (call.Callee is "set_target_fps" or "rl_set_target_fps")
        {
            var f = module.GetNamedFunction("SetTargetFPS");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
            var fps = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { fps }, "");
            return true;
        }

        if (call.Callee is "get_fps" or "rl_get_fps")
        {
            var f = module.GetNamedFunction("GetFPS");
            var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "fps");
            return true;
        }

        if (call.Callee is "get_frame_time" or "rl_get_frame_time")
        {
            var f = module.GetNamedFunction("GetFrameTime");
            var ft = LLVMTypeRef.CreateFunction(context.FloatType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "frame_time");
            return true;
        }

        if (call.Callee is "get_time" or "rl_get_time")
        {
            var f = module.GetNamedFunction("GetTime");
            var ft = LLVMTypeRef.CreateFunction(context.DoubleType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "cur_time");
            return true;
        }

        if (call.Callee is "begin_drawing" or "rl_begin_drawing")
        {
            var f = module.GetNamedFunction("BeginDrawing");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            });
            return true;
        }

        if (call.Callee is "end_drawing" or "rl_end_drawing")
        {
            var f = module.GetNamedFunction("EndDrawing");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            });
            return true;
        }

        if (call.Callee is "clear_background" or "rl_clear_background")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { colorVal }, "");
            });
            return true;
        }

        if (call.Callee is "draw_rectangle" or "rl_draw_rectangle")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { x, y, w, h, colorVal }, "");
            });
            return true;
        }

        if (call.Callee is "draw_circle" or "rl_draw_circle")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { cx, cy, rad, colorVal }, "");
            });
            return true;
        }

        if (call.Callee is "draw_text" or "rl_draw_text")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { txt, x, y, sz, colorVal }, "");
            });
            return true;
        }

        if (call.Callee is "draw_line" or "rl_draw_line")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { x1, y1, x2, y2, colorVal }, "");
            });
            return true;
        }

        if (call.Callee is "is_key_down" or "rl_is_key_down")
        {
            var f = module.GetNamedFunction("IsKeyDown");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { k }, "key_down");
            return true;
        }

        if (call.Callee is "is_key_pressed" or "rl_is_key_pressed")
        {
            var f = module.GetNamedFunction("IsKeyPressed");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { k }, "key_pressed");
            return true;
        }

        if (call.Callee is "is_key_released" or "rl_is_key_released")
        {
            var f = module.GetNamedFunction("IsKeyReleased");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { k }, "key_rel");
            return true;
        }

        if (call.Callee is "is_key_up" or "rl_is_key_up")
        {
            var f = module.GetNamedFunction("IsKeyUp");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { k }, "key_up");
            return true;
        }

        if (call.Callee is "get_mouse_x" or "rl_get_mouse_x")
        {
            var f = module.GetNamedFunction("GetMouseX");
            var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_x");
            return true;
        }

        if (call.Callee is "get_mouse_y" or "rl_get_mouse_y")
        {
            var f = module.GetNamedFunction("GetMouseY");
            var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_y");
            return true;
        }

        if (call.Callee is "is_mouse_button_down" or "rl_is_mouse_button_down")
        {
            var f = module.GetNamedFunction("IsMouseButtonDown");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { btn }, "btn_down");
            return true;
        }

        if (call.Callee is "is_mouse_button_pressed" or "rl_is_mouse_button_pressed")
        {
            var f = module.GetNamedFunction("IsMouseButtonPressed");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
            var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            result = builder.BuildCall2(ft, f, new[] { btn }, "btn_pressed");
            return true;
        }

        if (call.Callee == "rl_color")
        {
            var r = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var g = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var b = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var a = call.Arguments.Count > 3 ? CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc) : null;
            result = PackRgbaColor(context, builder, r, g, b, a);
            return true;
        }

        if (call.Callee is "load_texture" or "rl_load_texture")
        {
            var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var mallocFunc = module.GetNamedFunction("malloc");
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            var texBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "tex_buf");

            var loadTexFunc = module.GetNamedFunction("LoadTexture");
            var loadTexType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
            builder.BuildCall2(loadTexType, loadTexFunc, new[] { texBuf, pathVal }, "");

            result = builder.BuildPtrToInt(texBuf, context.Int64Type, "tex_handle");
            return true;
        }

        if (call.Callee is "get_texture_width" or "rl_get_texture_width")
        {
            var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
            var wPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "tex_w_ptr");
            result = builder.BuildLoad2(context.Int32Type, wPtr, "tex_w");
            return true;
        }

        if (call.Callee is "get_texture_height" or "rl_get_texture_height")
        {
            var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
            var hPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 2) }, "tex_h_ptr");
            result = builder.BuildLoad2(context.Int32Type, hPtr, "tex_h");
            return true;
        }

        if (call.Callee is "draw_texture" or "rl_draw_texture")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { texPtr, posX, posY, tintVal }, "");
            });
            return true;
        }

        if (call.Callee is "draw_texture_pro" or "rl_draw_texture_pro")
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
            result = EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                builder.BuildCall2(ft, f, new[] { texPtr, srcPtr, dstPtr, originI64, rot, tintVal }, "");
            });
            return true;
        }

        if (call.Callee is "unload_texture" or "rl_unload_texture")
        {
            var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
            var f = module.GetNamedFunction("UnloadTexture");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            builder.BuildCall2(ft, f, new[] { texPtr }, "");
            var freeFunc = module.GetNamedFunction("free");
            var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(freeType, freeFunc, new[] { texPtr }, "");
            return true;
        }

        if (call.Callee is "init_audio_device" or "rl_init_audio_device")
        {
            var f = module.GetNamedFunction("InitAudioDevice");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            return true;
        }

        if (call.Callee is "close_audio_device" or "rl_close_audio_device")
        {
            var f = module.GetNamedFunction("CloseAudioDevice");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            return true;
        }

        if (call.Callee is "is_audio_device_ready" or "rl_is_audio_device_ready")
        {
            var f = module.GetNamedFunction("IsAudioDeviceReady");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "audio_ready");
            return true;
        }

        if (call.Callee is "load_sound" or "rl_load_sound")
        {
            var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var mallocFunc = module.GetNamedFunction("malloc");
            var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
            var sndBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 64) }, "snd_buf");

            var loadSndFunc = module.GetNamedFunction("LoadSound");
            var loadSndType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
            builder.BuildCall2(loadSndType, loadSndFunc, new[] { sndBuf, pathVal }, "");

            result = builder.BuildPtrToInt(sndBuf, context.Int64Type, "snd_handle");
            return true;
        }

        if (call.Callee is "play_sound" or "rl_play_sound")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("PlaySound");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr }, "");
            return true;
        }

        if (call.Callee is "stop_sound" or "rl_stop_sound")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("StopSound");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr }, "");
            return true;
        }

        if (call.Callee is "pause_sound" or "rl_pause_sound")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("PauseSound");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr }, "");
            return true;
        }

        if (call.Callee is "resume_sound" or "rl_resume_sound")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("ResumeSound");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr }, "");
            return true;
        }

        if (call.Callee is "is_sound_playing" or "rl_is_sound_playing")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("IsSoundPlaying");
            var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr }, "snd_playing");
            return true;
        }

        if (call.Callee is "set_sound_volume" or "rl_set_sound_volume")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var vol = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
            var f = module.GetNamedFunction("SetSoundVolume");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.FloatType }, false);
            result = builder.BuildCall2(ft, f, new[] { sndPtr, vol }, "");
            return true;
        }

        if (call.Callee is "unload_sound" or "rl_unload_sound")
        {
            var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
            var f = module.GetNamedFunction("UnloadSound");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            builder.BuildCall2(ft, f, new[] { sndPtr }, "");
            var freeFunc = module.GetNamedFunction("free");
            var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
            result = builder.BuildCall2(freeType, freeFunc, new[] { sndPtr }, "");
            return true;
        }

        if (call.Callee is "begin_mode_2d" or "rl_begin_mode_2d")
        {
            var camType = LLVMTypeRef.CreateArray(context.FloatType, 6);
            var camAlloca = CreateEntryBlockAlloca(context, function, camType, "cam_alloca");
            var camZeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

            var ox = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
            var oy = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
            var tx = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
            var ty = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
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
            result = builder.BuildCall2(ft, f, new[] { camPtr }, "");
            return true;
        }

        if (call.Callee is "end_mode_2d" or "rl_end_mode_2d")
        {
            var f = module.GetNamedFunction("EndMode2D");
            var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
            result = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
            return true;
        }

        result = default;
        return false;
    }
}
