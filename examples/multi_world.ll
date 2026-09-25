; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.EcsWorld = type { i32, i32, ptr, i32, i32, ptr, ptr }
%struct.Archetype = type { i64, i32, i32, ptr, [4 x ptr] }
%struct.ChildOf = type { i32 }
%struct.Position = type { float, float }
%struct.Velocity = type { float, float }
%struct.Label = type { ptr }

@str_lit = private unnamed_addr constant [42 x i8] c"  [Game System] Moved entity to Position:\00", align 1
@fmt_f = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@fmt_f.1 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.2 = private unnamed_addr constant [43 x i8] c"  [UI System] Rendering Label at Position:\00", align 1
@fmt_f.3 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@fmt_f.4 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.5 = private unnamed_addr constant [47 x i8] c"=== ECS Multi-World Universal Context Demo ===\00", align 1
@str_lit.6 = private unnamed_addr constant [11 x i8] c"Start Game\00", align 1
@str_lit.7 = private unnamed_addr constant [5 x i8] c"Quit\00", align 1
@str_lit.8 = private unnamed_addr constant [43 x i8] c"--- Running GamePipeline on game_world ---\00", align 1
@str_lit.9 = private unnamed_addr constant [39 x i8] c"--- Running UiPipeline on ui_world ---\00", align 1
@str_lit.10 = private unnamed_addr constant [74 x i8] c"Universal Contexts: Game and UI Worlds executed completely independently!\00", align 1
@str_lit.11 = private unnamed_addr constant [23 x i8] c"Press Enter to exit...\00", align 1

declare i32 @puts(ptr)

declare i32 @printf(ptr, ...)

declare ptr @realloc(ptr, i64)

declare i32 @getchar()

declare ptr @memcpy(ptr, ptr, i64)

declare ptr @memset(ptr, i32, i64)

declare ptr @malloc(i64)

declare void @free(ptr)

define i32 @world_get_or_create_archetype(ptr %0, i64 %1) {
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
  %existing_mask = load i64, ptr %mask_gep, align 8
  %is_match = icmp eq i64 %existing_mask, %1
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
  %alloc_bytes = mul i64 %new_cap64, 56
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
  store i64 %1, ptr %m_gep, align 8
  %cnt_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 1
  store i32 0, ptr %cnt_gep, align 4
  %cap_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 2
  store i32 0, ptr %cap_gep, align 4
  %ent_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 3
  store ptr null, ptr %ent_gep, align 8
  %cols_gep = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_elem, i32 0, i32 4
  %col_slot_0 = getelementptr inbounds [4 x ptr], ptr %cols_gep, i32 0, i32 0
  store ptr null, ptr %col_slot_0, align 8
  %col_slot_1 = getelementptr inbounds [4 x ptr], ptr %cols_gep, i32 0, i32 1
  store ptr null, ptr %col_slot_1, align 8
  %col_slot_2 = getelementptr inbounds [4 x ptr], ptr %cols_gep, i32 0, i32 2
  store ptr null, ptr %col_slot_2, align 8
  %col_slot_3 = getelementptr inbounds [4 x ptr], ptr %cols_gep, i32 0, i32 3
  store ptr null, ptr %col_slot_3, align 8
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
  %col_slot_ChildOf = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 0
  %cur_col_ChildOf = load ptr, ptr %col_slot_ChildOf, align 8
  %col_bytes_ChildOf = mul i64 %new_cap64, 4
  %new_col_ChildOf = call ptr @realloc(ptr %cur_col_ChildOf, i64 %col_bytes_ChildOf)
  store ptr %new_col_ChildOf, ptr %col_slot_ChildOf, align 8
  br label %skip_col_ChildOf

skip_col_ChildOf:                                 ; preds = %grow_col_ChildOf, %entry
  %has_Position = and i64 %arch_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %grow_col_Position, label %skip_col_Position

grow_col_Position:                                ; preds = %skip_col_ChildOf
  %col_slot_Position = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 1
  %cur_col_Position = load ptr, ptr %col_slot_Position, align 8
  %col_bytes_Position = mul i64 %new_cap64, 8
  %new_col_Position = call ptr @realloc(ptr %cur_col_Position, i64 %col_bytes_Position)
  store ptr %new_col_Position, ptr %col_slot_Position, align 8
  br label %skip_col_Position

skip_col_Position:                                ; preds = %grow_col_Position, %skip_col_ChildOf
  %has_Velocity = and i64 %arch_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %grow_col_Velocity, label %skip_col_Velocity

grow_col_Velocity:                                ; preds = %skip_col_Position
  %col_slot_Velocity = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 2
  %cur_col_Velocity = load ptr, ptr %col_slot_Velocity, align 8
  %col_bytes_Velocity = mul i64 %new_cap64, 8
  %new_col_Velocity = call ptr @realloc(ptr %cur_col_Velocity, i64 %col_bytes_Velocity)
  store ptr %new_col_Velocity, ptr %col_slot_Velocity, align 8
  br label %skip_col_Velocity

skip_col_Velocity:                                ; preds = %grow_col_Velocity, %skip_col_Position
  %has_Label = and i64 %arch_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %grow_col_Label, label %skip_col_Label

grow_col_Label:                                   ; preds = %skip_col_Velocity
  %col_slot_Label = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 3
  %cur_col_Label = load ptr, ptr %col_slot_Label, align 8
  %col_bytes_Label = mul i64 %new_cap64, 8
  %new_col_Label = call ptr @realloc(ptr %cur_col_Label, i64 %col_bytes_Label)
  store ptr %new_col_Label, ptr %col_slot_Label, align 8
  br label %skip_col_Label

skip_col_Label:                                   ; preds = %grow_col_Label, %skip_col_Velocity
  ret void
}

define ptr @ecs_create_world() {
entry:
  %raw_world = call ptr @malloc(i64 64)
  %0 = call ptr @memset(ptr %raw_world, i32 0, i64 64)
  %a0_init = call i32 @world_get_or_create_archetype(ptr %raw_world, i64 0)
  ret ptr %raw_world
}

define i32 @world_spawn(ptr %0) {
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
  %a0 = call i32 @world_get_or_create_archetype(ptr %0, i64 0)
  %tables_slot_sp = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %tables_sp = load ptr, ptr %tables_slot_sp, align 8
  %a0_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_sp, i32 %a0
  %cnt_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr, i32 0, i32 1
  %cur_cnt0 = load i32, ptr %cnt_slot0, align 4
  %cap_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr, i32 0, i32 2
  %cur_cap0 = load i32, ptr %cap_slot0, align 4
  %need_grow0 = icmp sge i32 %cur_cnt0, %cur_cap0
  br i1 %need_grow0, label %grow_a0, label %after_grow_a0

grow_a0:                                          ; preds = %assign_ent
  call void @world_grow_archetype(ptr %0, i32 %a0)
  br label %after_grow_a0

after_grow_a0:                                    ; preds = %grow_a0, %assign_ent
  %tables_sp2 = load ptr, ptr %tables_slot_sp, align 8
  %a0_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_sp2, i32 %a0
  %cnt_slot0_2 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr2, i32 0, i32 1
  %row = load i32, ptr %cnt_slot0_2, align 4
  %next_cnt0 = add i32 %row, 1
  store i32 %next_cnt0, ptr %cnt_slot0_2, align 4
  %ent_slot0 = getelementptr inbounds nuw %struct.Archetype, ptr %a0_ptr2, i32 0, i32 3
  %ent_raw0 = load ptr, ptr %ent_slot0, align 8
  %ent_elem0 = getelementptr inbounds i32, ptr %ent_raw0, i32 %row
  store i32 %cur_ent_count, ptr %ent_elem0, align 4
  %arch_arr_sp = load ptr, ptr %ent_arch_slot, align 8
  %e_arch_slot = getelementptr inbounds i32, ptr %arch_arr_sp, i32 %cur_ent_count
  store i32 %a0, ptr %e_arch_slot, align 4
  %row_arr_sp = load ptr, ptr %ent_row_slot, align 8
  %e_row_slot = getelementptr inbounds i32, ptr %row_arr_sp, i32 %cur_ent_count
  store i32 %row, ptr %e_row_slot, align 4
  ret i32 %cur_ent_count
}

define void @world_set_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 1
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 1
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 0
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
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_ChildOf(ptr %0, i32 %1, i32 %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 1
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 1
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 0
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
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_ChildOf(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 1
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -2
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
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
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position = icmp ne i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position, label %copy_rem_Position, label %skip_rem_Position

copy_rem_Position:                                ; preds = %after_grow_rem_arch
  %rem_src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i32 %cur_row_rem
  %rem_dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 1
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_Position, ptr %rem_src_elem_Position, i64 8)
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %after_grow_rem_arch
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity = icmp ne i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity, label %copy_rem_Velocity, label %skip_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_Position
  %rem_src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i32 %cur_row_rem
  %rem_dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 2
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Velocity, ptr %rem_src_elem_Velocity, i64 8)
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_Position
  %rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_has_rem_Label = icmp ne i64 %rem_has_Label, 0
  br i1 %is_has_rem_Label, label %copy_rem_Label, label %skip_rem_Label

copy_rem_Label:                                   ; preds = %skip_rem_Velocity
  %rem_src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %rem_src_raw_Label = load ptr, ptr %rem_src_col_Label, align 8
  %rem_src_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_src_raw_Label, i32 %cur_row_rem
  %rem_dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 3
  %rem_dst_raw_Label = load ptr, ptr %rem_dst_col_Label, align 8
  %rem_dst_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_dst_raw_Label, i32 %new_row_rem
  %4 = call ptr @memcpy(ptr %rem_dst_elem_Label, ptr %rem_src_elem_Label, i64 8)
  br label %skip_rem_Label

skip_rem_Label:                                   ; preds = %copy_rem_Label, %skip_rem_Velocity
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Label
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Label, %skip_rem_Label
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  %sw_rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Velocity = icmp ne i64 %sw_rem_has_Velocity, 0
  br i1 %is_sw_rem_Velocity, label %swap_rem_Velocity, label %skip_sw_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %last_row_rem
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %cur_row_rem
  %7 = call ptr @memcpy(ptr %sw_dst_rem_Velocity, ptr %sw_src_rem_Velocity, i64 8)
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  %sw_rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_sw_rem_Label = icmp ne i64 %sw_rem_has_Label, 0
  br i1 %is_sw_rem_Label, label %swap_rem_Label, label %skip_sw_rem_Label

swap_rem_Label:                                   ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %sw_raw_rem_Label = load ptr, ptr %sw_col_rem_Label, align 8
  %sw_src_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %last_row_rem
  %sw_dst_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %cur_row_rem
  %8 = call ptr @memcpy(ptr %sw_dst_rem_Label, ptr %sw_src_rem_Label, i64 8)
  br label %skip_sw_rem_Label

skip_sw_rem_Label:                                ; preds = %swap_rem_Label, %skip_sw_rem_Velocity
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_ChildOf(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_slot_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 1
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has
}

define void @world_set_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 2
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 2
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 1
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
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %7 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %11 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_Position(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 2
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 2
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 1
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
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %7 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %11 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_Position(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 2
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -3
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
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
  %rem_src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %rem_src_raw_ChildOf = load ptr, ptr %rem_src_col_ChildOf, align 8
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i32 %cur_row_rem
  %rem_dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 0
  %rem_dst_raw_ChildOf = load ptr, ptr %rem_dst_col_ChildOf, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_ChildOf, ptr %rem_src_elem_ChildOf, i64 4)
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity = icmp ne i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity, label %copy_rem_Velocity, label %skip_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i32 %cur_row_rem
  %rem_dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 2
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Velocity, ptr %rem_src_elem_Velocity, i64 8)
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_ChildOf
  %rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_has_rem_Label = icmp ne i64 %rem_has_Label, 0
  br i1 %is_has_rem_Label, label %copy_rem_Label, label %skip_rem_Label

copy_rem_Label:                                   ; preds = %skip_rem_Velocity
  %rem_src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %rem_src_raw_Label = load ptr, ptr %rem_src_col_Label, align 8
  %rem_src_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_src_raw_Label, i32 %cur_row_rem
  %rem_dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 3
  %rem_dst_raw_Label = load ptr, ptr %rem_dst_col_Label, align 8
  %rem_dst_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_dst_raw_Label, i32 %new_row_rem
  %4 = call ptr @memcpy(ptr %rem_dst_elem_Label, ptr %rem_src_elem_Label, i64 8)
  br label %skip_rem_Label

skip_rem_Label:                                   ; preds = %copy_rem_Label, %skip_rem_Velocity
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Label
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Label, %skip_rem_Label
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  %sw_rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Velocity = icmp ne i64 %sw_rem_has_Velocity, 0
  br i1 %is_sw_rem_Velocity, label %swap_rem_Velocity, label %skip_sw_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %last_row_rem
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %cur_row_rem
  %7 = call ptr @memcpy(ptr %sw_dst_rem_Velocity, ptr %sw_src_rem_Velocity, i64 8)
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  %sw_rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_sw_rem_Label = icmp ne i64 %sw_rem_has_Label, 0
  br i1 %is_sw_rem_Label, label %swap_rem_Label, label %skip_sw_rem_Label

swap_rem_Label:                                   ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %sw_raw_rem_Label = load ptr, ptr %sw_col_rem_Label, align 8
  %sw_src_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %last_row_rem
  %sw_dst_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %cur_row_rem
  %8 = call ptr @memcpy(ptr %sw_dst_rem_Label, ptr %sw_src_rem_Label, i64 8)
  br label %skip_sw_rem_Label

skip_sw_rem_Label:                                ; preds = %swap_rem_Label, %skip_sw_rem_Velocity
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_Position(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_slot_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 2
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has
}

define void @world_set_Velocity(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 4
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 4
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 2
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Velocity, ptr %final_col_raw, i32 %final_row
  %vx_gep = getelementptr inbounds nuw %struct.Velocity, ptr %final_elem, i32 0, i32 0
  store float %2, ptr %vx_gep, align 4
  %vy_gep = getelementptr inbounds nuw %struct.Velocity, ptr %final_elem, i32 0, i32 1
  store float %3, ptr %vy_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %7 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %11 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_Velocity(ptr %0, i32 %1, float %2, float %3) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 4
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 4
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 2
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Velocity, ptr %final_col_raw, i32 %final_row
  %vx_gep = getelementptr inbounds nuw %struct.Velocity, ptr %final_elem, i32 0, i32 0
  store float %2, ptr %vx_gep, align 4
  %vy_gep = getelementptr inbounds nuw %struct.Velocity, ptr %final_elem, i32 0, i32 1
  store float %3, ptr %vy_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %7 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %11 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_Velocity(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 4
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -5
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
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
  %rem_src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %rem_src_raw_ChildOf = load ptr, ptr %rem_src_col_ChildOf, align 8
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i32 %cur_row_rem
  %rem_dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 0
  %rem_dst_raw_ChildOf = load ptr, ptr %rem_dst_col_ChildOf, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_ChildOf, ptr %rem_src_elem_ChildOf, i64 4)
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position = icmp ne i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position, label %copy_rem_Position, label %skip_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i32 %cur_row_rem
  %rem_dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 1
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Position, ptr %rem_src_elem_Position, i64 8)
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_has_rem_Label = icmp ne i64 %rem_has_Label, 0
  br i1 %is_has_rem_Label, label %copy_rem_Label, label %skip_rem_Label

copy_rem_Label:                                   ; preds = %skip_rem_Position
  %rem_src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %rem_src_raw_Label = load ptr, ptr %rem_src_col_Label, align 8
  %rem_src_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_src_raw_Label, i32 %cur_row_rem
  %rem_dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 3
  %rem_dst_raw_Label = load ptr, ptr %rem_dst_col_Label, align 8
  %rem_dst_elem_Label = getelementptr inbounds %struct.Label, ptr %rem_dst_raw_Label, i32 %new_row_rem
  %4 = call ptr @memcpy(ptr %rem_dst_elem_Label, ptr %rem_src_elem_Label, i64 8)
  br label %skip_rem_Label

skip_rem_Label:                                   ; preds = %copy_rem_Label, %skip_rem_Position
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Label
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Label, %skip_rem_Label
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  %sw_rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Velocity = icmp ne i64 %sw_rem_has_Velocity, 0
  br i1 %is_sw_rem_Velocity, label %swap_rem_Velocity, label %skip_sw_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %last_row_rem
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %cur_row_rem
  %7 = call ptr @memcpy(ptr %sw_dst_rem_Velocity, ptr %sw_src_rem_Velocity, i64 8)
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  %sw_rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_sw_rem_Label = icmp ne i64 %sw_rem_has_Label, 0
  br i1 %is_sw_rem_Label, label %swap_rem_Label, label %skip_sw_rem_Label

swap_rem_Label:                                   ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %sw_raw_rem_Label = load ptr, ptr %sw_col_rem_Label, align 8
  %sw_src_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %last_row_rem
  %sw_dst_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %cur_row_rem
  %8 = call ptr @memcpy(ptr %sw_dst_rem_Label, ptr %sw_src_rem_Label, i64 8)
  br label %skip_sw_rem_Label

skip_sw_rem_Label:                                ; preds = %swap_rem_Label, %skip_sw_rem_Velocity
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_Velocity(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_slot_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 4
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has
}

define void @world_set_Label(ptr %0, i32 %1, ptr %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 8
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 3
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Label, ptr %final_col_raw, i32 %final_row
  %text_gep = getelementptr inbounds nuw %struct.Label, ptr %final_elem, i32 0, i32 0
  store ptr %2, ptr %text_gep, align 8
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_add_Label(ptr %0, i32 %1, ptr %2) {
entry:
  %target_arch = alloca i32, align 4
  %target_row = alloca i32, align 4
  %ent_arch_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_set = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i32 %1
  %cur_arch_idx = load i32, ptr %ent_arch_slot, align 4
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i32 %1
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i32 %cur_arch_idx
  %cur_mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %cur_mask = load i64, ptr %cur_mask_slot, align 8
  %has_bit = and i64 %cur_mask, 8
  %already_has = icmp ne i64 %has_bit, 0
  br i1 %already_has, label %in_place_update, label %transition

in_place_update:                                  ; preds = %entry
  store i32 %cur_arch_idx, ptr %target_arch, align 4
  store i32 %cur_row, ptr %target_row, align 4
  br label %store_fields

transition:                                       ; preds = %entry
  %new_mask = or i64 %cur_mask, 8
  %new_arch_idx = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask)
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
  %final_col_slot = getelementptr inbounds [4 x ptr], ptr %final_cols, i32 0, i32 3
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Label, ptr %final_col_raw, i32 %final_row
  %text_gep = getelementptr inbounds nuw %struct.Label, ptr %final_elem, i32 0, i32 0
  store ptr %2, ptr %text_gep, align 8
  ret void

grow_new_arch:                                    ; preds = %transition
  call void @world_grow_archetype(ptr %0, i32 %new_arch_idx)
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %transition
  %tables_tr2 = load ptr, ptr %tables_slot_set, align 8
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i32 %cur_arch_idx
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
  %src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %src_raw_ChildOf = load ptr, ptr %src_col_ChildOf, align 8
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i32 %cur_row
  %dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 0
  %dst_raw_ChildOf = load ptr, ptr %dst_col_ChildOf, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i32 %new_row
  %3 = call ptr @memcpy(ptr %dst_elem_ChildOf, ptr %src_elem_ChildOf, i64 4)
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position = icmp ne i64 %has_Position, 0
  br i1 %is_has_Position, label %copy_Position, label %skip_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i32 %cur_row
  %dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 1
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i32 %new_row
  %4 = call ptr @memcpy(ptr %dst_elem_Position, ptr %src_elem_Position, i64 8)
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity = icmp ne i64 %has_Velocity, 0
  br i1 %is_has_Velocity, label %copy_Velocity, label %skip_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i32 %cur_row
  %dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 2
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i32 %new_row
  %5 = call ptr @memcpy(ptr %dst_elem_Velocity, ptr %src_elem_Velocity, i64 8)
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_Label = and i64 %cur_mask, 8
  %is_has_Label = icmp ne i64 %has_Label, 0
  br i1 %is_has_Label, label %copy_Label, label %skip_Label

copy_Label:                                       ; preds = %skip_Velocity
  %src_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %src_raw_Label = load ptr, ptr %src_col_Label, align 8
  %src_elem_Label = getelementptr inbounds %struct.Label, ptr %src_raw_Label, i32 %cur_row
  %dst_col_Label = getelementptr inbounds [4 x ptr], ptr %new_cols_arr, i32 0, i32 3
  %dst_raw_Label = load ptr, ptr %dst_col_Label, align 8
  %dst_elem_Label = getelementptr inbounds %struct.Label, ptr %dst_raw_Label, i32 %new_row
  %6 = call ptr @memcpy(ptr %dst_elem_Label, ptr %src_elem_Label, i64 8)
  br label %skip_Label

skip_Label:                                       ; preds = %copy_Label, %skip_Velocity
  %cur_cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 1
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = sub i32 %cur_arch_count, 1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Label
  %cur_ent_sr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr2, i32 0, i32 3
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %last_row
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i32 %cur_row
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  %has_sw_ChildOf = and i64 %cur_mask, 1
  %is_has_sw_ChildOf = icmp ne i64 %has_sw_ChildOf, 0
  br i1 %is_has_sw_ChildOf, label %swap_ChildOf, label %skip_sw_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Label, %skip_Label
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
  %sw_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 0
  %sw_raw_ChildOf = load ptr, ptr %sw_col_ChildOf, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %last_row
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i32 %cur_row
  %7 = call ptr @memcpy(ptr %sw_dst_ChildOf, ptr %sw_src_ChildOf, i64 4)
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  %has_sw_Position = and i64 %cur_mask, 2
  %is_has_sw_Position = icmp ne i64 %has_sw_Position, 0
  br i1 %is_has_sw_Position, label %swap_Position, label %skip_sw_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 1
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %last_row
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i32 %cur_row
  %8 = call ptr @memcpy(ptr %sw_dst_Position, ptr %sw_src_Position, i64 8)
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  %has_sw_Velocity = and i64 %cur_mask, 4
  %is_has_sw_Velocity = icmp ne i64 %has_sw_Velocity, 0
  br i1 %is_has_sw_Velocity, label %swap_Velocity, label %skip_sw_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 2
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %last_row
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i32 %cur_row
  %9 = call ptr @memcpy(ptr %sw_dst_Velocity, ptr %sw_src_Velocity, i64 8)
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  %has_sw_Label = and i64 %cur_mask, 8
  %is_has_sw_Label = icmp ne i64 %has_sw_Label, 0
  br i1 %is_has_sw_Label, label %swap_Label, label %skip_sw_Label

swap_Label:                                       ; preds = %skip_sw_Velocity
  %sw_col_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_arr, i32 0, i32 3
  %sw_raw_Label = load ptr, ptr %sw_col_Label, align 8
  %sw_src_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %last_row
  %sw_dst_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_Label, i32 %cur_row
  %10 = call ptr @memcpy(ptr %sw_dst_Label, ptr %sw_src_Label, i64 8)
  br label %skip_sw_Label

skip_sw_Label:                                    ; preds = %swap_Label, %skip_sw_Velocity
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i32 %moved_e
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

define void @world_remove_Label(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %ent_row_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 6
  %tables_slot_rem = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i32 %1
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i32 %1
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i32 %cur_arch_rem
  %cur_mask_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem, i32 0, i32 0
  %cur_mask_val_rem = load i64, ptr %cur_mask_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 8
  %has_comp_rem = icmp ne i64 %rem_has_bit, 0
  br i1 %has_comp_rem, label %do_remove, label %exit_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -9
  %new_arch_rem = call i32 @world_get_or_create_archetype(ptr %0, i64 %new_mask_rem)
  %tables_rem_tr1 = load ptr, ptr %tables_slot_rem, align 8
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i32 %new_arch_rem
  %cnt_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 1
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw %struct.Archetype, ptr %new_arch_ptr_rem1, i32 0, i32 2
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem = icmp sge i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem, label %grow_rem_arch, label %after_grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
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
  %rem_src_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %rem_src_raw_ChildOf = load ptr, ptr %rem_src_col_ChildOf, align 8
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i32 %cur_row_rem
  %rem_dst_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 0
  %rem_dst_raw_ChildOf = load ptr, ptr %rem_dst_col_ChildOf, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i32 %new_row_rem
  %2 = call ptr @memcpy(ptr %rem_dst_elem_ChildOf, ptr %rem_src_elem_ChildOf, i64 4)
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position = icmp ne i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position, label %copy_rem_Position, label %skip_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i32 %cur_row_rem
  %rem_dst_col_Position = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 1
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i32 %new_row_rem
  %3 = call ptr @memcpy(ptr %rem_dst_elem_Position, ptr %rem_src_elem_Position, i64 8)
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity = icmp ne i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity, label %copy_rem_Velocity, label %skip_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_Position
  %rem_src_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i32 %cur_row_rem
  %rem_dst_col_Velocity = getelementptr inbounds [4 x ptr], ptr %new_cols_rem, i32 0, i32 2
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i32 %new_row_rem
  %4 = call ptr @memcpy(ptr %rem_dst_elem_Velocity, ptr %rem_src_elem_Velocity, i64 8)
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_Position
  %cnt_slot_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 1
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = sub i32 %cnt_rem, 1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Velocity
  %ent_sr_rem = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr_rem2, i32 0, i32 3
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %last_row_rem
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i32 %cur_row_rem
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_sw_rem_ChildOf = icmp ne i64 %sw_rem_has_ChildOf, 0
  br i1 %is_sw_rem_ChildOf, label %swap_rem_ChildOf, label %skip_sw_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Label, %skip_rem_Velocity
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i32 %1
  store i32 %new_arch_rem, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i32 %1
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_col_rem_ChildOf = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 0
  %sw_raw_rem_ChildOf = load ptr, ptr %sw_col_rem_ChildOf, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %last_row_rem
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i32 %cur_row_rem
  %5 = call ptr @memcpy(ptr %sw_dst_rem_ChildOf, ptr %sw_src_rem_ChildOf, i64 4)
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  %sw_rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_sw_rem_Position = icmp ne i64 %sw_rem_has_Position, 0
  br i1 %is_sw_rem_Position, label %swap_rem_Position, label %skip_sw_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 1
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %last_row_rem
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i32 %cur_row_rem
  %6 = call ptr @memcpy(ptr %sw_dst_rem_Position, ptr %sw_src_rem_Position, i64 8)
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  %sw_rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_sw_rem_Velocity = icmp ne i64 %sw_rem_has_Velocity, 0
  br i1 %is_sw_rem_Velocity, label %swap_rem_Velocity, label %skip_sw_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 2
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %last_row_rem
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i32 %cur_row_rem
  %7 = call ptr @memcpy(ptr %sw_dst_rem_Velocity, ptr %sw_src_rem_Velocity, i64 8)
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  %sw_rem_has_Label = and i64 %cur_mask_val_rem, 8
  %is_sw_rem_Label = icmp ne i64 %sw_rem_has_Label, 0
  br i1 %is_sw_rem_Label, label %swap_rem_Label, label %skip_sw_rem_Label

swap_rem_Label:                                   ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_Label = getelementptr inbounds [4 x ptr], ptr %cur_cols_rem, i32 0, i32 3
  %sw_raw_rem_Label = load ptr, ptr %sw_col_rem_Label, align 8
  %sw_src_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %last_row_rem
  %sw_dst_rem_Label = getelementptr inbounds %struct.Label, ptr %sw_raw_rem_Label, i32 %cur_row_rem
  %8 = call ptr @memcpy(ptr %sw_dst_rem_Label, ptr %sw_src_rem_Label, i64 8)
  br label %skip_sw_rem_Label

skip_sw_rem_Label:                                ; preds = %swap_rem_Label, %skip_sw_rem_Velocity
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i32 %moved_e_rem
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

define i1 @world_has_Label(ptr %0, i32 %1) {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 5
  %tables_slot_has = getelementptr inbounds nuw %struct.EcsWorld, ptr %0, i32 0, i32 2
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i32 %1
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %arch_ptr_has = getelementptr inbounds %struct.Archetype, ptr %tables_has, i32 %cur_arch_idx_has
  %mask_slot_has = getelementptr inbounds nuw %struct.Archetype, ptr %arch_ptr_has, i32 0, i32 0
  %arch_mask_has = load i64, ptr %mask_slot_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 8
  %res_has = icmp ne i64 %bit_and_has, 0
  ret i1 %res_has
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
  %temp_Position = alloca %struct.Position, align 8
  %temp_Velocity = alloca %struct.Velocity, align 8
  %temp_Label = alloca %struct.Label, align 8
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
  %co_slot = getelementptr inbounds [4 x ptr], ptr %cols_sh, i32 0, i32 0
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

skip_swap_row:                                    ; preds = %skip_sw_sh_Label, %sort_inner_body
  %next_j = add i32 %cur_sort_j, 1
  store i32 %next_j, ptr %sort_j, align 4
  br label %sort_inner_cond

sw_sh_ChildOf:                                    ; preds = %do_swap_row
  %sw_sh_col_ChildOf = getelementptr inbounds [4 x ptr], ptr %cols_sh, i32 0, i32 0
  %sw_sh_raw_ChildOf = load ptr, ptr %sw_sh_col_ChildOf, align 8
  %elem_i_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i32 %cur_sort_i
  %elem_j_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i32 %cur_sort_j
  %1 = call ptr @memcpy(ptr %temp_ChildOf, ptr %elem_i_ChildOf, i64 4)
  %2 = call ptr @memcpy(ptr %elem_i_ChildOf, ptr %elem_j_ChildOf, i64 4)
  %3 = call ptr @memcpy(ptr %elem_j_ChildOf, ptr %temp_ChildOf, i64 4)
  br label %skip_sw_sh_ChildOf

skip_sw_sh_ChildOf:                               ; preds = %sw_sh_ChildOf, %do_swap_row
  %sw_co_has_Position = and i64 %m_val, 2
  %is_sw_co_Position = icmp ne i64 %sw_co_has_Position, 0
  br i1 %is_sw_co_Position, label %sw_sh_Position, label %skip_sw_sh_Position

sw_sh_Position:                                   ; preds = %skip_sw_sh_ChildOf
  %sw_sh_col_Position = getelementptr inbounds [4 x ptr], ptr %cols_sh, i32 0, i32 1
  %sw_sh_raw_Position = load ptr, ptr %sw_sh_col_Position, align 8
  %elem_i_Position = getelementptr inbounds %struct.Position, ptr %sw_sh_raw_Position, i32 %cur_sort_i
  %elem_j_Position = getelementptr inbounds %struct.Position, ptr %sw_sh_raw_Position, i32 %cur_sort_j
  %4 = call ptr @memcpy(ptr %temp_Position, ptr %elem_i_Position, i64 8)
  %5 = call ptr @memcpy(ptr %elem_i_Position, ptr %elem_j_Position, i64 8)
  %6 = call ptr @memcpy(ptr %elem_j_Position, ptr %temp_Position, i64 8)
  br label %skip_sw_sh_Position

skip_sw_sh_Position:                              ; preds = %sw_sh_Position, %skip_sw_sh_ChildOf
  %sw_co_has_Velocity = and i64 %m_val, 4
  %is_sw_co_Velocity = icmp ne i64 %sw_co_has_Velocity, 0
  br i1 %is_sw_co_Velocity, label %sw_sh_Velocity, label %skip_sw_sh_Velocity

sw_sh_Velocity:                                   ; preds = %skip_sw_sh_Position
  %sw_sh_col_Velocity = getelementptr inbounds [4 x ptr], ptr %cols_sh, i32 0, i32 2
  %sw_sh_raw_Velocity = load ptr, ptr %sw_sh_col_Velocity, align 8
  %elem_i_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_sh_raw_Velocity, i32 %cur_sort_i
  %elem_j_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_sh_raw_Velocity, i32 %cur_sort_j
  %7 = call ptr @memcpy(ptr %temp_Velocity, ptr %elem_i_Velocity, i64 8)
  %8 = call ptr @memcpy(ptr %elem_i_Velocity, ptr %elem_j_Velocity, i64 8)
  %9 = call ptr @memcpy(ptr %elem_j_Velocity, ptr %temp_Velocity, i64 8)
  br label %skip_sw_sh_Velocity

skip_sw_sh_Velocity:                              ; preds = %sw_sh_Velocity, %skip_sw_sh_Position
  %sw_co_has_Label = and i64 %m_val, 8
  %is_sw_co_Label = icmp ne i64 %sw_co_has_Label, 0
  br i1 %is_sw_co_Label, label %sw_sh_Label, label %skip_sw_sh_Label

sw_sh_Label:                                      ; preds = %skip_sw_sh_Velocity
  %sw_sh_col_Label = getelementptr inbounds [4 x ptr], ptr %cols_sh, i32 0, i32 3
  %sw_sh_raw_Label = load ptr, ptr %sw_sh_col_Label, align 8
  %elem_i_Label = getelementptr inbounds %struct.Label, ptr %sw_sh_raw_Label, i32 %cur_sort_i
  %elem_j_Label = getelementptr inbounds %struct.Label, ptr %sw_sh_raw_Label, i32 %cur_sort_j
  %10 = call ptr @memcpy(ptr %temp_Label, ptr %elem_i_Label, i64 8)
  %11 = call ptr @memcpy(ptr %elem_i_Label, ptr %elem_j_Label, i64 8)
  %12 = call ptr @memcpy(ptr %elem_j_Label, ptr %temp_Label, i64 8)
  br label %skip_sw_sh_Label

skip_sw_sh_Label:                                 ; preds = %sw_sh_Label, %skip_sw_sh_Velocity
  br label %skip_swap_row
}

define void @system_MoveEntities(ptr %world) {
entry:
  %world_arch_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 0
  %num_archs = load i32, ptr %world_arch_count_slot, align 4
  %arch_idx = alloca i32, align 4
  store i32 0, ptr %arch_idx, align 4
  br label %arch_cond

arch_cond:                                        ; preds = %next_arch, %entry
  %cur_arch_idx = load i32, ptr %arch_idx, align 4
  %has_more_archs = icmp slt i32 %cur_arch_idx, %num_archs
  br i1 %has_more_archs, label %arch_body, label %sys_exit

arch_body:                                        ; preds = %arch_cond
  %world_arch_tables_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 2
  %tables_base = load ptr, ptr %world_arch_tables_slot, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_base, i32 %cur_arch_idx
  %mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %arch_mask = load i64, ptr %mask_slot, align 8
  %and_mask = and i64 %arch_mask, 6
  %is_match = icmp eq i64 %and_mask, 6
  br i1 %is_match, label %check_count, label %next_arch

next_arch:                                        ; preds = %ent_loop_cond, %check_count, %arch_body
  %next_arch_idx = add i32 %cur_arch_idx, 1
  store i32 %next_arch_idx, ptr %arch_idx, align 4
  br label %arch_cond

sys_exit:                                         ; preds = %arch_cond
  ret void

check_count:                                      ; preds = %arch_body
  %cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 1
  %arch_count = load i32, ptr %cnt_slot, align 4
  %has_entities = icmp sgt i32 %arch_count, 0
  br i1 %has_entities, label %ent_loop_header, label %next_arch

ent_loop_header:                                  ; preds = %check_count
  %row = alloca i32, align 4
  store i32 0, ptr %row, align 4
  br label %ent_loop_cond

ent_loop_cond:                                    ; preds = %ent_loop_body, %ent_loop_header
  %cur_row = load i32, ptr %row, align 4
  %has_more_rows = icmp slt i32 %cur_row, %arch_count
  br i1 %has_more_rows, label %ent_loop_body, label %next_arch

ent_loop_body:                                    ; preds = %ent_loop_cond
  %cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 4
  %pos_col_slot = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 1
  %pos_raw = load ptr, ptr %pos_col_slot, align 8
  %pos_elem = getelementptr inbounds %struct.Position, ptr %pos_raw, i32 %cur_row
  %vel_col_slot = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 2
  %vel_raw = load ptr, ptr %vel_col_slot, align 8
  %vel_elem = getelementptr inbounds %struct.Velocity, ptr %vel_raw, i32 %cur_row
  %vel_vx = getelementptr inbounds nuw %struct.Velocity, ptr %vel_elem, i32 0, i32 0
  %vx_val = load float, ptr %vel_vx, align 4
  %pos_x_gep = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 0
  %cur_fld = load float, ptr %pos_x_gep, align 4
  %fadd = fadd float %cur_fld, %vx_val
  store float %fadd, ptr %pos_x_gep, align 4
  %vel_vy = getelementptr inbounds nuw %struct.Velocity, ptr %vel_elem, i32 0, i32 1
  %vy_val = load float, ptr %vel_vy, align 4
  %pos_y_gep = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 1
  %cur_fld1 = load float, ptr %pos_y_gep, align 4
  %fadd2 = fadd float %cur_fld1, %vy_val
  store float %fadd2, ptr %pos_y_gep, align 4
  %puts_call = call i32 @puts(ptr @str_lit)
  %pos_x = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 0
  %x_val = load float, ptr %pos_x, align 4
  %f_to_d = fpext float %x_val to double
  %printf_call = call i32 (ptr, ...) @printf(ptr @fmt_f, double %f_to_d)
  %pos_y = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 1
  %y_val = load float, ptr %pos_y, align 4
  %f_to_d3 = fpext float %y_val to double
  %printf_call4 = call i32 (ptr, ...) @printf(ptr @fmt_f.1, double %f_to_d3)
  %next_row = add i32 %cur_row, 1
  store i32 %next_row, ptr %row, align 4
  br label %ent_loop_cond
}

define void @system_RenderLabels(ptr %world) {
entry:
  %world_arch_count_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 0
  %num_archs = load i32, ptr %world_arch_count_slot, align 4
  %arch_idx = alloca i32, align 4
  store i32 0, ptr %arch_idx, align 4
  br label %arch_cond

arch_cond:                                        ; preds = %next_arch, %entry
  %cur_arch_idx = load i32, ptr %arch_idx, align 4
  %has_more_archs = icmp slt i32 %cur_arch_idx, %num_archs
  br i1 %has_more_archs, label %arch_body, label %sys_exit

arch_body:                                        ; preds = %arch_cond
  %world_arch_tables_slot = getelementptr inbounds nuw %struct.EcsWorld, ptr %world, i32 0, i32 2
  %tables_base = load ptr, ptr %world_arch_tables_slot, align 8
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_base, i32 %cur_arch_idx
  %mask_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 0
  %arch_mask = load i64, ptr %mask_slot, align 8
  %and_mask = and i64 %arch_mask, 10
  %is_match = icmp eq i64 %and_mask, 10
  br i1 %is_match, label %check_count, label %next_arch

next_arch:                                        ; preds = %ent_loop_cond, %check_count, %arch_body
  %next_arch_idx = add i32 %cur_arch_idx, 1
  store i32 %next_arch_idx, ptr %arch_idx, align 4
  br label %arch_cond

sys_exit:                                         ; preds = %arch_cond
  ret void

check_count:                                      ; preds = %arch_body
  %cnt_slot = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 1
  %arch_count = load i32, ptr %cnt_slot, align 4
  %has_entities = icmp sgt i32 %arch_count, 0
  br i1 %has_entities, label %ent_loop_header, label %next_arch

ent_loop_header:                                  ; preds = %check_count
  %row = alloca i32, align 4
  store i32 0, ptr %row, align 4
  br label %ent_loop_cond

ent_loop_cond:                                    ; preds = %ent_loop_body, %ent_loop_header
  %cur_row = load i32, ptr %row, align 4
  %has_more_rows = icmp slt i32 %cur_row, %arch_count
  br i1 %has_more_rows, label %ent_loop_body, label %next_arch

ent_loop_body:                                    ; preds = %ent_loop_cond
  %cols_arr = getelementptr inbounds nuw %struct.Archetype, ptr %cur_arch_ptr, i32 0, i32 4
  %lbl_col_slot = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 3
  %lbl_raw = load ptr, ptr %lbl_col_slot, align 8
  %lbl_elem = getelementptr inbounds %struct.Label, ptr %lbl_raw, i32 %cur_row
  %pos_col_slot = getelementptr inbounds [4 x ptr], ptr %cols_arr, i32 0, i32 1
  %pos_raw = load ptr, ptr %pos_col_slot, align 8
  %pos_elem = getelementptr inbounds %struct.Position, ptr %pos_raw, i32 %cur_row
  %puts_call = call i32 @puts(ptr @str_lit.2)
  %lbl_text = getelementptr inbounds nuw %struct.Label, ptr %lbl_elem, i32 0, i32 0
  %text_val = load ptr, ptr %lbl_text, align 8
  %puts_call1 = call i32 @puts(ptr %text_val)
  %pos_x = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 0
  %x_val = load float, ptr %pos_x, align 4
  %f_to_d = fpext float %x_val to double
  %printf_call = call i32 (ptr, ...) @printf(ptr @fmt_f.3, double %f_to_d)
  %pos_y = getelementptr inbounds nuw %struct.Position, ptr %pos_elem, i32 0, i32 1
  %y_val = load float, ptr %pos_y, align 4
  %f_to_d2 = fpext float %y_val to double
  %printf_call3 = call i32 (ptr, ...) @printf(ptr @fmt_f.4, double %f_to_d2)
  %next_row = add i32 %cur_row, 1
  store i32 %next_row, ptr %row, align 4
  br label %ent_loop_cond
}

define void @pipeline_GamePipeline(ptr %world) {
entry:
  call void @system_MoveEntities(ptr %world)
  ret void
}

define void @pipeline_UiPipeline(ptr %world) {
entry:
  call void @system_RenderLabels(ptr %world)
  ret void
}

define i32 @main() {
entry:
  %quit_btn = alloca i32, align 4
  %title_btn = alloca i32, align 4
  %ui_world = alloca ptr, align 8
  %enemy = alloca i32, align 4
  %player = alloca i32, align 4
  %game_world = alloca ptr, align 8
  %puts_call = call i32 @puts(ptr @str_lit.5)
  %new_world = call ptr @ecs_create_world()
  store ptr %new_world, ptr %game_world, align 8
  %game_world1 = load ptr, ptr %game_world, align 8
  %spawn_call = call i32 @world_spawn(ptr %game_world1)
  store i32 %spawn_call, ptr %player, align 4
  %game_world2 = load ptr, ptr %game_world, align 8
  %player3 = load i32, ptr %player, align 4
  call void @world_set_Position(ptr %game_world2, i32 %player3, float 1.000000e+01, float 2.000000e+01)
  %game_world4 = load ptr, ptr %game_world, align 8
  %player5 = load i32, ptr %player, align 4
  call void @world_set_Velocity(ptr %game_world4, i32 %player5, float 1.500000e+00, float 2.500000e+00)
  %game_world6 = load ptr, ptr %game_world, align 8
  %spawn_call7 = call i32 @world_spawn(ptr %game_world6)
  store i32 %spawn_call7, ptr %enemy, align 4
  %game_world8 = load ptr, ptr %game_world, align 8
  %enemy9 = load i32, ptr %enemy, align 4
  call void @world_set_Position(ptr %game_world8, i32 %enemy9, float 1.000000e+02, float 2.000000e+02)
  %game_world10 = load ptr, ptr %game_world, align 8
  %enemy11 = load i32, ptr %enemy, align 4
  call void @world_set_Velocity(ptr %game_world10, i32 %enemy11, float -5.000000e+00, float 0.000000e+00)
  %new_world12 = call ptr @ecs_create_world()
  store ptr %new_world12, ptr %ui_world, align 8
  %ui_world13 = load ptr, ptr %ui_world, align 8
  %spawn_call14 = call i32 @world_spawn(ptr %ui_world13)
  store i32 %spawn_call14, ptr %title_btn, align 4
  %ui_world15 = load ptr, ptr %ui_world, align 8
  %title_btn16 = load i32, ptr %title_btn, align 4
  call void @world_set_Position(ptr %ui_world15, i32 %title_btn16, float 0.000000e+00, float 0.000000e+00)
  %ui_world17 = load ptr, ptr %ui_world, align 8
  %title_btn18 = load i32, ptr %title_btn, align 4
  call void @world_set_Label(ptr %ui_world17, i32 %title_btn18, ptr @str_lit.6)
  %ui_world19 = load ptr, ptr %ui_world, align 8
  %spawn_call20 = call i32 @world_spawn(ptr %ui_world19)
  store i32 %spawn_call20, ptr %quit_btn, align 4
  %ui_world21 = load ptr, ptr %ui_world, align 8
  %quit_btn22 = load i32, ptr %quit_btn, align 4
  call void @world_set_Position(ptr %ui_world21, i32 %quit_btn22, float 0.000000e+00, float 5.000000e+01)
  %ui_world23 = load ptr, ptr %ui_world, align 8
  %quit_btn24 = load i32, ptr %quit_btn, align 4
  call void @world_set_Label(ptr %ui_world23, i32 %quit_btn24, ptr @str_lit.7)
  %puts_call25 = call i32 @puts(ptr @str_lit.8)
  %game_world26 = load ptr, ptr %game_world, align 8
  call void @pipeline_GamePipeline(ptr %game_world26)
  %puts_call27 = call i32 @puts(ptr @str_lit.9)
  %ui_world28 = load ptr, ptr %ui_world, align 8
  call void @pipeline_UiPipeline(ptr %ui_world28)
  %puts_call29 = call i32 @puts(ptr @str_lit.10)
  %puts_call30 = call i32 @puts(ptr @str_lit.11)
  %key_input = call i32 @getchar()
  ret i32 0
}
