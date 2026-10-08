using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckRaylibCall(CallExpression call, out TypeSymbol? result)
    {
        // Raylib Window & Drawing built-ins
        if (call.Callee is "init_window" or "rl_init_window" or "close_window" or "rl_close_window" or
            "set_target_fps" or "rl_set_target_fps" or "begin_drawing" or "rl_begin_drawing" or
            "end_drawing" or "rl_end_drawing" or "clear_background" or "rl_clear_background" or
            "draw_rectangle" or "rl_draw_rectangle" or "draw_circle" or "rl_draw_circle" or
            "draw_text" or "rl_draw_text" or "draw_line" or "rl_draw_line" or
            "draw_texture" or "rl_draw_texture" or "draw_texture_pro" or "rl_draw_texture_pro" or
            "unload_texture" or "rl_unload_texture" or
            "init_audio_device" or "rl_init_audio_device" or "close_audio_device" or "rl_close_audio_device" or
            "play_sound" or "rl_play_sound" or "stop_sound" or "rl_stop_sound" or
            "pause_sound" or "rl_pause_sound" or "resume_sound" or "rl_resume_sound" or
            "set_sound_volume" or "rl_set_sound_volume" or "unload_sound" or "rl_unload_sound" or
            "begin_mode_2d" or "rl_begin_mode_2d" or "end_mode_2d" or "rl_end_mode_2d" or
            "render_profiler" or "render_debug_overlay" or "ecs::render_profiler")
        {
            result = TypeSymbol.Void;
            return true;
        }

        // Raylib Input & Query built-ins
        if (call.Callee is "is_window_ready" or "rl_is_window_ready" or
            "window_should_close" or "rl_window_should_close" or
            "is_key_down" or "rl_is_key_down" or "is_key_pressed" or "rl_is_key_pressed" or
            "is_key_released" or "rl_is_key_released" or "is_key_up" or "rl_is_key_up" or
            "is_mouse_button_down" or "rl_is_mouse_button_down" or
            "is_mouse_button_pressed" or "rl_is_mouse_button_pressed" or
            "is_audio_device_ready" or "rl_is_audio_device_ready" or
            "is_sound_playing" or "rl_is_sound_playing")
        {
            result = TypeSymbol.Bool;
            return true;
        }

        if (call.Callee is "get_fps" or "rl_get_fps" or
            "get_mouse_x" or "rl_get_mouse_x" or
            "get_mouse_y" or "rl_get_mouse_y" or
            "get_texture_width" or "rl_get_texture_width" or
            "get_texture_height" or "rl_get_texture_height" or
            "rl_color" or "get_tick_count" or "time_ms")
        {
            result = TypeSymbol.I32;
            return true;
        }

        if (call.Callee is "load_texture" or "rl_load_texture" or
            "load_sound" or "rl_load_sound")
        {
            result = TypeSymbol.I64;
            return true;
        }

        if (call.Callee is "get_frame_time" or "rl_get_frame_time")
        {
            result = TypeSymbol.F32;
            return true;
        }

        if (call.Callee is "get_time" or "rl_get_time")
        {
            result = TypeSymbol.F64;
            return true;
        }

        result = null;
        return false;
    }
}
