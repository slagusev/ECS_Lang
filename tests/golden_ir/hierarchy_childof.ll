; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.EcsWorld = type { i32, i32, ptr, i32, i32, ptr, ptr, i32, i32, ptr, ptr, ptr, ptr, i32, i32, ptr, ptr }
%struct.Archetype = type { [1 x i64], i32, i32, ptr, [3 x ptr] }
%struct.ChildOf = type { i32 }
%struct.NameTag = type { i32 }
%struct.Position = type { float, float }

@NvOptimusEnablement = dllexport global i32 1
@AmdPowerXpressRequestHighPerformance = dllexport global i32 1
@ecs_err_oob_world_set_ChildOf = private unnamed_addr constant [77 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_set_ChildOf.\00", align 1
@ecs_err_dead_world_set_ChildOf = private unnamed_addr constant [81 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_set_ChildOf.\00", align 1
@ecs_err_pending_world_set_ChildOf = private unnamed_addr constant [85 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_set_ChildOf.\00", align 1
@ecs_err_oob_world_add_ChildOf = private unnamed_addr constant [77 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_add_ChildOf.\00", align 1
@ecs_err_dead_world_add_ChildOf = private unnamed_addr constant [81 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_add_ChildOf.\00", align 1
@ecs_err_pending_world_add_ChildOf = private unnamed_addr constant [85 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_add_ChildOf.\00", align 1
@ecs_err_oob_rem_ChildOf = private unnamed_addr constant [80 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_remove_ChildOf.\00", align 1
@ecs_err_dead_rem_ChildOf = private unnamed_addr constant [84 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_remove_ChildOf.\00", align 1
@ecs_err_pending_rem_ChildOf = private unnamed_addr constant [88 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_remove_ChildOf.\00", align 1
@ecs_err_oob_has_ChildOf = private unnamed_addr constant [76 x i8] c"[ECS Error] Attempted to query out of bounds entity with world_has_ChildOf.\00", align 1
@ecs_err_oob_world_set_NameTag = private unnamed_addr constant [77 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_set_NameTag.\00", align 1
@ecs_err_dead_world_set_NameTag = private unnamed_addr constant [81 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_set_NameTag.\00", align 1
@ecs_err_pending_world_set_NameTag = private unnamed_addr constant [85 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_set_NameTag.\00", align 1
@ecs_err_oob_world_add_NameTag = private unnamed_addr constant [77 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_add_NameTag.\00", align 1
@ecs_err_dead_world_add_NameTag = private unnamed_addr constant [81 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_add_NameTag.\00", align 1
@ecs_err_pending_world_add_NameTag = private unnamed_addr constant [85 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_add_NameTag.\00", align 1
@ecs_err_oob_rem_NameTag = private unnamed_addr constant [80 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_remove_NameTag.\00", align 1
@ecs_err_dead_rem_NameTag = private unnamed_addr constant [84 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_remove_NameTag.\00", align 1
@ecs_err_pending_rem_NameTag = private unnamed_addr constant [88 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_remove_NameTag.\00", align 1
@ecs_err_oob_has_NameTag = private unnamed_addr constant [76 x i8] c"[ECS Error] Attempted to query out of bounds entity with world_has_NameTag.\00", align 1
@ecs_err_oob_world_set_Position = private unnamed_addr constant [78 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_set_Position.\00", align 1
@ecs_err_dead_world_set_Position = private unnamed_addr constant [82 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_set_Position.\00", align 1
@ecs_err_pending_world_set_Position = private unnamed_addr constant [86 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_set_Position.\00", align 1
@ecs_err_oob_world_add_Position = private unnamed_addr constant [78 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_add_Position.\00", align 1
@ecs_err_dead_world_add_Position = private unnamed_addr constant [82 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_add_Position.\00", align 1
@ecs_err_pending_world_add_Position = private unnamed_addr constant [86 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_add_Position.\00", align 1
@ecs_err_oob_rem_Position = private unnamed_addr constant [81 x i8] c"[ECS Error] Attempted to mutate out of bounds entity with world_remove_Position.\00", align 1
@ecs_err_dead_rem_Position = private unnamed_addr constant [85 x i8] c"[ECS Error] Attempted to mutate despawned or dead entity with world_remove_Position.\00", align 1
@ecs_err_pending_rem_Position = private unnamed_addr constant [89 x i8] c"[ECS Error] Attempted to mutate unassigned or pending entity with world_remove_Position.\00", align 1
@ecs_err_oob_has_Position = private unnamed_addr constant [77 x i8] c"[ECS Error] Attempted to query out of bounds entity with world_has_Position.\00", align 1
@g_ecs_profiler_visible = internal global i32 0
@p_title = private unnamed_addr constant [44 x i8] c"[ ECS ARCHETYPE PROFILER & INSPECTOR (F1) ]\00", align 1
@fps_fmt = private unnamed_addr constant [38 x i8] c"Performance: %d FPS (%.2f ms / frame)\00", align 1
@stats_fmt = private unnamed_addr constant [47 x i8] c"World Stats: %d live entities in %d archetypes\00", align 1
@arch_fmt = private unnamed_addr constant [47 x i8] c"  Archetype #%d: count=%d, cap=%d, mask=0x%llX\00", align 1
@p_hint = private unnamed_addr constant [30 x i8] c"[F1] Toggle ECS Inspector HUD\00", align 1
@net_empty = private unnamed_addr constant [1 x i8] zeroinitializer, align 1
@net_udp_empty = private unnamed_addr constant [1 x i8] zeroinitializer, align 1
@net_extract_empty = private unnamed_addr constant [1 x i8] zeroinitializer, align 1
@str_lit = private unnamed_addr constant [41 x i8] c"Hierarchy sorting executed successfully.\00", align 1
@prompt_exit = private unnamed_addr constant [25 x i8] c"Press any key to exit...\00", align 1

declare i32 @puts(ptr)

declare i32 @printf(ptr, ...)

declare ptr @realloc(ptr, i64)

declare i32 @getchar()

declare i32 @_getch()

declare ptr @memcpy(ptr, ptr, i64)

declare ptr @memset(ptr, i32, i64)

declare ptr @malloc(i64)

declare void @free(ptr)

declare i32 @sprintf(ptr, ptr, ...)

declare i64 @strlen(ptr)

declare i32 @strcmp(ptr, ptr)

declare float @sqrtf(float)

declare float @sinf(float)

declare float @cosf(float)

declare float @floorf(float)

declare float @ceilf(float)

declare i32 @rand()

declare void @InitWindow(i32, i32, ptr)

declare i8 @IsWindowReady()

declare i8 @WindowShouldClose()

declare void @CloseWindow()

declare void @SetTargetFPS(i32)

declare i32 @GetFPS()

declare float @GetFrameTime()

declare double @GetTime()

declare void @BeginDrawing()

declare void @EndDrawing()

declare void @ClearBackground(i32)

declare void @DrawText(ptr, i32, i32, i32, i32)

declare void @DrawRectangle(i32, i32, i32, i32, i32)

declare void @DrawRectangleLines(i32, i32, i32, i32, i32)

declare void @DrawCircle(i32, i32, float, i32)

declare void @DrawLine(i32, i32, i32, i32, i32)

declare i8 @IsKeyDown(i32)

declare i8 @IsKeyPressed(i32)

declare i8 @IsKeyReleased(i32)

declare i8 @IsKeyUp(i32)

declare i32 @GetMouseX()

declare i32 @GetMouseY()

declare i8 @IsMouseButtonDown(i32)

declare i8 @IsMouseButtonPressed(i32)

declare void @LoadTexture(ptr, ptr)

declare void @DrawTexture(ptr, i32, i32, i32)

declare void @DrawTexturePro(ptr, ptr, ptr, i64, float, i32)

declare void @UnloadTexture(ptr)

declare void @InitAudioDevice()

declare void @CloseAudioDevice()

declare i1 @IsAudioDeviceReady()

declare void @LoadSound(ptr, ptr)

declare void @PlaySound(ptr)

declare void @StopSound(ptr)

declare void @PauseSound(ptr)

declare void @ResumeSound(ptr)

declare i1 @IsSoundPlaying(ptr)

declare void @SetSoundVolume(ptr, float)

declare void @UnloadSound(ptr)

declare void @BeginMode2D(ptr)

declare void @EndMode2D()

declare ptr @CreateThreadpoolWork(ptr, ptr, ptr)

declare void @SubmitThreadpoolWork(ptr)

declare void @WaitForThreadpoolWorkCallbacks(ptr, i32)

declare void @CloseThreadpoolWork(ptr)

declare i32 @GetTickCount()

define i32 @world_get_or_create_archetype(ptr %0, ptr %1) {
entry:
  %arch_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 0
  %arch_cap_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 1
  %arch_tables_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %cur_count = load i32, ptr %arch_count_slot, align 4
  %i = alloca i32, align 4
  store i32 0, ptr %i, align 4
  br label %search_cond

search_cond:                                      ; preds = %search_next, %entry
  %cur_i = load i32, ptr %i, align 4
  %has_more = icmp slt i32 %cur_i, %cur_count
  br i1 %has_more, label %search_body, label %not_found

search_body:                                      ; preds = %search_cond
  %tables_base = load ptr, ptr %arch_tables_slot, align 8
  %arch_elem = getelementptr inbounds %struct.Archetype, ptr %tables_base, i32 %cur_i
  %mask_gep = getelementptr inbounds nuw %struct.Archetype, ptr %arch_elem, i32 0, i32 0
  %ex_w0 = getelementptr inbounds [1 x i64], ptr %mask_gep, i32 0, i32 0
  %existing_mask = load i64, ptr %ex_w0, align 8
  %tgt_w0 = getelementptr inbounds [1 x i64], ptr %1, i32 0, i32 0
  %target_mask = load i64, ptr %tgt_w0, align 8
  %is_match = icmp eq i64 %existing_mask, %target_mask
  br i1 %is_match, label %return_found, label %search_next

not_found:                                        ; preds = %search_cond
  %cur_cap = load i32, ptr %arch_cap_slot, align 4
  %need_grow = icmp sge i32 %cur_count, %cur_cap
  br i1 %need_grow, label %grow_tables, label %init_arch

return_found:                                     ; preds = %search_body
  ret i32 %cur_i

search_next:                                      ; preds = %search_body
  %next_i = add i32 %cur_i, 1
  store i32 %next_i, ptr %i, align 4
  br label %search_cond

grow_tables:                                      ; preds = %not_found
  %cap_zero = icmp eq i32 %cur_cap, 0
  %double_cap = mul i32 %cur_cap, 2
  %new_cap = select i1 %cap_zero, i32 8, i32 %double_cap
  store i32 %new_cap, ptr %arch_cap_slot, align 4
  %new_cap64 = zext i32 %new_cap to i64
  %alloc_bytes = mul i64 %new_cap64, 48
  %cur_tables_raw = load ptr, ptr %arch_tables_slot, align 8
  %new_tables_i8 = call ptr @realloc(ptr %cur_tables_raw, i64 %alloc_bytes)
  store ptr %new_tables_i8, ptr %arch_tables_slot, align 8
  br label %init_arch

init_arch:                                        ; preds = %grow_tables, %not_found
  %next_count = add i32 %cur_count, 1
  store i32 %next_count, ptr %arch_count_slot, align 4
  %latest_tables = load ptr, ptr %arch_tables_slot, align 8
  %new_arch_elem = getelementptr inbounds %struct.Archetype, ptr %latest_tables, i32 %cur_count
  %m_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 0
  %src_w0 = getelementptr inbounds [1 x i64], ptr %1, i32 0, i32 0
  %src_val0 = load i64, ptr %src_w0, align 8
  %dst_w0 = getelementptr inbounds [1 x i64], ptr %m_gep, i32 0, i32 0
  store i64 %src_val0, ptr %dst_w0, align 8
  %cnt_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 1
  store i32 0, ptr %cnt_gep, align 4
  %cap_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 2
  store i32 0, ptr %cap_gep, align 4
  %ent_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 3
  store ptr null, ptr %ent_gep, align 8
  %cols_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 4
  %col_slot_0 = getelementptr inbounds [3 x ptr], ptr %cols_gep, i32 0, i32 0
  store ptr null, ptr %col_slot_0, align 8
  %col_slot_1 = getelementptr inbounds [3 x ptr], ptr %cols_gep, i32 0, i32 1
  store ptr null, ptr %col_slot_1, align 8
  %col_slot_2 = getelementptr inbounds [3 x ptr], ptr %cols_gep, i32 0, i32 2
  store ptr null, ptr %col_slot_2, align 8
  ret i32 %cur_count
}

define void @world_grow_archetype(ptr %0, i32 %1) {
entry:
  %tables_slot_gr = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %tables_base = load ptr, ptr %tables_slot_gr, align 8
  %arch_elem = getelementptr inbounds %struct.Archetype, ptr %tables_base, i32 %1
  %cap_slot = getelementptr inbounds nuw %struct.Archetype, ptr %arch_elem, i32 0, i32 2
  %cur_cap = load i32, ptr %cap_slot, align 4
  %is_zero = icmp eq i32 %cur_cap, 0
  %double_cap = mul i32 %cur_cap, 2
  %new_arch_cap = select i1 %is_zero, i32 32, i32 %double_cap
  store i32 %new_arch_cap, ptr %cap_slot, align 4
  %new_cap64 = zext i32 %new_arch_cap to i64
  %ent_slot = getelementptr inbounds nuw %struct.Archetype, ptr %arch_elem, i32 0, i32 3
  %cur_ent_raw = load ptr, ptr %ent_slot, align 8
  %ent_bytes = mul i64 %new_cap64, 4
  %new_ent_raw = call ptr @realloc(ptr %cur_ent_raw, i64 %ent_bytes)
  store ptr %new_ent_raw, ptr %ent_slot, align 8
  %mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %arch_elem, i32 0, i32 0
  %arch_mask = load i64, ptr %mask_slot, align 8
  %cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %arch_elem, i32 0, i32 4
  %has_ChildOf = and i64 %arch_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %grow_col_ChildOf, label %skip_col_ChildOf

grow_col_ChildOf:                                 ; preds = %entry
  %col_slot_ChildOf = getelementptr inbounds [3 x ptr], ptr %cols_arr, i32 0, i32 0
  %cur_col_ChildOf = load ptr, ptr %col_slot_ChildOf, align 8
  %col_bytes_ChildOf = mul i64 %new_cap64, 4
  %new_col_ChildOf = call ptr @realloc(ptr %cur_col_ChildOf, i64 %col_bytes_ChildOf)
  store ptr %new_col_ChildOf, ptr %col_slot_ChildOf, align 8
  br label %skip_col_ChildOf

skip_col_ChildOf:                                 ; preds = %grow_col_ChildOf, %entry
  %has_NameTag = and i64 %arch_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %grow_col_NameTag, label %skip_col_NameTag

grow_col_NameTag:                                 ; preds = %skip_col_ChildOf
  %col_slot_NameTag = getelementptr inbounds [3 x ptr], ptr %cols_arr, i32 0, i32 1
  %cur_col_NameTag = load ptr, ptr %col_slot_NameTag, align 8
  %col_bytes_NameTag = mul i64 %new_cap64, 4
  %new_col_NameTag = call ptr @realloc(ptr %cur_col_NameTag, i64 %col_bytes_NameTag)
  store ptr %new_col_NameTag, ptr %col_slot_NameTag, align 8
  br label %skip_col_NameTag

skip_col_NameTag:                                 ; preds = %grow_col_NameTag, %skip_col_ChildOf
  %has_Position = and i64 %arch_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %grow_col_Position, label %skip_col_Position

grow_col_Position:                                ; preds = %skip_col_NameTag
  %col_slot_Position = getelementptr inbounds [3 x ptr], ptr %cols_arr, i32 0, i32 2
  %cur_col_Position = load ptr, ptr %col_slot_Position, align 8
  %col_bytes_Position = mul i64 %new_cap64, 8
  %new_col_Position = call ptr @realloc(ptr %cur_col_Position, i64 %col_bytes_Position)
  store ptr %new_col_Position, ptr %col_slot_Position, align 8
  br label %skip_col_Position

skip_col_Position:                                ; preds = %grow_col_Position, %skip_col_NameTag
  ret void
}

define ptr @ecs_create_world() {
entry:
  %raw_world = call ptr @malloc(i64 104)
  %0 = call ptr @memset(ptr %raw_world, i32 0, i64 104)
  %a0_mask = alloca [1 x i64], align 8
  store [1 x i64] zeroinitializer, ptr %a0_mask, align 8
  %a0_init = call i32 @world_get_or_create_archetype(ptr %raw_world, ptr %a0_mask)
  ret ptr %raw_world
}

declare void @AcquireSRWLockExclusive(ptr)

declare void @ReleaseSRWLockExclusive(ptr)

define internal void @rt_panic(ptr %0) {
entry:
  %1 = call i32 @puts(ptr %0)
  call void @exit(i32 1)
  unreachable
}

declare void @exit(i32)

define i32 @world_alloc_entity(ptr %0) {
entry:
  %ent_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %ent_cap_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 4
  %ent_arch_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %cur_ent_count = load i32, ptr %ent_count_slot, align 4
  %cur_ent_cap = load i32, ptr %ent_cap_slot, align 4
  %need_grow_ent = icmp sge i32 %cur_ent_count, %cur_ent_cap
  br i1 %need_grow_ent, label %grow_ent, label %assign_ent

grow_ent:                                         ; preds = %entry
  %ent_cap_zero = icmp eq i32 %cur_ent_cap, 0
  %double_ent_cap = mul i32 %cur_ent_cap, 2
  %new_ent_cap = select i1 %ent_cap_zero, i32 64, i32 %double_ent_cap
  store i32 %new_ent_cap, ptr %ent_cap_slot, align 4
  %new_ent_cap64 = zext i32 %new_ent_cap to i64
  %bytes_for_ent = mul i64 %new_ent_cap64, 4
  %cur_arch_arr = load ptr, ptr %ent_arch_slot, align 8
  %new_arch_i8 = call ptr @realloc(ptr %cur_arch_arr, i64 %bytes_for_ent)
  store ptr %new_arch_i8, ptr %ent_arch_slot, align 8
  %cur_row_arr = load ptr, ptr %ent_row_slot, align 8
  %new_row_i8 = call ptr @realloc(ptr %cur_row_arr, i64 %bytes_for_ent)
  store ptr %new_row_i8, ptr %ent_row_slot, align 8
  br label %assign_ent

assign_ent:                                       ; preds = %grow_ent, %entry
  %next_ent_cnt = add i32 %cur_ent_count, 1
  store i32 %next_ent_cnt, ptr %ent_count_slot, align 4
  %cur_arch_arr_alloc = load ptr, ptr %ent_arch_slot, align 8
  %e_arch_slot_alloc = getelementptr inbounds i32, ptr %cur_arch_arr_alloc, i32 %cur_ent_count
  store i32 -1, ptr %e_arch_slot_alloc, align 4
  %cur_row_arr_alloc = load ptr, ptr %ent_row_slot, align 8
  %e_row_slot_alloc = getelementptr inbounds i32, ptr %cur_row_arr_alloc, i32 %cur_ent_count
  store i32 -1, ptr %e_row_slot_alloc, align 4
  ret i32 %cur_ent_count
}

define void @world_assign_a0(ptr %0, i32 %1) {
entry:
  %arch_arr_a0_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %arch_arr_a0 = load ptr, ptr %arch_arr_a0_slot, align 8
  %e_arch_slot_a0 = getelementptr inbounds i32, ptr %arch_arr_a0, i32 %1
  %cur_arch_val_a0 = load i32, ptr %e_arch_slot_a0, align 4
  %is_unassigned = icmp eq i32 %cur_arch_val_a0, -1
  br i1 %is_unassigned, label %do_assign, label %exit_a0

do_assign:                                        ; preds = %entry
  %a0_mask_assign = alloca [1 x i64], align 8
  store [1 x i64] zeroinitializer, ptr %a0_mask_assign, align 8
  %a0 = call i32 @world_get_or_create_archetype(ptr %0, ptr %a0_mask_assign)
  %tables_slot_a0 = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %tables_a0 = load ptr, ptr %tables_slot_a0, align 8
  %a0_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_a0, i32 %a0
  %cnt_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr, i32 0, i32 1
  %cur_cnt0 = load i32, ptr %cnt_slot0, align 4
  %cap_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr, i32 0, i32 2
  %cur_cap0 = load i32, ptr %cap_slot0, align 4
  %need_grow0 = icmp sge i32 %cur_cnt0, %cur_cap0
  br i1 %need_grow0, label %grow_a0, label %after_grow_a0

exit_a0:                                          ; preds = %after_grow_a0, %entry
  ret void

grow_a0:                                          ; preds = %do_assign
  call void @world_grow_archetype(ptr %0, i32 %a0)
  br label %after_grow_a0

after_grow_a0:                                    ; preds = %grow_a0, %do_assign
  %tables_a0_2 = load ptr, ptr %tables_slot_a0, align 8
  %a0_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_a0_2, i32 %a0
  %cnt_slot0_2 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr2, i32 0, i32 1
  %row = load i32, ptr %cnt_slot0_2, align 4
  %next_cnt0 = add i32 %row, 1
  store i32 %next_cnt0, ptr %cnt_slot0_2, align 4
  %ent_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr2, i32 0, i32 3
  %ent_raw0 = load ptr, ptr %ent_slot0, align 8
  %ent_elem0 = getelementptr inbounds i32, ptr %ent_raw0, i32 %row
  store i32 %1, ptr %ent_elem0, align 4
  store i32 %a0, ptr %e_arch_slot_a0, align 4
  %row_arr_a0_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %row_arr_a0 = load ptr, ptr %row_arr_a0_slot, align 8
  %e_row_slot_a0 = getelementptr inbounds i32, ptr %row_arr_a0, i32 %1
  store i32 %row, ptr %e_row_slot_a0, align 4
  br label %exit_a0
}

define i32 @world_spawn(ptr %0) {
entry:
  %e_spawn = call i32 @world_alloc_entity(ptr %0)
  call void @world_assign_a0(ptr %0, i32 %e_spawn)
  ret i32 %e_spawn
}

define void @world_despawn(ptr %0, i32 %1) {
entry:
  %ent_count_slot_ds = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_ds = load i32, ptr %ent_count_slot_ds, align 4
  %e_non_neg_ds = icmp sge i32 %1, 0
  %e_in_bounds_ds = icmp slt i32 %1, %total_ents_ds
  %is_valid_id_ds = and i1 %e_non_neg_ds, %e_in_bounds_ds
  br i1 %is_valid_id_ds, label %check_arch, label %ds_exit

check_arch:                                       ; preds = %entry
  %ent_arch_slot_ds = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_ds = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_ds = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_ds = load ptr, ptr %ent_arch_slot_ds, align 8
  %e_arch_slot_ds_inst = getelementptr inbounds i32, ptr %arch_arr_ds, i32 %1
  %cur_arch_idx_ds = load i32, ptr %e_arch_slot_ds_inst, align 4
  %is_alive_ds = icmp sge i32 %cur_arch_idx_ds, 0
  br i1 %is_alive_ds, label %do_despawn, label %ds_exit

ds_exit:                                          ; preds = %after_swap_ds, %check_arch, %entry
  ret void

do_despawn:                                       ; preds = %check_arch
  %row_arr_ds = load ptr, ptr %ent_row_slot_ds, align 8
  %e_row_slot_ds_inst = getelementptr inbounds i32, ptr %row_arr_ds, i32 %1
  %cur_row_ds = load i32, ptr %e_row_slot_ds_inst, align 4
  %tables_base_ds = load ptr, ptr %tables_slot_ds, align 8
  %cur_arch_ptr_ds = getelementptr inbounds %struct.Archetype, ptr %tables_base_ds, i32 %cur_arch_idx_ds
  %cur_cnt_slot_ds = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_ds, i32 0, i32 1
  %cur_arch_cnt_ds = load i32, ptr %cur_cnt_slot_ds, align 4
  %last_row_ds = sub i32 %cur_arch_cnt_ds, 1
  store i32 %last_row_ds, ptr %cur_cnt_slot_ds, align 4
  %is_last_row_ds = icmp eq i32 %cur_row_ds, %last_row_ds
  br i1 %is_last_row_ds, label %after_swap_ds, label %do_swap_ds

do_swap_ds:                                       ; preds = %do_despawn
  %cur_ent_slot_ds = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_ds, i32 0, i32 3
  %cur_ent_raw_ds = load ptr, ptr %cur_ent_slot_ds, align 8
  %last_ent_elem_ds = getelementptr inbounds i32, ptr %cur_ent_raw_ds, i32 %last_row_ds
  %moved_e_ds = load i32, ptr %last_ent_elem_ds, align 4
  %cur_ent_elem_ds = getelementptr inbounds i32, ptr %cur_ent_raw_ds, i32 %cur_row_ds
  store i32 %moved_e_ds, ptr %cur_ent_elem_ds, align 4
  %cur_mask_slot_ds = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_ds, i32 0, i32 0
  %cur_mask_ds = load i64, ptr %cur_mask_slot_ds, align 8
  %cur_cols_arr_ds = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_ds, i32 0, i32 4
  %has_sw_ds_ChildOf = and i64 %cur_mask_ds, 1
  %is_has_sw_ds_ChildOf = icmp ne i64 %has_sw_ds_ChildOf, 0
  br i1 %is_has_sw_ds_ChildOf, label %swap_ds_ChildOf, label %skip_sw_ds_ChildOf

after_swap_ds:                                    ; preds = %skip_sw_ds_Position, %do_despawn
  store i32 -2, ptr %e_arch_slot_ds_inst, align 4
  store i32 -2, ptr %e_row_slot_ds_inst, align 4
  br label %ds_exit

swap_ds_ChildOf:                                  ; preds = %do_swap_ds
  %sw_col_ds_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr_ds, i32 0, i32 0
  %sw_raw_ds_ChildOf = load ptr, ptr %sw_col_ds_ChildOf, align 8
  %sw_src_ds_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf, i32 %last_row_ds
  %sw_dst_ds_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf, i32 %cur_row_ds
  %2 = call ptr @memcpy(ptr %sw_dst_ds_ChildOf, ptr %sw_src_ds_ChildOf, i64 4)
  br label %skip_sw_ds_ChildOf

skip_sw_ds_ChildOf:                               ; preds = %swap_ds_ChildOf, %do_swap_ds
  %has_sw_ds_NameTag = and i64 %cur_mask_ds, 2
  %is_has_sw_ds_NameTag = icmp ne i64 %has_sw_ds_NameTag, 0
  br i1 %is_has_sw_ds_NameTag, label %swap_ds_NameTag, label %skip_sw_ds_NameTag

swap_ds_NameTag:                                  ; preds = %skip_sw_ds_ChildOf
  %sw_col_ds_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr_ds, i32 0, i32 1
  %sw_raw_ds_NameTag = load ptr, ptr %sw_col_ds_NameTag, align 8
  %sw_src_ds_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_ds_NameTag, i32 %last_row_ds
  %sw_dst_ds_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_ds_NameTag, i32 %cur_row_ds
  %3 = call ptr @memcpy(ptr %sw_dst_ds_NameTag, ptr %sw_src_ds_NameTag, i64 4)
  br label %skip_sw_ds_NameTag

skip_sw_ds_NameTag:                               ; preds = %swap_ds_NameTag, %skip_sw_ds_ChildOf
  %has_sw_ds_Position = and i64 %cur_mask_ds, 4
  %is_has_sw_ds_Position = icmp ne i64 %has_sw_ds_Position, 0
  br i1 %is_has_sw_ds_Position, label %swap_ds_Position, label %skip_sw_ds_Position

swap_ds_Position:                                 ; preds = %skip_sw_ds_NameTag
  %sw_col_ds_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr_ds, i32 0, i32 2
  %sw_raw_ds_Position = load ptr, ptr %sw_col_ds_Position, align 8
  %sw_src_ds_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position, i32 %last_row_ds
  %sw_dst_ds_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position, i32 %cur_row_ds
  %4 = call ptr @memcpy(ptr %sw_dst_ds_Position, ptr %sw_src_ds_Position, i64 8)
  br label %skip_sw_ds_Position

skip_sw_ds_Position:                              ; preds = %swap_ds_Position, %skip_sw_ds_NameTag
  %moved_e_row_slot_ds = getelementptr inbounds i32, ptr %row_arr_ds, i32 %moved_e_ds
  store i32 %cur_row_ds, ptr %moved_e_row_slot_ds, align 4
  br label %after_swap_ds
}

define void @world_cmd_ensure_cap(ptr %0, i32 %1) {
entry:
  %cmd_cnt_slot_ec = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_cap_slot_ec = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 8
  %cmd_data_slot_ec = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cmd_cnt = load i32, ptr %cmd_cnt_slot_ec, align 4
  %cur_cmd_cap = load i32, ptr %cmd_cap_slot_ec, align 4
  %needed_total = add i32 %cur_cmd_cnt, %1
  %need_grow_cmd = icmp sgt i32 %needed_total, %cur_cmd_cap
  br i1 %need_grow_cmd, label %grow_cmd, label %ec_exit

grow_cmd:                                         ; preds = %entry
  %double_cmd_cap = mul i32 %cur_cmd_cap, 2
  %lt_1k = icmp slt i32 %double_cmd_cap, 1024
  %at_least_1k = select i1 %lt_1k, i32 1024, i32 %double_cmd_cap
  %lt_needed = icmp slt i32 %at_least_1k, %needed_total
  %final_cap = select i1 %lt_needed, i32 %needed_total, i32 %at_least_1k
  store i32 %final_cap, ptr %cmd_cap_slot_ec, align 4
  %final_cap64 = zext i32 %final_cap to i64
  %cur_cmd_data = load ptr, ptr %cmd_data_slot_ec, align 8
  %new_cmd_data = call ptr @realloc(ptr %cur_cmd_data, i64 %final_cap64)
  store ptr %new_cmd_data, ptr %cmd_data_slot_ec, align 8
  br label %ec_exit

ec_exit:                                          ; preds = %grow_cmd, %entry
  ret void
}

define i32 @world_cmd_spawn(ptr %0) {
entry:
  %cmd_lock_slot_cs = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cs)
  %e_alloc_cs = call i32 @world_alloc_entity(ptr %0)
  call void @world_cmd_ensure_cap(ptr %0, i32 8)
  %cmd_cnt_slot_cs = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cs = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cs = load i32, ptr %cmd_cnt_slot_cs, align 4
  %data_ptr_cs = load ptr, ptr %cmd_data_slot_cs, align 8
  %cur_cnt_cs64 = zext i32 %cur_cnt_cs to i64
  %write_ptr_cs = getelementptr inbounds i8, ptr %data_ptr_cs, i64 %cur_cnt_cs64
  %op_slot_cs = getelementptr inbounds i32, ptr %write_ptr_cs, i32 0
  store i32 1, ptr %op_slot_cs, align 4
  %e_slot_cs = getelementptr inbounds i32, ptr %write_ptr_cs, i32 1
  store i32 %e_alloc_cs, ptr %e_slot_cs, align 4
  %new_cnt_cs = add i32 %cur_cnt_cs, 8
  store i32 %new_cnt_cs, ptr %cmd_cnt_slot_cs, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cs)
  ret i32 %e_alloc_cs
}

define void @world_cmd_despawn(ptr %0, i32 %1) {
entry:
  %cmd_lock_slot_cd = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cd)
  call void @world_cmd_ensure_cap(ptr %0, i32 8)
  %cmd_cnt_slot_cd = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cd = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cd = load i32, ptr %cmd_cnt_slot_cd, align 4
  %data_ptr_cd = load ptr, ptr %cmd_data_slot_cd, align 8
  %cur_cnt_cd64 = zext i32 %cur_cnt_cd to i64
  %write_ptr_cd = getelementptr inbounds i8, ptr %data_ptr_cd, i64 %cur_cnt_cd64
  %op_slot_cd = getelementptr inbounds i32, ptr %write_ptr_cd, i32 0
  store i32 2, ptr %op_slot_cd, align 4
  %e_slot_cd = getelementptr inbounds i32, ptr %write_ptr_cd, i32 1
  store i32 %1, ptr %e_slot_cd, align 4
  %new_cnt_cd = add i32 %cur_cnt_cd, 8
  store i32 %new_cnt_cd, ptr %cmd_cnt_slot_cd, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cd)
  ret void
}

define void @world_set_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_set_ChildOf)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 1
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_set_ChildOf)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_set_ChildOf)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 1
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 0
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.ChildOf, ptr %final_col_raw, i32 %final_row
  %parent_gep = getelementptr inbounds nuw %struct.ChildOf, ptr %final_elem, i32 0, i32 0
  store i32 %2, ptr %parent_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %6 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_add_ChildOf)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 1
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_add_ChildOf)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_add_ChildOf)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 1
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 0
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.ChildOf, ptr %final_col_raw, i32 %final_row
  %parent_gep = getelementptr inbounds nuw %struct.ChildOf, ptr %final_elem, i32 0, i32 0
  store i32 %2, ptr %parent_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %6 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_ChildOf(ptr %0, i32 %1) {
entry:
  %ent_count_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_rem = load i32, ptr %ent_count_slot_rem, align 4
  %rem_non_neg = icmp sge i32 %1, 0
  %rem_in_bounds = icmp slt i32 %1, %total_ents_rem
  %rem_is_valid_id = and i1 %rem_non_neg, %rem_in_bounds
  br i1 %rem_is_valid_id, label %check_arch_rem, label %rem_oob_error

check_arch_rem:                                   ; preds = %entry
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %is_neg_arch_rem = icmp slt i32 %cur_arch_rem, 0
  br i1 %is_neg_arch_rem, label %rem_err_entity, label %rem_cont

rem_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_rem_ChildOf)
  unreachable

rem_err_entity:                                   ; preds = %check_arch_rem
  %is_dead_rem = icmp eq i32 %cur_arch_rem, -2
  br i1 %is_dead_rem, label %rem_dead_entity_error, label %rem_pending_entity_error

rem_cont:                                         ; preds = %check_arch_rem
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_w0_rem_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_w0_rem_ptr, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 1
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

rem_dead_entity_error:                            ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_dead_rem_ChildOf)
  unreachable

rem_pending_entity_error:                         ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_pending_rem_ChildOf)
  unreachable

do_remove:                                        ; preds = %rem_cont
  %new_mask_rem = and i64 %cur_mask_val_rem, -2
  %temp_mask_rem = alloca [1 x i64], align 8
  %src_rem_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %val_rem_w0 = load i64, ptr %src_rem_w0, align 8
  %dst_rem_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_rem, i32 0, i32 0
  store i64 %new_mask_rem, ptr %dst_rem_w0, align 8
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %rem_cont
  ret void

grow_rem_arch:                                    ; preds = %do_remove
  call void @world_grow_archetype(ptr %0, i32 %new_arch_rem)
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %do_remove
  %tables_rem_tr2 = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %cur_arch_rem
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %new_arch_rem
  %cnt_slot_rem2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 1
  %new_row_rem = load i32, ptr %cnt_slot_rem2, align 4
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 3
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i32 %new_row_rem
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 4
  %new_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 4
  %rem_has_NameTag = and i64 %cur_mask_val_rem, 2
  %is_has_rem_NameTag = icmp ne i64 %rem_has_NameTag, 0
  br i1 %is_has_rem_NameTag, label %copy_rem_NameTag, label %skip_rem_NameTag

copy_rem_NameTag:                                 ; preds = %after_grow_rem_arch
  %rem_src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %rem_src_raw_NameTag = load ptr, ptr %rem_src_col_NameTag, align 8
  %rem_src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %rem_src_raw_NameTag, i32 %cur_row_rem
  %rem_dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 1
  %rem_dst_raw_NameTag = load ptr, ptr %rem_dst_col_NameTag, align 8
  %rem_dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %rem_dst_raw_NameTag, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_NameTag, ptr %rem_src_elem_NameTag, i64 4)
  br label %skip_rem_NameTag

skip_rem_NameTag:                                 ; preds = %copy_rem_NameTag, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Position = icmp ne i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position, label %copy_rem_Position, label %skip_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_NameTag
  %rem_src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i32 %cur_row_rem
  %rem_dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 2
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Position, ptr %rem_src_elem_Position, i64 8)
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_NameTag
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Position
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Position, %skip_rem_Position
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %4 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_NameTag = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_NameTag = icmp ne i64 %sw_rem_has_NameTag, 0
  br i1 %is_sw_rem_NameTag, label %swap_rem_NameTag, label %skip_sw_rem_NameTag

swap_rem_NameTag:                                 ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_NameTag = load ptr, ptr %sw_col_rem_NameTag, align 8
  %sw_src_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %last_row_rem
  %sw_dst_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_NameTag, ptr %sw_src_rem_NameTag, i64 4)
  br label %skip_sw_rem_NameTag

skip_sw_rem_NameTag:                              ; preds = %swap_rem_NameTag, %skip_sw_rem_ChildOf
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_NameTag
  %sw_col_rem_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_NameTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_ChildOf(ptr %0, i32 %1) {
entry:
  %ent_count_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_has = load i32, ptr %ent_count_slot_has, align 4
  %has_non_neg = icmp sge i32 %1, 0
  %has_in_bounds = icmp slt i32 %1, %total_ents_has
  %has_is_valid_id = and i1 %has_non_neg, %has_in_bounds
  br i1 %has_is_valid_id, label %check_arch_has, label %has_oob_error

check_arch_has:                                   ; preds = %entry
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sge i32 %cur_arch_idx_has, 0
  br i1 %is_alive_has, label %check_mask, label %ret_false

has_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_has_ChildOf)
  unreachable

check_mask:                                       ; preds = %check_arch_has
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %mask_w0_has_ptr = getelementptr inbounds [1 x i64], ptr %mask_slot_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_w0_has_ptr, align 8
  %bit_and_has = and i64 %arch_mask_has, 1
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has

ret_false:                                        ; preds = %check_arch_has
  ret i1 false
}

define void @world_cmd_set_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 16)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 0, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.ChildOf, ptr %payload_raw, i32 0, i32 0
  store i32 %2, ptr %f_slot_0, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 16
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_add_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 16)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 0, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.ChildOf, ptr %payload_raw, i32 0, i32 0
  store i32 %2, ptr %f_slot_0, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 16
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_ChildOf(ptr %0, i32 %1) {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_crem)
  call void @world_cmd_ensure_cap(ptr %0, i32 12)
  %cmd_cnt_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_crem = load i32, ptr %cmd_cnt_slot_crem, align 4
  %data_ptr_crem = load ptr, ptr %cmd_data_slot_crem, align 8
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  %op_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 0
  store i32 4, ptr %op_slot_crem, align 4
  %e_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 1
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 2
  store i32 0, ptr %comp_id_slot_crem, align 4
  %new_cnt_crem = add i32 %cur_cnt_crem, 12
  store i32 %new_cnt_crem, ptr %cmd_cnt_slot_crem, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_crem)
  ret void
}

define void @world_set_NameTag(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_set_NameTag)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 2
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_set_NameTag)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_set_NameTag)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 2
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 1
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.NameTag, ptr %final_col_raw, i32 %final_row
  %id_gep = getelementptr inbounds nuw %struct.NameTag, ptr %final_elem, i32 0, i32 0
  store i32 %2, ptr %id_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %6 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_NameTag(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_add_NameTag)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 2
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_add_NameTag)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_add_NameTag)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 2
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 1
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.NameTag, ptr %final_col_raw, i32 %final_row
  %id_gep = getelementptr inbounds nuw %struct.NameTag, ptr %final_elem, i32 0, i32 0
  store i32 %2, ptr %id_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %6 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_NameTag(ptr %0, i32 %1) {
entry:
  %ent_count_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_rem = load i32, ptr %ent_count_slot_rem, align 4
  %rem_non_neg = icmp sge i32 %1, 0
  %rem_in_bounds = icmp slt i32 %1, %total_ents_rem
  %rem_is_valid_id = and i1 %rem_non_neg, %rem_in_bounds
  br i1 %rem_is_valid_id, label %check_arch_rem, label %rem_oob_error

check_arch_rem:                                   ; preds = %entry
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %is_neg_arch_rem = icmp slt i32 %cur_arch_rem, 0
  br i1 %is_neg_arch_rem, label %rem_err_entity, label %rem_cont

rem_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_rem_NameTag)
  unreachable

rem_err_entity:                                   ; preds = %check_arch_rem
  %is_dead_rem = icmp eq i32 %cur_arch_rem, -2
  br i1 %is_dead_rem, label %rem_dead_entity_error, label %rem_pending_entity_error

rem_cont:                                         ; preds = %check_arch_rem
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_w0_rem_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_w0_rem_ptr, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 2
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

rem_dead_entity_error:                            ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_dead_rem_NameTag)
  unreachable

rem_pending_entity_error:                         ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_pending_rem_NameTag)
  unreachable

do_remove:                                        ; preds = %rem_cont
  %new_mask_rem = and i64 %cur_mask_val_rem, -3
  %temp_mask_rem = alloca [1 x i64], align 8
  %src_rem_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %val_rem_w0 = load i64, ptr %src_rem_w0, align 8
  %dst_rem_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_rem, i32 0, i32 0
  store i64 %new_mask_rem, ptr %dst_rem_w0, align 8
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %rem_cont
  ret void

grow_rem_arch:                                    ; preds = %do_remove
  call void @world_grow_archetype(ptr %0, i32 %new_arch_rem)
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %do_remove
  %tables_rem_tr2 = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %cur_arch_rem
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %new_arch_rem
  %cnt_slot_rem2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 1
  %new_row_rem = load i32, ptr %cnt_slot_rem2, align 4
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 3
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i32 %new_row_rem
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 4
  %new_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 4
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf = icmp ne i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf, label %copy_rem_ChildOf, label %skip_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %rem_src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %rem_src_raw_ChildOf = load ptr, ptr %rem_src_col_ChildOf, align 8
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i32 %cur_row_rem
  %rem_dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 0
  %rem_dst_raw_ChildOf = load ptr, ptr %rem_dst_col_ChildOf, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_ChildOf, ptr %rem_src_elem_ChildOf, i64 4)
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Position = icmp ne i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position, label %copy_rem_Position, label %skip_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i32 %cur_row_rem
  %rem_dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 2
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Position, ptr %rem_src_elem_Position, i64 8)
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Position
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Position, %skip_rem_Position
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %4 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_NameTag = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_NameTag = icmp ne i64 %sw_rem_has_NameTag, 0
  br i1 %is_sw_rem_NameTag, label %swap_rem_NameTag, label %skip_sw_rem_NameTag

swap_rem_NameTag:                                 ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_NameTag = load ptr, ptr %sw_col_rem_NameTag, align 8
  %sw_src_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %last_row_rem
  %sw_dst_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_NameTag, ptr %sw_src_rem_NameTag, i64 4)
  br label %skip_sw_rem_NameTag

skip_sw_rem_NameTag:                              ; preds = %swap_rem_NameTag, %skip_sw_rem_ChildOf
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_NameTag
  %sw_col_rem_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_NameTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_NameTag(ptr %0, i32 %1) {
entry:
  %ent_count_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_has = load i32, ptr %ent_count_slot_has, align 4
  %has_non_neg = icmp sge i32 %1, 0
  %has_in_bounds = icmp slt i32 %1, %total_ents_has
  %has_is_valid_id = and i1 %has_non_neg, %has_in_bounds
  br i1 %has_is_valid_id, label %check_arch_has, label %has_oob_error

check_arch_has:                                   ; preds = %entry
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sge i32 %cur_arch_idx_has, 0
  br i1 %is_alive_has, label %check_mask, label %ret_false

has_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_has_NameTag)
  unreachable

check_mask:                                       ; preds = %check_arch_has
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %mask_w0_has_ptr = getelementptr inbounds [1 x i64], ptr %mask_slot_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_w0_has_ptr, align 8
  %bit_and_has = and i64 %arch_mask_has, 2
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has

ret_false:                                        ; preds = %check_arch_has
  ret i1 false
}

define void @world_cmd_set_NameTag(ptr %0, i32 %1, i32 %2) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 16)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 1, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.NameTag, ptr %payload_raw, i32 0, i32 0
  store i32 %2, ptr %f_slot_0, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 16
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_add_NameTag(ptr %0, i32 %1, i32 %2) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 16)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 1, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.NameTag, ptr %payload_raw, i32 0, i32 0
  store i32 %2, ptr %f_slot_0, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 16
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_NameTag(ptr %0, i32 %1) {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_crem)
  call void @world_cmd_ensure_cap(ptr %0, i32 12)
  %cmd_cnt_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_crem = load i32, ptr %cmd_cnt_slot_crem, align 4
  %data_ptr_crem = load ptr, ptr %cmd_data_slot_crem, align 8
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  %op_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 0
  store i32 4, ptr %op_slot_crem, align 4
  %e_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 1
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 2
  store i32 1, ptr %comp_id_slot_crem, align 4
  %new_cnt_crem = add i32 %cur_cnt_crem, 12
  store i32 %new_cnt_crem, ptr %cmd_cnt_slot_crem, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_crem)
  ret void
}

define void @world_set_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_set_Position)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 4
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_set_Position)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_set_Position)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 4
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 2
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Position, ptr %final_col_raw, i32 %final_row
  %x_gep = getelementptr inbounds nuw %struct.Position, ptr %final_elem, i32 0, i32 0
  store float %2, ptr %x_gep, align 4
  %y_gep = getelementptr inbounds nuw %struct.Position, ptr %final_elem, i32 0, i32 1
  store float %3, ptr %y_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_count_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_set = load i32, ptr %ent_count_slot_set, align 4
  %e_non_neg_set = icmp sge i32 %1, 0
  %e_in_bounds_set = icmp slt i32 %1, %total_ents_set
  %is_valid_id_set = and i1 %e_non_neg_set, %e_in_bounds_set
  br i1 %is_valid_id_set, label %check_arch_set, label %set_oob_error

check_arch_set:                                   ; preds = %entry
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_err_entity, label %set_cont

set_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_world_add_Position)
  unreachable

set_err_entity:                                   ; preds = %check_arch_set
  %is_dead = icmp eq i32 %cur_arch_idx_raw, -2
  br i1 %is_dead, label %set_dead_entity_error, label %set_pending_entity_error

set_cont:                                         ; preds = %check_arch_set
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx_raw
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask_w0_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_w0_ptr, align 8
  %has_bit = and i64 %cur_mask, 4
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

set_dead_entity_error:                            ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_dead_world_add_Position)
  unreachable

set_pending_entity_error:                         ; preds = %set_err_entity
  call void @rt_panic(ptr @ecs_err_pending_world_add_Position)
  unreachable

in_place_update:                                  ; preds = %set_cont
  store i32 %cur_arch_idx_raw, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or i64 %cur_mask, 4
  %temp_mask_set = alloca [1 x i64], align 8
  %src_set_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_slot, i32 0, i32 0
  %val_set_w0 = load i64, ptr %src_set_w0, align 8
  %dst_set_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_set, i32 0, i32 0
  store i64 %new_mask, ptr %dst_set_w0, align 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_set)
  %tables_tr1 = load ptr, ptr %tables_slot_set, align 8
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i32 %new_arch_idx
  %new_cnt_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 1
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr1, i32 0, i32 2
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new = icmp sge i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new, label %grow_new_arch, label %after_grow_new_arch

store_fields:                                     ; preds = %after_swap_remove, %in_place_update
  %final_arch = load i32, ptr %target_arch, align 4
  %final_row = load i32, ptr %target_row, align 4
  %latest_tables_sf = load ptr, ptr %tables_slot_set, align 8
  %final_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i32 %final_arch
  %final_cols = getelementptr inbounds nuw %struct.Archetype, ptr %final_arch_ptr, i32 0, i32 4
  %final_col_slot = getelementptr inbounds [3 x ptr], ptr %final_cols, i32 0, i32 2
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Position, ptr %final_col_raw, i32 %final_row
  %x_gep = getelementptr inbounds nuw %struct.Position, ptr %final_elem, i32 0, i32 0
  store float %2, ptr %x_gep, align 4
  %y_gep = getelementptr inbounds nuw %struct.Position, ptr %final_elem, i32 0, i32 1
  store float %3, ptr %y_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx_raw
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %new_arch_idx
  %new_cnt_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 1
  %new_row = load i32, ptr %new_cnt_slot2, align 4
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 3
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i32 %new_row
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 4
  %new_cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr2, i32 0, i32 4
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf = icmp ne i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf, label %copy_ChildOf, label %skip_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_NameTag = and i64 %cur_mask, 2
  %is_has_NameTag = icmp ne i64 %has_NameTag, 0
  br i1 %is_has_NameTag, label %copy_NameTag, label %skip_NameTag

copy_NameTag:                                     ; preds = %skip_ChildOf
  %src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_NameTag = load ptr, ptr %src_col_NameTag, align 8
  %src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %src_raw_NameTag, i32 %cur_row
  %dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_NameTag = load ptr, ptr %dst_col_NameTag, align 8
  %dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %dst_raw_NameTag, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_NameTag, ptr %src_elem_NameTag, i64 4)
  br label %skip_NameTag

skip_NameTag:                                     ; preds = %copy_NameTag, %skip_ChildOf
  %has_Position = and i64 %cur_mask, 4
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_NameTag
  %src_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [3 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_NameTag
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Position
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Position, %skip_Position
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i32 %1
  store i32 %new_arch_idx, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i32 %1
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  store i32 %new_arch_idx, ptr %target_arch, align 4
  store i32 %new_row, ptr %target_row, align 4
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_NameTag = and i64 %cur_mask, 2
  %is_has_sw_NameTag = icmp ne i64 %has_sw_NameTag, 0
  br i1 %is_has_sw_NameTag, label %swap_NameTag, label %skip_sw_NameTag

swap_NameTag:                                     ; preds = %skip_sw_ChildOf
  %sw_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_NameTag = load ptr, ptr %sw_col_NameTag, align 8
  %sw_src_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %last_row
  %sw_dst_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_NameTag, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_NameTag, ptr %sw_src_NameTag, i64 4)
  br label %skip_sw_NameTag

skip_sw_NameTag:                                  ; preds = %swap_NameTag, %skip_sw_ChildOf
  %has_sw_Position = and i64 %cur_mask, 4
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_NameTag
  %sw_col_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_NameTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_Position(ptr %0, i32 %1) {
entry:
  %ent_count_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_rem = load i32, ptr %ent_count_slot_rem, align 4
  %rem_non_neg = icmp sge i32 %1, 0
  %rem_in_bounds = icmp slt i32 %1, %total_ents_rem
  %rem_is_valid_id = and i1 %rem_non_neg, %rem_in_bounds
  br i1 %rem_is_valid_id, label %check_arch_rem, label %rem_oob_error

check_arch_rem:                                   ; preds = %entry
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %is_neg_arch_rem = icmp slt i32 %cur_arch_rem, 0
  br i1 %is_neg_arch_rem, label %rem_err_entity, label %rem_cont

rem_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_rem_Position)
  unreachable

rem_err_entity:                                   ; preds = %check_arch_rem
  %is_dead_rem = icmp eq i32 %cur_arch_rem, -2
  br i1 %is_dead_rem, label %rem_dead_entity_error, label %rem_pending_entity_error

rem_cont:                                         ; preds = %check_arch_rem
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_w0_rem_ptr = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_w0_rem_ptr, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 4
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

rem_dead_entity_error:                            ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_dead_rem_Position)
  unreachable

rem_pending_entity_error:                         ; preds = %rem_err_entity
  call void @rt_panic(ptr @ecs_err_pending_rem_Position)
  unreachable

do_remove:                                        ; preds = %rem_cont
  %new_mask_rem = and i64 %cur_mask_val_rem, -5
  %temp_mask_rem = alloca [1 x i64], align 8
  %src_rem_w0 = getelementptr inbounds [1 x i64], ptr %cur_mask_rem, i32 0, i32 0
  %val_rem_w0 = load i64, ptr %src_rem_w0, align 8
  %dst_rem_w0 = getelementptr inbounds [1 x i64], ptr %temp_mask_rem, i32 0, i32 0
  store i64 %new_mask_rem, ptr %dst_rem_w0, align 8
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, ptr %temp_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %rem_cont
  ret void

grow_rem_arch:                                    ; preds = %do_remove
  call void @world_grow_archetype(ptr %0, i32 %new_arch_rem)
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %do_remove
  %tables_rem_tr2 = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %cur_arch_rem
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i32 %new_arch_rem
  %cnt_slot_rem2 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 1
  %new_row_rem = load i32, ptr %cnt_slot_rem2, align 4
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 3
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i32 %new_row_rem
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 4
  %new_cols_rem = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem2, i32 0, i32 4
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf = icmp ne i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf, label %copy_rem_ChildOf, label %skip_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %rem_src_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %rem_src_raw_ChildOf = load ptr, ptr %rem_src_col_ChildOf, align 8
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i32 %cur_row_rem
  %rem_dst_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 0
  %rem_dst_raw_ChildOf = load ptr, ptr %rem_dst_col_ChildOf, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_ChildOf, ptr %rem_src_elem_ChildOf, i64 4)
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_NameTag = and i64 %cur_mask_val_rem, 2
  %is_has_rem_NameTag = icmp ne i64 %rem_has_NameTag, 0
  br i1 %is_has_rem_NameTag, label %copy_rem_NameTag, label %skip_rem_NameTag

copy_rem_NameTag:                                 ; preds = %skip_rem_ChildOf
  %rem_src_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %rem_src_raw_NameTag = load ptr, ptr %rem_src_col_NameTag, align 8
  %rem_src_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %rem_src_raw_NameTag, i32 %cur_row_rem
  %rem_dst_col_NameTag = getelementptr inbounds [3 x ptr], ptr %new_cols_rem, i32 0, i32 1
  %rem_dst_raw_NameTag = load ptr, ptr %rem_dst_col_NameTag, align 8
  %rem_dst_elem_NameTag = getelementptr inbounds %struct.NameTag, ptr %rem_dst_raw_NameTag, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_NameTag, ptr %rem_src_elem_NameTag, i64 4)
  br label %skip_rem_NameTag

skip_rem_NameTag:                                 ; preds = %copy_rem_NameTag, %skip_rem_ChildOf
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_NameTag
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Position, %skip_rem_NameTag
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %4 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_NameTag = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_NameTag = icmp ne i64 %sw_rem_has_NameTag, 0
  br i1 %is_sw_rem_NameTag, label %swap_rem_NameTag, label %skip_sw_rem_NameTag

swap_rem_NameTag:                                 ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_NameTag = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_NameTag = load ptr, ptr %sw_col_rem_NameTag, align 8
  %sw_src_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %last_row_rem
  %sw_dst_rem_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_raw_rem_NameTag, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_NameTag, ptr %sw_src_rem_NameTag, i64 4)
  br label %skip_sw_rem_NameTag

skip_sw_rem_NameTag:                              ; preds = %swap_rem_NameTag, %skip_sw_rem_ChildOf
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_NameTag
  %sw_col_rem_Position = getelementptr inbounds [3 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_NameTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_Position(ptr %0, i32 %1) {
entry:
  %ent_count_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 3
  %total_ents_has = load i32, ptr %ent_count_slot_has, align 4
  %has_non_neg = icmp sge i32 %1, 0
  %has_in_bounds = icmp slt i32 %1, %total_ents_has
  %has_is_valid_id = and i1 %has_non_neg, %has_in_bounds
  br i1 %has_is_valid_id, label %check_arch_has, label %has_oob_error

check_arch_has:                                   ; preds = %entry
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sge i32 %cur_arch_idx_has, 0
  br i1 %is_alive_has, label %check_mask, label %ret_false

has_oob_error:                                    ; preds = %entry
  call void @rt_panic(ptr @ecs_err_oob_has_Position)
  unreachable

check_mask:                                       ; preds = %check_arch_has
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %mask_w0_has_ptr = getelementptr inbounds [1 x i64], ptr %mask_slot_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_w0_has_ptr, align 8
  %bit_and_has = and i64 %arch_mask_has, 4
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has

ret_false:                                        ; preds = %check_arch_has
  ret i1 false
}

define void @world_cmd_set_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 20)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 2, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.Position, ptr %payload_raw, i32 0, i32 0
  store float %2, ptr %f_slot_0, align 4
  %f_slot_1 = getelementptr inbounds nuw %struct.Position, ptr %payload_raw, i32 0, i32 1
  store float %3, ptr %f_slot_1, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 20
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_add_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_cset)
  call void @world_cmd_ensure_cap(ptr %0, i32 20)
  %cmd_cnt_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_cset = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_cset = load i32, ptr %cmd_cnt_slot_cset, align 4
  %data_ptr_cset = load ptr, ptr %cmd_data_slot_cset, align 8
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  %op_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 0
  store i32 3, ptr %op_slot_cset, align 4
  %e_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 1
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds i32, ptr %write_ptr_cset, i32 2
  store i32 2, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds i8, ptr %write_ptr_cset, i64 12
  %f_slot_0 = getelementptr inbounds nuw %struct.Position, ptr %payload_raw, i32 0, i32 0
  store float %2, ptr %f_slot_0, align 4
  %f_slot_1 = getelementptr inbounds nuw %struct.Position, ptr %payload_raw, i32 0, i32 1
  store float %3, ptr %f_slot_1, align 4
  %new_cnt_cset = add i32 %cur_cnt_cset, 20
  store i32 %new_cnt_cset, ptr %cmd_cnt_slot_cset, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_Position(ptr %0, i32 %1) {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 10
  call void @AcquireSRWLockExclusive(ptr %cmd_lock_slot_crem)
  call void @world_cmd_ensure_cap(ptr %0, i32 12)
  %cmd_cnt_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_crem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %cur_cnt_crem = load i32, ptr %cmd_cnt_slot_crem, align 4
  %data_ptr_crem = load ptr, ptr %cmd_data_slot_crem, align 8
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  %op_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 0
  store i32 4, ptr %op_slot_crem, align 4
  %e_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 1
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds i32, ptr %write_ptr_crem, i32 2
  store i32 2, ptr %comp_id_slot_crem, align 4
  %new_cnt_crem = add i32 %cur_cnt_crem, 12
  store i32 %new_cnt_crem, ptr %cmd_cnt_slot_crem, align 4
  call void @ReleaseSRWLockExclusive(ptr %cmd_lock_slot_crem)
  ret void
}

define void @world_apply_commands(ptr %0) {
entry:
  %cmd_cnt_slot_ac = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 7
  %cmd_data_slot_ac = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 9
  %total_cmd_bytes = load i32, ptr %cmd_cnt_slot_ac, align 4
  %has_cmds = icmp sgt i32 %total_cmd_bytes, 0
  br i1 %has_cmds, label %ac_loop_head, label %ac_exit

ac_loop_head:                                     ; preds = %entry
  %ac_offset = alloca i32, align 4
  store i32 0, ptr %ac_offset, align 4
  br label %ac_loop_cond

ac_exit:                                          ; preds = %ac_loop_cond, %entry
  store i32 0, ptr %cmd_cnt_slot_ac, align 4
  ret void

ac_loop_cond:                                     ; preds = %op_default, %rem_def, %rem_case_Position, %rem_case_NameTag, %rem_case_ChildOf, %set_def, %set_case_Position, %set_case_NameTag, %set_case_ChildOf, %op_despawn, %op_spawn, %ac_loop_head
  %cur_offset = load i32, ptr %ac_offset, align 4
  %has_more_cmds = icmp slt i32 %cur_offset, %total_cmd_bytes
  br i1 %has_more_cmds, label %ac_loop_body, label %ac_exit

ac_loop_body:                                     ; preds = %ac_loop_cond
  %data_ptr_ac = load ptr, ptr %cmd_data_slot_ac, align 8
  %cur_offset64 = zext i32 %cur_offset to i64
  %cur_cmd_ptr = getelementptr inbounds i8, ptr %data_ptr_ac, i64 %cur_offset64
  %op_slot = getelementptr inbounds i32, ptr %cur_cmd_ptr, i32 0
  %op_val = load i32, ptr %op_slot, align 4
  %e_slot = getelementptr inbounds i32, ptr %cur_cmd_ptr, i32 1
  %e_val = load i32, ptr %e_slot, align 4
  switch i32 %op_val, label %op_default [
    i32 1, label %op_spawn
    i32 2, label %op_despawn
    i32 3, label %op_set
    i32 4, label %op_remove
  ]

op_spawn:                                         ; preds = %ac_loop_body
  call void @world_assign_a0(ptr %0, i32 %e_val)
  %next_off_1 = add i32 %cur_offset, 8
  store i32 %next_off_1, ptr %ac_offset, align 4
  br label %ac_loop_cond

op_despawn:                                       ; preds = %ac_loop_body
  call void @world_despawn(ptr %0, i32 %e_val)
  %next_off_2 = add i32 %cur_offset, 8
  store i32 %next_off_2, ptr %ac_offset, align 4
  br label %ac_loop_cond

op_set:                                           ; preds = %ac_loop_body
  %comp_id_slot_set = getelementptr inbounds i32, ptr %cur_cmd_ptr, i32 2
  %comp_id_set = load i32, ptr %comp_id_slot_set, align 4
  %payload_ptr_set = getelementptr inbounds i8, ptr %cur_cmd_ptr, i64 12
  switch i32 %comp_id_set, label %set_def [
    i32 0, label %set_case_ChildOf
    i32 1, label %set_case_NameTag
    i32 2, label %set_case_Position
  ]

op_remove:                                        ; preds = %ac_loop_body
  %comp_id_slot_rem = getelementptr inbounds i32, ptr %cur_cmd_ptr, i32 2
  %comp_id_rem = load i32, ptr %comp_id_slot_rem, align 4
  switch i32 %comp_id_rem, label %rem_def [
    i32 0, label %rem_case_ChildOf
    i32 1, label %rem_case_NameTag
    i32 2, label %rem_case_Position
  ]

op_default:                                       ; preds = %ac_loop_body
  %next_off_def = add i32 %cur_offset, 4
  store i32 %next_off_def, ptr %ac_offset, align 4
  br label %ac_loop_cond

set_def:                                          ; preds = %op_set
  %next_off_set_def = add i32 %cur_offset, 12
  store i32 %next_off_set_def, ptr %ac_offset, align 4
  br label %ac_loop_cond

set_case_ChildOf:                                 ; preds = %op_set
  %p_f_0 = getelementptr inbounds nuw %struct.ChildOf, ptr %payload_ptr_set, i32 0, i32 0
  %f_val_0 = load i32, ptr %p_f_0, align 4
  call void @world_set_ChildOf(ptr %0, i32 %e_val, i32 %f_val_0)
  %next_off_set_ChildOf = add i32 %cur_offset, 16
  store i32 %next_off_set_ChildOf, ptr %ac_offset, align 4
  br label %ac_loop_cond

set_case_NameTag:                                 ; preds = %op_set
  %p_f_01 = getelementptr inbounds nuw %struct.NameTag, ptr %payload_ptr_set, i32 0, i32 0
  %f_val_02 = load i32, ptr %p_f_01, align 4
  call void @world_set_NameTag(ptr %0, i32 %e_val, i32 %f_val_02)
  %next_off_set_NameTag = add i32 %cur_offset, 16
  store i32 %next_off_set_NameTag, ptr %ac_offset, align 4
  br label %ac_loop_cond

set_case_Position:                                ; preds = %op_set
  %p_f_03 = getelementptr inbounds nuw %struct.Position, ptr %payload_ptr_set, i32 0, i32 0
  %f_val_04 = load float, ptr %p_f_03, align 4
  %p_f_1 = getelementptr inbounds nuw %struct.Position, ptr %payload_ptr_set, i32 0, i32 1
  %f_val_1 = load float, ptr %p_f_1, align 4
  call void @world_set_Position(ptr %0, i32 %e_val, float %f_val_04, float %f_val_1)
  %next_off_set_Position = add i32 %cur_offset, 20
  store i32 %next_off_set_Position, ptr %ac_offset, align 4
  br label %ac_loop_cond

rem_def:                                          ; preds = %op_remove
  %next_off_rem_def = add i32 %cur_offset, 12
  store i32 %next_off_rem_def, ptr %ac_offset, align 4
  br label %ac_loop_cond

rem_case_ChildOf:                                 ; preds = %op_remove
  call void @world_remove_ChildOf(ptr %0, i32 %e_val)
  %next_off_rem_ChildOf = add i32 %cur_offset, 12
  store i32 %next_off_rem_ChildOf, ptr %ac_offset, align 4
  br label %ac_loop_cond

rem_case_NameTag:                                 ; preds = %op_remove
  call void @world_remove_NameTag(ptr %0, i32 %e_val)
  %next_off_rem_NameTag = add i32 %cur_offset, 12
  store i32 %next_off_rem_NameTag, ptr %ac_offset, align 4
  br label %ac_loop_cond

rem_case_Position:                                ; preds = %op_remove
  call void @world_remove_Position(ptr %0, i32 %e_val)
  %next_off_rem_Position = add i32 %cur_offset, 12
  store i32 %next_off_rem_Position, ptr %ac_offset, align 4
  br label %ac_loop_cond
}

define void @world_sort_hierarchy(ptr %0) {
entry:
  %arch_count_sh = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 0
  %tables_slot_sh = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %ent_row_slot_sh = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %num_archs = load i32, ptr %arch_count_sh, align 4
  %arch_i = alloca i32, align 4
  store i32 0, ptr %arch_i, align 4
  %init_i = alloca i32, align 4
  %sort_i = alloca i32, align 4
  %sort_j = alloca i32, align 4
  %temp_ChildOf = alloca %struct.ChildOf, align 8
  %temp_NameTag = alloca %struct.NameTag, align 8
  %temp_Position = alloca %struct.Position, align 8
  br label %arch_loop_cond

arch_loop_cond:                                   ; preds = %next_arch, %entry
  %cur_a_idx = load i32, ptr %arch_i, align 4
  %has_more_archs = icmp slt i32 %cur_a_idx, %num_archs
  br i1 %has_more_archs, label %arch_loop_body, label %arch_loop_exit

arch_loop_body:                                   ; preds = %arch_loop_cond
  %t_base = load ptr, ptr %tables_slot_sh, align 8
  %cur_a = getelementptr inbounds %struct.Archetype, ptr %t_base, i32 %cur_a_idx
  %m_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_a, i32 0, i32 0
  %m_val = load i64, ptr %m_slot, align 8
  %and_co = and i64 %m_val, 1
  %has_co = icmp ne i64 %and_co, 0
  br i1 %has_co, label %check_sort, label %next_arch

arch_loop_exit:                                   ; preds = %arch_loop_cond
  ret void

check_sort:                                       ; preds = %arch_loop_body
  %cnt_slot_sh = getelementptr inbounds nuw %struct.Archetype, ptr %cur_a, i32 0, i32 1
  %cnt_sh = load i32, ptr %cnt_slot_sh, align 4
  %can_sort = icmp sgt i32 %cnt_sh, 1
  br i1 %can_sort, label %do_sort, label %next_arch

next_arch:                                        ; preds = %sort_outer_exit, %check_sort, %arch_loop_body
  %next_a_idx = add i32 %cur_a_idx, 1
  store i32 %next_a_idx, ptr %arch_i, align 4
  br label %arch_loop_cond

do_sort:                                          ; preds = %check_sort
  %count64 = zext i32 %cnt_sh to i64
  %bytes_needed = mul i64 %count64, 4
  %depths_raw = call ptr @malloc(i64 %bytes_needed)
  %cols_sh = getelementptr inbounds nuw %struct.Archetype, ptr %cur_a, i32 0, i32 4
  %co_slot = getelementptr inbounds [3 x ptr], ptr %cols_sh, i32 0, i32 0
  %co_col_raw = load ptr, ptr %co_slot, align 8
  store i32 0, ptr %init_i, align 4
  br label %d_init_cond

d_init_cond:                                      ; preds = %d_init_body, %do_sort
  %cur_init_i = load i32, ptr %init_i, align 4
  %has_init_more = icmp slt i32 %cur_init_i, %cnt_sh
  br i1 %has_init_more, label %d_init_body, label %d_init_exit

d_init_body:                                      ; preds = %d_init_cond
  %child_elem = getelementptr inbounds %struct.ChildOf, ptr %co_col_raw, i32 %cur_init_i
  %parent_gep = getelementptr inbounds nuw %struct.ChildOf, ptr %child_elem, i32 0, i32 0
  %parent_id = load i32, ptr %parent_gep, align 4
  %is_root = icmp slt i32 %parent_id, 0
  %depth_val = select i1 %is_root, i32 0, i32 1
  %depth_slot = getelementptr inbounds i32, ptr %depths_raw, i32 %cur_init_i
  store i32 %depth_val, ptr %depth_slot, align 4
  %next_init_i = add i32 %cur_init_i, 1
  store i32 %next_init_i, ptr %init_i, align 4
  br label %d_init_cond

d_init_exit:                                      ; preds = %d_init_cond
  store i32 0, ptr %sort_i, align 4
  br label %sort_outer_cond

sort_outer_cond:                                  ; preds = %sort_inner_step, %d_init_exit
  %cur_sort_i = load i32, ptr %sort_i, align 4
  %outer_limit = sub i32 %cnt_sh, 1
  %outer_more = icmp slt i32 %cur_sort_i, %outer_limit
  br i1 %outer_more, label %sort_outer_body, label %sort_outer_exit

sort_outer_body:                                  ; preds = %sort_outer_cond
  %inner_start = add i32 %cur_sort_i, 1
  store i32 %inner_start, ptr %sort_j, align 4
  br label %sort_inner_cond

sort_outer_exit:                                  ; preds = %sort_outer_cond
  call void @free(ptr %depths_raw)
  br label %next_arch

sort_inner_cond:                                  ; preds = %skip_swap_row, %sort_outer_body
  %cur_sort_j = load i32, ptr %sort_j, align 4
  %inner_more = icmp slt i32 %cur_sort_j, %cnt_sh
  br i1 %inner_more, label %sort_inner_body, label %sort_inner_step

sort_inner_body:                                  ; preds = %sort_inner_cond
  %di_slot = getelementptr inbounds i32, ptr %depths_raw, i32 %cur_sort_i
  %dj_slot = getelementptr inbounds i32, ptr %depths_raw, i32 %cur_sort_j
  %di = load i32, ptr %di_slot, align 4
  %dj = load i32, ptr %dj_slot, align 4
  %need_swap = icmp sgt i32 %di, %dj
  br i1 %need_swap, label %do_swap_row, label %skip_swap_row

sort_inner_step:                                  ; preds = %sort_inner_cond
  %next_sort_i = add i32 %cur_sort_i, 1
  store i32 %next_sort_i, ptr %sort_i, align 4
  br label %sort_outer_cond

do_swap_row:                                      ; preds = %sort_inner_body
  store i32 %dj, ptr %di_slot, align 4
  store i32 %di, ptr %dj_slot, align 4
  %ent_slot_sh = getelementptr inbounds nuw %struct.Archetype, ptr %cur_a, i32 0, i32 3
  %ent_raw_sh = load ptr, ptr %ent_slot_sh, align 8
  %ei_slot = getelementptr inbounds i32, ptr %ent_raw_sh, i32 %cur_sort_i
  %ej_slot = getelementptr inbounds i32, ptr %ent_raw_sh, i32 %cur_sort_j
  %ei = load i32, ptr %ei_slot, align 4
  %ej = load i32, ptr %ej_slot, align 4
  store i32 %ej, ptr %ei_slot, align 4
  store i32 %ei, ptr %ej_slot, align 4
  %row_arr_sh = load ptr, ptr %ent_row_slot_sh, align 8
  %ei_row_slot = getelementptr inbounds i32, ptr %row_arr_sh, i32 %ei
  %ej_row_slot = getelementptr inbounds i32, ptr %row_arr_sh, i32 %ej
  store i32 %cur_sort_j, ptr %ei_row_slot, align 4
  store i32 %cur_sort_i, ptr %ej_row_slot, align 4
  %sw_co_has_ChildOf = and i64 %m_val, 1
  %is_sw_co_ChildOf = icmp ne i64 %sw_co_has_ChildOf, 0
  br i1 %is_sw_co_ChildOf, label %sw_sh_ChildOf, label %skip_sw_sh_ChildOf

skip_swap_row:                                    ; preds = %skip_sw_sh_Position, %sort_inner_body
  %next_j = add i32 %cur_sort_j, 1
  store i32 %next_j, ptr %sort_j, align 4
  br label %sort_inner_cond

sw_sh_ChildOf:                                    ; preds = %do_swap_row
  %sw_sh_col_ChildOf = getelementptr inbounds [3 x ptr], ptr %cols_sh, i32 0, i32 0
  %sw_sh_raw_ChildOf = load ptr, ptr %sw_sh_col_ChildOf, align 8
  %elem_i_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i32 %cur_sort_i
  %elem_j_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i32 %cur_sort_j
  %1 = call ptr @memcpy(ptr %temp_ChildOf, ptr %elem_i_ChildOf, i64 4)
  %2 = call ptr @memcpy(ptr %elem_i_ChildOf, ptr %elem_j_ChildOf, i64 4)
  %3 = call ptr @memcpy(ptr %elem_j_ChildOf, ptr %temp_ChildOf, i64 4)
  br label %skip_sw_sh_ChildOf

skip_sw_sh_ChildOf:                               ; preds = %sw_sh_ChildOf, %do_swap_row
  %sw_co_has_NameTag = and i64 %m_val, 2
  %is_sw_co_NameTag = icmp ne i64 %sw_co_has_NameTag, 0
  br i1 %is_sw_co_NameTag, label %sw_sh_NameTag, label %skip_sw_sh_NameTag

sw_sh_NameTag:                                    ; preds = %skip_sw_sh_ChildOf
  %sw_sh_col_NameTag = getelementptr inbounds [3 x ptr], ptr %cols_sh, i32 0, i32 1
  %sw_sh_raw_NameTag = load ptr, ptr %sw_sh_col_NameTag, align 8
  %elem_i_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_sh_raw_NameTag, i32 %cur_sort_i
  %elem_j_NameTag = getelementptr inbounds %struct.NameTag, ptr %sw_sh_raw_NameTag, i32 %cur_sort_j
  %4 = call ptr @memcpy(ptr %temp_NameTag, ptr %elem_i_NameTag, i64 4)
  %5 = call ptr @memcpy(ptr %elem_i_NameTag, ptr %elem_j_NameTag, i64 4)
  %6 = call ptr @memcpy(ptr %elem_j_NameTag, ptr %temp_NameTag, i64 4)
  br label %skip_sw_sh_NameTag

skip_sw_sh_NameTag:                               ; preds = %sw_sh_NameTag, %skip_sw_sh_ChildOf
  %sw_co_has_Position = and i64 %m_val, 4
  %is_sw_co_Position = icmp ne i64 %sw_co_has_Position, 0
  br i1 %is_sw_co_Position, label %sw_sh_Position, label %skip_sw_sh_Position

sw_sh_Position:                                   ; preds = %skip_sw_sh_NameTag
  %sw_sh_col_Position = getelementptr inbounds [3 x ptr], ptr %cols_sh, i32 0, i32 2
  %sw_sh_raw_Position = load ptr, ptr %sw_sh_col_Position, align 8
  %elem_i_Position = getelementptr inbounds %struct.Position, ptr %sw_sh_raw_Position, i32 %cur_sort_i
  %elem_j_Position = getelementptr inbounds %struct.Position, ptr %sw_sh_raw_Position, i32 %cur_sort_j
  %7 = call ptr @memcpy(ptr %temp_Position, ptr %elem_i_Position, i64 8)
  %8 = call ptr @memcpy(ptr %elem_i_Position, ptr %elem_j_Position, i64 8)
  %9 = call ptr @memcpy(ptr %elem_j_Position, ptr %temp_Position, i64 8)
  br label %skip_sw_sh_Position

skip_sw_sh_Position:                              ; preds = %sw_sh_Position, %skip_sw_sh_NameTag
  br label %skip_swap_row
}

define void @world_swap_events(ptr %world) {
entry:
  %swap_emit_lock_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 11
  call void @AcquireSRWLockExclusive(ptr %swap_emit_lock_slot)
  call void @ReleaseSRWLockExclusive(ptr %swap_emit_lock_slot)
  ret void
}

define void @world_render_profiler(ptr %world) {
entry:
  %win_ready_raw = call i8 @IsWindowReady()
  %win_ready = icmp ne i8 %win_ready_raw, 0
  br i1 %win_ready, label %check_key, label %exit

check_key:                                        ; preds = %entry
  %f1_pressed_raw = call i8 @IsKeyPressed(i32 290)
  %f1_pressed = icmp ne i8 %f1_pressed_raw, 0
  br i1 %f1_pressed, label %toggle_vis, label %render_check

render_check:                                     ; preds = %toggle_vis, %check_key
  %is_vis = load i32, ptr @g_ecs_profiler_visible, align 4
  %should_render = icmp ne i32 %is_vis, 0
  br i1 %should_render, label %do_render, label %exit

do_render:                                        ; preds = %render_check
  call void @DrawRectangle(i32 10, i32 10, i32 500, i32 320, i32 -350350321)
  call void @DrawRectangleLines(i32 10, i32 10, i32 500, i32 320, i32 -1012686)
  call void @DrawText(ptr @p_title, i32 22, i32 20, i32 18, i32 -11656)
  %str_buf = alloca [256 x i8], align 1
  %cur_fps = call i32 @GetFPS()
  %cur_ft_sec = call float @GetFrameTime()
  %ft_ms = fmul float %cur_ft_sec, 1.000000e+03
  %ft_ms_d = fpext float %ft_ms to double
  %0 = call i32 (ptr, ptr, ...) @sprintf(ptr %str_buf, ptr @fps_fmt, i32 %cur_fps, double %ft_ms_d)
  call void @DrawText(ptr %str_buf, i32 24, i32 48, i32 16, i32 -8856496)
  %p_arch_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 0
  %p_tables_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 2
  %p_ent_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 3
  %p_arch_count = load i32, ptr %p_arch_count_slot, align 4
  %p_ent_count = load i32, ptr %p_ent_count_slot, align 4
  %1 = call i32 (ptr, ptr, ...) @sprintf(ptr %str_buf, ptr @stats_fmt, i32 %p_ent_count, i32 %p_arch_count)
  call void @DrawText(ptr %str_buf, i32 24, i32 72, i32 16, i32 -1318436)
  %p_a_i = alloca i32, align 4
  store i32 0, ptr %p_a_i, align 4
  br label %p_a_cond

exit:                                             ; preds = %p_a_exit, %render_check, %entry
  ret void

toggle_vis:                                       ; preds = %check_key
  %cur_vis = load i32, ptr @g_ecs_profiler_visible, align 4
  %toggled_vis = sub i32 1, %cur_vis
  store i32 %toggled_vis, ptr @g_ecs_profiler_visible, align 4
  br label %render_check

p_a_cond:                                         ; preds = %p_a_body, %do_render
  %cur_a = load i32, ptr %p_a_i, align 4
  %a_more = icmp slt i32 %cur_a, %p_arch_count
  %a_limit = icmp slt i32 %cur_a, 7
  %a_cond = and i1 %a_more, %a_limit
  br i1 %a_cond, label %p_a_body, label %p_a_exit

p_a_body:                                         ; preds = %p_a_cond
  %p_tables = load ptr, ptr %p_tables_slot, align 8
  %p_arch = getelementptr inbounds %struct.Archetype, ptr %p_tables, i32 %cur_a
  %p_a_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %p_arch, i32 0, i32 0
  %p_a_count_slot = getelementptr inbounds nuw %struct.Archetype, ptr %p_arch, i32 0, i32 1
  %p_a_cap_slot = getelementptr inbounds nuw %struct.Archetype, ptr %p_arch, i32 0, i32 2
  %p_a_mask = load i64, ptr %p_a_mask_slot, align 8
  %p_a_count = load i32, ptr %p_a_count_slot, align 4
  %p_a_cap = load i32, ptr %p_a_cap_slot, align 4
  %a_y_mul = mul i32 %cur_a, 24
  %a_y = add i32 104, %a_y_mul
  %2 = call i32 (ptr, ptr, ...) @sprintf(ptr %str_buf, ptr @arch_fmt, i32 %cur_a, i32 %p_a_count, i32 %p_a_cap, i64 %p_a_mask)
  call void @DrawText(ptr %str_buf, i32 24, i32 %a_y, i32 15, i32 -1318436)
  %next_a = add i32 %cur_a, 1
  store i32 %next_a, ptr %p_a_i, align 4
  br label %p_a_cond

p_a_exit:                                         ; preds = %p_a_cond
  call void @DrawText(ptr @p_hint, i32 24, i32 290, i32 14, i32 -5597556)
  br label %exit
}

declare ptr @memmove(ptr, ptr, i64)

declare i32 @htonl(i32)

declare i32 @ntohl(i32)

declare i64 @socket(i32, i32, i32)

declare i32 @bind(i64, ptr, i32)

declare i32 @listen(i64, i32)

declare i64 @accept(i64, ptr, ptr)

declare i32 @connect(i64, ptr, i32)

declare i32 @send(i64, ptr, i32, i32)

declare i32 @recv(i64, ptr, i32, i32)

declare i32 @sendto(i64, ptr, i32, i32, ptr, i32)

declare i32 @recvfrom(i64, ptr, i32, i32, ptr, ptr)

declare i32 @setsockopt(i64, i32, i32, ptr, i32)

declare i16 @htons(i16)

declare i32 @inet_addr(ptr)

declare i32 @WSAStartup(i16, ptr)

declare i32 @WSACleanup()

declare i32 @WSAGetLastError()

declare i32 @ioctlsocket(i64, i32, ptr)

declare i32 @closesocket(i64)

define i32 @ecs_net_init() {
entry:
  %wsa_data = alloca [512 x i8], align 1
  %wsa_res = call i32 @WSAStartup(i16 514, ptr %wsa_data)
  ret i32 %wsa_res
}

define void @ecs_net_cleanup() {
entry:
  %0 = call i32 @WSACleanup()
  ret void
}

define i32 @ecs_net_tcp_listen(i32 %0) {
entry:
  %sock = call i64 @socket(i32 2, i32 1, i32 0)
  %s_invalid = icmp slt i64 %sock, 0
  br i1 %s_invalid, label %err, label %ok

err:                                              ; preds = %entry
  ret i32 -1

ok:                                               ; preds = %entry
  %sin_addr = alloca [16 x i8], align 1
  %1 = call ptr @memset(ptr %sin_addr, i32 0, i64 16)
  store i16 2, ptr %sin_addr, align 2
  %port_i16 = trunc i32 %0 to i16
  %net_port = call i16 @htons(i16 %port_i16)
  %p_raw = getelementptr i8, ptr %sin_addr, i32 2
  store i16 %net_port, ptr %p_raw, align 2
  %in_raw = getelementptr i8, ptr %sin_addr, i32 4
  store i32 0, ptr %in_raw, align 4
  %bind_res = call i32 @bind(i64 %sock, ptr %sin_addr, i32 16)
  %bind_failed = icmp slt i32 %bind_res, 0
  br i1 %bind_failed, label %listen_err_clean, label %bind_ok

listen_err_clean:                                 ; preds = %bind_ok, %ok
  %2 = call i32 @closesocket(i64 %sock)
  ret i32 -1

bind_ok:                                          ; preds = %ok
  %listen_res = call i32 @listen(i64 %sock, i32 128)
  %listen_failed = icmp slt i32 %listen_res, 0
  br i1 %listen_failed, label %listen_err_clean, label %listen_success

listen_success:                                   ; preds = %bind_ok
  %nb_one = alloca i32, align 4
  store i32 1, ptr %nb_one, align 4
  %3 = call i32 @ioctlsocket(i64 %sock, i32 -2147195266, ptr %nb_one)
  %s_val32 = trunc i64 %sock to i32
  ret i32 %s_val32
}

define i32 @ecs_net_tcp_connect(ptr %0, i32 %1) {
entry:
  %c_sock = call i64 @socket(i32 2, i32 1, i32 0)
  %cs_invalid = icmp slt i64 %c_sock, 0
  br i1 %cs_invalid, label %err, label %ok

err:                                              ; preds = %entry
  ret i32 -1

ok:                                               ; preds = %entry
  %c_addr = alloca [16 x i8], align 1
  %2 = call ptr @memset(ptr %c_addr, i32 0, i64 16)
  store i16 2, ptr %c_addr, align 2
  %c_port_i16 = trunc i32 %1 to i16
  %c_net_port = call i16 @htons(i16 %c_port_i16)
  %3 = getelementptr i8, ptr %c_addr, i32 2
  store i16 %c_net_port, ptr %3, align 2
  %ip_parsed = call i32 @inet_addr(ptr %0)
  %4 = getelementptr i8, ptr %c_addr, i32 4
  store i32 %ip_parsed, ptr %4, align 4
  %conn_res = call i32 @connect(i64 %c_sock, ptr %c_addr, i32 16)
  %c_nb = alloca i32, align 4
  store i32 1, ptr %c_nb, align 4
  %5 = call i32 @ioctlsocket(i64 %c_sock, i32 -2147195266, ptr %c_nb)
  %conn_failed = icmp slt i32 %conn_res, 0
  br i1 %conn_failed, label %conn_check_err, label %conn_success

conn_check_err:                                   ; preds = %ok
  %last_err = call i32 @WSAGetLastError()
  %is_wb = icmp eq i32 %last_err, 10035
  br i1 %is_wb, label %conn_success, label %conn_fail_clean

conn_success:                                     ; preds = %conn_check_err, %ok
  %6 = trunc i64 %c_sock to i32
  ret i32 %6

conn_fail_clean:                                  ; preds = %conn_check_err
  %7 = call i32 @closesocket(i64 %c_sock)
  ret i32 -1
}

define i32 @ecs_net_tcp_accept(i32 %0) {
entry:
  %l_fd64 = zext i32 %0 to i64
  %client_fd = call i64 @accept(i64 %l_fd64, ptr null, ptr null)
  %client_invalid = icmp slt i64 %client_fd, 0
  br i1 %client_invalid, label %err, label %ok

ok:                                               ; preds = %entry
  %cl_nb = alloca i32, align 4
  store i32 1, ptr %cl_nb, align 4
  %1 = call i32 @ioctlsocket(i64 %client_fd, i32 -2147195266, ptr %cl_nb)
  %client_fd32 = trunc i64 %client_fd to i32
  ret i32 %client_fd32

err:                                              ; preds = %entry
  ret i32 -1
}

define i32 @ecs_net_tcp_send(i32 %0, ptr %1) {
entry:
  %2 = zext i32 %0 to i64
  %data_len64 = call i64 @strlen(ptr %1)
  %data_len32 = trunc i64 %data_len64 to i32
  %sent = call i32 @send(i64 %2, ptr %1, i32 %data_len32, i32 0)
  ret i32 %sent
}

define ptr @ecs_net_tcp_recv(i32 %0, i32 %1) {
entry:
  %2 = zext i32 %0 to i64
  %3 = icmp sgt i32 %1, 0
  %max_len_clean = select i1 %3, i32 %1, i32 4096
  %alloc_sz = add i32 %max_len_clean, 1
  %alloc_sz64 = zext i32 %alloc_sz to i64
  %recv_buf = call ptr @malloc(i64 %alloc_sz64)
  %read_bytes = call i32 @recv(i64 %2, ptr %recv_buf, i32 %max_len_clean, i32 0)
  %has_data1 = icmp sgt i32 %read_bytes, 0
  br i1 %has_data1, label %has_data, label %empty

has_data:                                         ; preds = %entry
  %r_read64 = sext i32 %read_bytes to i64
  %term_ptr = getelementptr i8, ptr %recv_buf, i64 %r_read64
  store i8 0, ptr %term_ptr, align 1
  ret ptr %recv_buf

empty:                                            ; preds = %entry
  call void @free(ptr %recv_buf)
  ret ptr @net_empty
}

define i32 @ecs_net_udp_bind(i32 %0) {
entry:
  %u_sock = call i64 @socket(i32 2, i32 2, i32 0)
  %1 = icmp slt i64 %u_sock, 0
  br i1 %1, label %err, label %ok

err:                                              ; preds = %entry
  ret i32 -1

ok:                                               ; preds = %entry
  %u_nb = alloca i32, align 4
  store i32 1, ptr %u_nb, align 4
  %2 = call i32 @ioctlsocket(i64 %u_sock, i32 -2147195266, ptr %u_nb)
  %u_addr = alloca [16 x i8], align 1
  %3 = call ptr @memset(ptr %u_addr, i32 0, i64 16)
  store i16 2, ptr %u_addr, align 2
  %4 = trunc i32 %0 to i16
  %5 = call i16 @htons(i16 %4)
  %6 = getelementptr i8, ptr %u_addr, i32 2
  store i16 %5, ptr %6, align 2
  %7 = getelementptr i8, ptr %u_addr, i32 4
  store i32 0, ptr %7, align 4
  %8 = call i32 @bind(i64 %u_sock, ptr %u_addr, i32 16)
  %9 = trunc i64 %u_sock to i32
  ret i32 %9
}

define i32 @ecs_net_udp_connect(i32 %0, ptr %1, i32 %2) {
entry:
  %3 = zext i32 %0 to i64
  %uc_addr = alloca [16 x i8], align 1
  %4 = call ptr @memset(ptr %uc_addr, i32 0, i64 16)
  store i16 2, ptr %uc_addr, align 2
  %5 = trunc i32 %2 to i16
  %6 = call i16 @htons(i16 %5)
  %7 = getelementptr i8, ptr %uc_addr, i32 2
  store i16 %6, ptr %7, align 2
  %8 = call i32 @inet_addr(ptr %1)
  %9 = getelementptr i8, ptr %uc_addr, i32 4
  store i32 %8, ptr %9, align 4
  %uc_res = call i32 @connect(i64 %3, ptr %uc_addr, i32 16)
  ret i32 %uc_res
}

define i32 @ecs_net_udp_send_to(i32 %0, ptr %1, i32 %2, ptr %3) {
entry:
  %4 = zext i32 %0 to i64
  %5 = call i64 @strlen(ptr %3)
  %6 = trunc i64 %5 to i32
  %ut_addr = alloca [16 x i8], align 1
  %7 = call ptr @memset(ptr %ut_addr, i32 0, i64 16)
  store i16 2, ptr %ut_addr, align 2
  %8 = trunc i32 %2 to i16
  %9 = call i16 @htons(i16 %8)
  %10 = getelementptr i8, ptr %ut_addr, i32 2
  store i16 %9, ptr %10, align 2
  %11 = call i32 @inet_addr(ptr %1)
  %12 = getelementptr i8, ptr %ut_addr, i32 4
  store i32 %11, ptr %12, align 4
  %u_sent = call i32 @sendto(i64 %4, ptr %3, i32 %6, i32 0, ptr %ut_addr, i32 16)
  ret i32 %u_sent
}

define ptr @ecs_net_udp_recv_from(i32 %0, i32 %1) {
entry:
  %2 = zext i32 %0 to i64
  %3 = icmp sgt i32 %1, 0
  %4 = select i1 %3, i32 %1, i32 4096
  %5 = add i32 %4, 1
  %6 = zext i32 %5 to i64
  %u_buf = call ptr @malloc(i64 %6)
  %u_from_addr = alloca [16 x i8], align 1
  %u_from_len = alloca i32, align 4
  store i32 16, ptr %u_from_len, align 4
  %u_recv = call i32 @recvfrom(i64 %2, ptr %u_buf, i32 %4, i32 0, ptr %u_from_addr, ptr %u_from_len)
  %7 = icmp sgt i32 %u_recv, 0
  br i1 %7, label %has_data, label %empty

has_data:                                         ; preds = %entry
  %8 = sext i32 %u_recv to i64
  %9 = getelementptr i8, ptr %u_buf, i64 %8
  store i8 0, ptr %9, align 1
  ret ptr %u_buf

empty:                                            ; preds = %entry
  call void @free(ptr %u_buf)
  ret ptr @net_udp_empty
}

define void @ecs_net_close(i32 %0) {
entry:
  %1 = zext i32 %0 to i64
  %2 = call i32 @closesocket(i64 %1)
  ret void
}

define i32 @ecs_net_get_last_error() {
entry:
  %err_val = call i32 @WSAGetLastError()
  ret i32 %err_val
}

define void @ecs_net_poll_wait(i32 %0) {
entry:
  call void @Sleep(i32 %0)
  ret void
}

declare void @Sleep(i32)

define i32 @ecs_net_tcp_send_framed(i32 %0, ptr %1) {
entry:
  %sf_fd64 = zext i32 %0 to i64
  %sf_plen64 = call i64 @strlen(ptr %1)
  %sf_plen32 = trunc i64 %sf_plen64 to i32
  %sf_netlen = call i32 @htonl(i32 %sf_plen32)
  %sf_tot_len32 = add i32 %sf_plen32, 4
  %sf_tot_len64 = zext i32 %sf_tot_len32 to i64
  %sf_buf = call ptr @malloc(i64 %sf_tot_len64)
  store i32 %sf_netlen, ptr %sf_buf, align 4
  %sf_payload_dst = getelementptr i8, ptr %sf_buf, i32 4
  %2 = call ptr @memcpy(ptr %sf_payload_dst, ptr %1, i64 %sf_plen64)
  %sf_sent = call i32 @send(i64 %sf_fd64, ptr %sf_buf, i32 %sf_tot_len32, i32 0)
  call void @free(ptr %sf_buf)
  ret i32 %sf_sent
}

define { ptr, i32, i32 } @ecs_net_tcp_recv_append(i32 %0, { ptr, i32, i32 } %1, i32 %2) {
entry:
  %ra_fd64 = zext i32 %0 to i64
  %3 = icmp sgt i32 %2, 0
  %ra_maxlen_clean = select i1 %3, i32 %2, i32 4096
  %4 = zext i32 %ra_maxlen_clean to i64
  %ra_tmp = call ptr @malloc(i64 %4)
  %ra_read = call i32 @recv(i64 %ra_fd64, ptr %ra_tmp, i32 %ra_maxlen_clean, i32 0)
  %ra_has_data = icmp sgt i32 %ra_read, 0
  br i1 %ra_has_data, label %has_data, label %no_data

has_data:                                         ; preds = %entry
  %cur_data = extractvalue { ptr, i32, i32 } %1, 0
  %cur_len = extractvalue { ptr, i32, i32 } %1, 1
  %cur_cap = extractvalue { ptr, i32, i32 } %1, 2
  %needed_len = add i32 %cur_len, %ra_read
  %need_grow = icmp sgt i32 %needed_len, %cur_cap
  br i1 %need_grow, label %ra_grow, label %ra_append

no_data:                                          ; preds = %entry
  call void @free(ptr %ra_tmp)
  ret { ptr, i32, i32 } %1

ra_grow:                                          ; preds = %has_data
  %ra_double_cap = mul i32 %cur_cap, 2
  %5 = icmp sgt i32 %needed_len, %ra_double_cap
  %ra_larger_cap = select i1 %5, i32 %needed_len, i32 %ra_double_cap
  %6 = icmp slt i32 %ra_larger_cap, 16
  %ra_new_cap = select i1 %6, i32 16, i32 %ra_larger_cap
  %7 = zext i32 %ra_new_cap to i64
  %ra_realloc = call ptr @realloc(ptr %cur_data, i64 %7)
  br label %ra_append

ra_append:                                        ; preds = %ra_grow, %has_data
  %final_data = phi ptr [ %cur_data, %has_data ], [ %ra_realloc, %ra_grow ]
  %final_cap = phi i32 [ %cur_cap, %has_data ], [ %ra_new_cap, %ra_grow ]
  %ra_append_dst = getelementptr i8, ptr %final_data, i32 %cur_len
  %8 = zext i32 %ra_read to i64
  %9 = call ptr @memmove(ptr %ra_append_dst, ptr %ra_tmp, i64 %8)
  call void @free(ptr %ra_tmp)
  %ra_res0 = insertvalue { ptr, i32, i32 } zeroinitializer, ptr %final_data, 0
  %ra_res1 = insertvalue { ptr, i32, i32 } %ra_res0, i32 %needed_len, 1
  %ra_res2 = insertvalue { ptr, i32, i32 } %ra_res1, i32 %final_cap, 2
  ret { ptr, i32, i32 } %ra_res2
}

define i32 @ecs_net_buffer_read_i32({ ptr, i32, i32 } %0, i32 %1) {
entry:
  %rb_data = extractvalue { ptr, i32, i32 } %0, 0
  %rb_len = extractvalue { ptr, i32, i32 } %0, 1
  %off_geq_0 = icmp sge i32 %1, 0
  %off_plus_4 = add i32 %1, 4
  %off_leq_len = icmp sle i32 %off_plus_4, %rb_len
  %off_valid = and i1 %off_geq_0, %off_leq_len
  br i1 %off_valid, label %valid, label %invalid

valid:                                            ; preds = %entry
  %rb_ptr_raw = getelementptr i8, ptr %rb_data, i32 %1
  %rb_alloca = alloca i32, align 4
  %2 = call ptr @memcpy(ptr %rb_alloca, ptr %rb_ptr_raw, i64 4)
  %rb_raw_val = load i32, ptr %rb_alloca, align 4
  %rb_host_val = call i32 @ntohl(i32 %rb_raw_val)
  ret i32 %rb_host_val

invalid:                                          ; preds = %entry
  ret i32 -1
}

define ptr @ecs_net_buffer_extract_str({ ptr, i32, i32 } %0, i32 %1, i32 %2) {
entry:
  %es_data = extractvalue { ptr, i32, i32 } %0, 0
  %es_buflen = extractvalue { ptr, i32, i32 } %0, 1
  %es_off_geq_0 = icmp sge i32 %1, 0
  %es_len_geq_0 = icmp sge i32 %2, 0
  %es_end = add i32 %1, %2
  %es_end_leq = icmp sle i32 %es_end, %es_buflen
  %3 = and i1 %es_off_geq_0, %es_len_geq_0
  %es_cond = and i1 %3, %es_end_leq
  br i1 %es_cond, label %valid, label %invalid

valid:                                            ; preds = %entry
  %es_alloc_sz = add i32 %2, 1
  %es_alloc_sz64 = zext i32 %es_alloc_sz to i64
  %es_str = call ptr @malloc(i64 %es_alloc_sz64)
  %es_src = getelementptr i8, ptr %es_data, i32 %1
  %es_len64 = zext i32 %2 to i64
  %4 = call ptr @memcpy(ptr %es_str, ptr %es_src, i64 %es_len64)
  %es_term = getelementptr i8, ptr %es_str, i64 %es_len64
  store i8 0, ptr %es_term, align 1
  ret ptr %es_str

invalid:                                          ; preds = %entry
  ret ptr @net_extract_empty
}

define { ptr, i32, i32 } @ecs_net_buffer_drain({ ptr, i32, i32 } %0, i32 %1) {
entry:
  %dr_data = extractvalue { ptr, i32, i32 } %0, 0
  %dr_len = extractvalue { ptr, i32, i32 } %0, 1
  %dr_cap = extractvalue { ptr, i32, i32 } %0, 2
  %count_leq_0 = icmp sle i32 %1, 0
  br i1 %count_leq_0, label %nop, label %check_clear

shift:                                            ; preds = %check_clear
  %dr_rem = sub i32 %dr_len, %1
  %dr_src = getelementptr i8, ptr %dr_data, i32 %1
  %2 = zext i32 %dr_rem to i64
  %3 = call ptr @memmove(ptr %dr_data, ptr %dr_src, i64 %2)
  %dr_shift_res = insertvalue { ptr, i32, i32 } %0, i32 %dr_rem, 1
  ret { ptr, i32, i32 } %dr_shift_res

clear:                                            ; preds = %check_clear
  %dr_clear_res = insertvalue { ptr, i32, i32 } %0, i32 0, 1
  ret { ptr, i32, i32 } %dr_clear_res

nop:                                              ; preds = %entry
  ret { ptr, i32, i32 } %0

check_clear:                                      ; preds = %entry
  %count_geq_len = icmp sge i32 %1, %dr_len
  br i1 %count_geq_len, label %clear, label %shift
}

define i32 @main() {
entry:
  %grandchild = alloca i32, align 4
  %child = alloca i32, align 4
  %parent = alloca i32, align 4
  %world = alloca ptr, align 8
  %0 = call i32 @SetConsoleOutputCP(i32 65001)
  %1 = call i32 @SetConsoleCP(i32 65001)
  %new_world = call ptr @ecs_create_world()
  store ptr %new_world, ptr %world, align 8
  %world1 = load ptr, ptr %world, align 8
  %spawn_call = call i32 @world_spawn(ptr %world1)
  store i32 %spawn_call, ptr %parent, align 4
  %world2 = load ptr, ptr %world, align 8
  %parent3 = load i32, ptr %parent, align 4
  call void @world_set_Position(ptr %world2, i32 %parent3, float 0.000000e+00, float 0.000000e+00)
  %world4 = load ptr, ptr %world, align 8
  %parent5 = load i32, ptr %parent, align 4
  call void @world_set_NameTag(ptr %world4, i32 %parent5, i32 100)
  %world6 = load ptr, ptr %world, align 8
  %spawn_call7 = call i32 @world_spawn(ptr %world6)
  store i32 %spawn_call7, ptr %child, align 4
  %world8 = load ptr, ptr %world, align 8
  %child9 = load i32, ptr %child, align 4
  call void @world_set_Position(ptr %world8, i32 %child9, float 1.000000e+01, float 1.000000e+01)
  %world10 = load ptr, ptr %world, align 8
  %child11 = load i32, ptr %child, align 4
  call void @world_set_NameTag(ptr %world10, i32 %child11, i32 200)
  %world12 = load ptr, ptr %world, align 8
  %child13 = load i32, ptr %child, align 4
  %parent14 = load i32, ptr %parent, align 4
  call void @world_set_ChildOf(ptr %world12, i32 %child13, i32 %parent14)
  %world15 = load ptr, ptr %world, align 8
  %spawn_call16 = call i32 @world_spawn(ptr %world15)
  store i32 %spawn_call16, ptr %grandchild, align 4
  %world17 = load ptr, ptr %world, align 8
  %grandchild18 = load i32, ptr %grandchild, align 4
  call void @world_set_Position(ptr %world17, i32 %grandchild18, float 2.000000e+01, float 2.000000e+01)
  %world19 = load ptr, ptr %world, align 8
  %grandchild20 = load i32, ptr %grandchild, align 4
  call void @world_set_NameTag(ptr %world19, i32 %grandchild20, i32 300)
  %world21 = load ptr, ptr %world, align 8
  %grandchild22 = load i32, ptr %grandchild, align 4
  %child23 = load i32, ptr %child, align 4
  call void @world_set_ChildOf(ptr %world21, i32 %grandchild22, i32 %child23)
  %world24 = load ptr, ptr %world, align 8
  call void @pipeline_HierarchyPipeline(ptr %world24)
  %puts_call = call i32 @puts(ptr @str_lit)
  %puts_exit = call i32 @puts(ptr @prompt_exit)
  %h_stdin = call ptr @GetStdHandle(i32 -10)
  %2 = call i32 @FlushConsoleInputBuffer(ptr %h_stdin)
  %auto_wait_key = call i32 @_getch()
  ret i32 0
}

define void @pipeline_HierarchyPipeline(ptr %world) {
entry:
  call void @world_swap_events(ptr %world)
  call void @world_sort_hierarchy(ptr %world)
  call void @world_apply_commands(ptr %world)
  ret void
}

declare i32 @SetConsoleOutputCP(i32)

declare i32 @SetConsoleCP(i32)

declare ptr @GetStdHandle(i32)

declare i32 @FlushConsoleInputBuffer(ptr)
