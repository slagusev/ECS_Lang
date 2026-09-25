; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.res.Time = type { float }
%struct.ChildOf = type { i32 }
%struct.Position = type { float, float }
%struct.Velocity = type { float, float }

@res_Time = global %struct.res.Time zeroinitializer
@arch_count = global i32 0
@arch_cap = global i32 0
@arch_col_ChildOf = global ptr null
@arch_col_Position = global ptr null
@arch_col_Velocity = global ptr null
@str_lit = private unnamed_addr constant [17 x i8] c"Entity Position:\00", align 1
@fmt_f = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@fmt_f.1 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.2 = private unnamed_addr constant [42 x i8] c"--- Starting ECS Archetype Simulation ---\00", align 1
@str_lit.3 = private unnamed_addr constant [16 x i8] c"=== Frame 1 ===\00", align 1
@str_lit.4 = private unnamed_addr constant [16 x i8] c"=== Frame 2 ===\00", align 1
@str_lit.5 = private unnamed_addr constant [42 x i8] c"--- Simulation Finished Successfully! ---\00", align 1
@str_lit.6 = private unnamed_addr constant [23 x i8] c"Press Enter to exit...\00", align 1

declare i32 @puts(ptr)

declare i32 @printf(ptr, ...)

declare ptr @realloc(ptr, i64)

declare i32 @getchar()

define i32 @world_spawn() {
entry:
  %cur_count = load i32, ptr @arch_count, align 4
  %cur_cap = load i32, ptr @arch_cap, align 4
  %need_grow = icmp sge i32 %cur_count, %cur_cap
  br i1 %need_grow, label %grow, label %after_grow

grow:                                             ; preds = %entry
  %cap_is_zero = icmp eq i32 %cur_cap, 0
  %double_cap = mul i32 %cur_cap, 2
  %new_cap = select i1 %cap_is_zero, i32 32, i32 %double_cap
  store i32 %new_cap, ptr @arch_cap, align 4
  %new_cap64 = zext i32 %new_cap to i64
  %ChildOf_ptr = load ptr, ptr @arch_col_ChildOf, align 8
  %alloc_bytes = mul i64 %new_cap64, 32
  %realloc_call = call ptr @realloc(ptr %ChildOf_ptr, i64 %alloc_bytes)
  store ptr %realloc_call, ptr @arch_col_ChildOf, align 8
  %Position_ptr = load ptr, ptr @arch_col_Position, align 8
  %alloc_bytes1 = mul i64 %new_cap64, 32
  %realloc_call2 = call ptr @realloc(ptr %Position_ptr, i64 %alloc_bytes1)
  store ptr %realloc_call2, ptr @arch_col_Position, align 8
  %Velocity_ptr = load ptr, ptr @arch_col_Velocity, align 8
  %alloc_bytes3 = mul i64 %new_cap64, 32
  %realloc_call4 = call ptr @realloc(ptr %Velocity_ptr, i64 %alloc_bytes3)
  store ptr %realloc_call4, ptr @arch_col_Velocity, align 8
  br label %after_grow

after_grow:                                       ; preds = %grow, %entry
  %new_count = add i32 %cur_count, 1
  store i32 %new_count, ptr @arch_count, align 4
  ret i32 %cur_count
}

define void @world_set_ChildOf(i32 %0, i32 %1) {
entry:
  %col_base = load ptr, ptr @arch_col_ChildOf, align 8
  %elem_ptr = getelementptr inbounds %struct.ChildOf, ptr %col_base, i32 %0
  %parent_gep = getelementptr inbounds nuw %struct.ChildOf, ptr %elem_ptr, i32 0, i32 0
  store i32 %1, ptr %parent_gep, align 4
  ret void
}

define void @world_set_Position(i32 %0, float %1, float %2) {
entry:
  %col_base = load ptr, ptr @arch_col_Position, align 8
  %elem_ptr = getelementptr inbounds %struct.Position, ptr %col_base, i32 %0
  %x_gep = getelementptr inbounds nuw %struct.Position, ptr %elem_ptr, i32 0, i32 0
  store float %1, ptr %x_gep, align 4
  %y_gep = getelementptr inbounds nuw %struct.Position, ptr %elem_ptr, i32 0, i32 1
  store float %2, ptr %y_gep, align 4
  ret void
}

define void @world_set_Velocity(i32 %0, float %1, float %2) {
entry:
  %col_base = load ptr, ptr @arch_col_Velocity, align 8
  %elem_ptr = getelementptr inbounds %struct.Velocity, ptr %col_base, i32 %0
  %vx_gep = getelementptr inbounds nuw %struct.Velocity, ptr %elem_ptr, i32 0, i32 0
  store float %1, ptr %vx_gep, align 4
  %vy_gep = getelementptr inbounds nuw %struct.Velocity, ptr %elem_ptr, i32 0, i32 1
  store float %2, ptr %vy_gep, align 4
  ret void
}

define void @world_set_Time(float %0) {
entry:
  store float %0, ptr @res_Time, align 4
  ret void
}

define void @world_sort_hierarchy() {
entry:
  %total_count = load i32, ptr @arch_count, align 4
  %can_sort = icmp sgt i32 %total_count, 1
  br i1 %can_sort, label %do_sort, label %exit_sort

do_sort:                                          ; preds = %entry
  %count64 = zext i32 %total_count to i64
  %bytes_needed = mul i64 %count64, 4
  %depths_raw = call ptr @malloc(i64 %bytes_needed)
  %child_of_col = load ptr, ptr @arch_col_ChildOf, align 8
  %init_i = alloca i32, align 4
  store i32 0, ptr %init_i, align 4
  br label %d_init_cond

exit_sort:                                        ; preds = %sort_outer_exit, %entry
  ret void

d_init_cond:                                      ; preds = %d_init_body, %do_sort
  %cur_init_i = load i32, ptr %init_i, align 4
  %has_init_more = icmp slt i32 %cur_init_i, %total_count
  br i1 %has_init_more, label %d_init_body, label %d_init_exit

d_init_body:                                      ; preds = %d_init_cond
  %child_elem = getelementptr inbounds %struct.ChildOf, ptr %child_of_col, i32 %cur_init_i
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
  %sort_i = alloca i32, align 4
  %sort_j = alloca i32, align 4
  store i32 0, ptr %sort_i, align 4
  br label %sort_outer_cond

sort_outer_cond:                                  ; preds = %sort_inner_step, %d_init_exit
  %cur_sort_i = load i32, ptr %sort_i, align 4
  %outer_limit = sub i32 %total_count, 1
  %outer_more = icmp slt i32 %cur_sort_i, %outer_limit
  br i1 %outer_more, label %sort_outer_body, label %sort_outer_exit

sort_outer_body:                                  ; preds = %sort_outer_cond
  %inner_start = add i32 %cur_sort_i, 1
  store i32 %inner_start, ptr %sort_j, align 4
  br label %sort_inner_cond

sort_outer_exit:                                  ; preds = %sort_outer_cond
  call void @free(ptr %depths_raw)
  br label %exit_sort

sort_inner_cond:                                  ; preds = %skip_swap, %sort_outer_body
  %cur_sort_j = load i32, ptr %sort_j, align 4
  %inner_more = icmp slt i32 %cur_sort_j, %total_count
  br i1 %inner_more, label %sort_inner_body, label %sort_inner_step

sort_inner_body:                                  ; preds = %sort_inner_cond
  %di_slot = getelementptr inbounds i32, ptr %depths_raw, i32 %cur_sort_i
  %dj_slot = getelementptr inbounds i32, ptr %depths_raw, i32 %cur_sort_j
  %di = load i32, ptr %di_slot, align 4
  %dj = load i32, ptr %dj_slot, align 4
  %need_swap = icmp sgt i32 %di, %dj
  br i1 %need_swap, label %do_swap, label %skip_swap

sort_inner_step:                                  ; preds = %sort_inner_cond
  %next_i = add i32 %cur_sort_i, 1
  store i32 %next_i, ptr %sort_i, align 4
  br label %sort_outer_cond

do_swap:                                          ; preds = %sort_inner_body
  store i32 %dj, ptr %di_slot, align 4
  store i32 %di, ptr %dj_slot, align 4
  %ChildOf_col_swap = load ptr, ptr @arch_col_ChildOf, align 8
  %ChildOf_i = getelementptr inbounds %struct.ChildOf, ptr %ChildOf_col_swap, i32 %cur_sort_i
  %ChildOf_j = getelementptr inbounds %struct.ChildOf, ptr %ChildOf_col_swap, i32 %cur_sort_j
  %ChildOf_val_i = load %struct.ChildOf, ptr %ChildOf_i, align 4
  %ChildOf_val_j = load %struct.ChildOf, ptr %ChildOf_j, align 4
  store %struct.ChildOf %ChildOf_val_j, ptr %ChildOf_i, align 4
  store %struct.ChildOf %ChildOf_val_i, ptr %ChildOf_j, align 4
  %Position_col_swap = load ptr, ptr @arch_col_Position, align 8
  %Position_i = getelementptr inbounds %struct.Position, ptr %Position_col_swap, i32 %cur_sort_i
  %Position_j = getelementptr inbounds %struct.Position, ptr %Position_col_swap, i32 %cur_sort_j
  %Position_val_i = load %struct.Position, ptr %Position_i, align 4
  %Position_val_j = load %struct.Position, ptr %Position_j, align 4
  store %struct.Position %Position_val_j, ptr %Position_i, align 4
  store %struct.Position %Position_val_i, ptr %Position_j, align 4
  %Velocity_col_swap = load ptr, ptr @arch_col_Velocity, align 8
  %Velocity_i = getelementptr inbounds %struct.Velocity, ptr %Velocity_col_swap, i32 %cur_sort_i
  %Velocity_j = getelementptr inbounds %struct.Velocity, ptr %Velocity_col_swap, i32 %cur_sort_j
  %Velocity_val_i = load %struct.Velocity, ptr %Velocity_i, align 4
  %Velocity_val_j = load %struct.Velocity, ptr %Velocity_j, align 4
  store %struct.Velocity %Velocity_val_j, ptr %Velocity_i, align 4
  store %struct.Velocity %Velocity_val_i, ptr %Velocity_j, align 4
  br label %skip_swap

skip_swap:                                        ; preds = %do_swap, %sort_inner_body
  %next_j = add i32 %cur_sort_j, 1
  store i32 %next_j, ptr %sort_j, align 4
  br label %sort_inner_cond
}

declare ptr @malloc(i64)

declare void @free(ptr)

define void @system_MovementSystem() {
entry:
  %total_count = load i32, ptr @arch_count, align 4
  %has_entities = icmp sgt i32 %total_count, 0
  br i1 %has_entities, label %loop_cond, label %exit

loop_cond:                                        ; preds = %entry
  %i = alloca i32, align 4
  store i32 0, ptr %i, align 4
  br label %loop_body

loop_body:                                        ; preds = %loop_body, %loop_cond
  %cur_i = load i32, ptr %i, align 4
  %pos_col = load ptr, ptr @arch_col_Position, align 8
  %pos_ptr = getelementptr inbounds %struct.Position, ptr %pos_col, i32 %cur_i
  %vel_col = load ptr, ptr @arch_col_Velocity, align 8
  %vel_ptr = getelementptr inbounds %struct.Velocity, ptr %vel_col, i32 %cur_i
  %vel_vx = getelementptr inbounds nuw %struct.Velocity, ptr %vel_ptr, i32 0, i32 0
  %vx_val = load float, ptr %vel_vx, align 4
  %dt_val = load float, ptr @res_Time, align 4
  %fmul = fmul float %vx_val, %dt_val
  %pos_x_gep = getelementptr inbounds nuw %struct.Position, ptr %pos_ptr, i32 0, i32 0
  %cur_fld = load float, ptr %pos_x_gep, align 4
  %fadd = fadd float %cur_fld, %fmul
  store float %fadd, ptr %pos_x_gep, align 4
  %vel_vy = getelementptr inbounds nuw %struct.Velocity, ptr %vel_ptr, i32 0, i32 1
  %vy_val = load float, ptr %vel_vy, align 4
  %dt_val1 = load float, ptr @res_Time, align 4
  %fmul2 = fmul float %vy_val, %dt_val1
  %pos_y_gep = getelementptr inbounds nuw %struct.Position, ptr %pos_ptr, i32 0, i32 1
  %cur_fld3 = load float, ptr %pos_y_gep, align 4
  %fadd4 = fadd float %cur_fld3, %fmul2
  store float %fadd4, ptr %pos_y_gep, align 4
  %next_i = add i32 %cur_i, 1
  store i32 %next_i, ptr %i, align 4
  %loop_more = icmp slt i32 %next_i, %total_count
  br i1 %loop_more, label %loop_body, label %exit

exit:                                             ; preds = %loop_body, %entry
  ret void
}

define void @system_PrintSystem() {
entry:
  %total_count = load i32, ptr @arch_count, align 4
  %has_entities = icmp sgt i32 %total_count, 0
  br i1 %has_entities, label %loop_cond, label %exit

loop_cond:                                        ; preds = %entry
  %i = alloca i32, align 4
  store i32 0, ptr %i, align 4
  br label %loop_body

loop_body:                                        ; preds = %loop_body, %loop_cond
  %cur_i = load i32, ptr %i, align 4
  %pos_col = load ptr, ptr @arch_col_Position, align 8
  %pos_ptr = getelementptr inbounds %struct.Position, ptr %pos_col, i32 %cur_i
  %puts_call = call i32 @puts(ptr @str_lit)
  %pos_x = getelementptr inbounds nuw %struct.Position, ptr %pos_ptr, i32 0, i32 0
  %x_val = load float, ptr %pos_x, align 4
  %f_to_d = fpext float %x_val to double
  %printf_call = call i32 (ptr, ...) @printf(ptr @fmt_f, double %f_to_d)
  %pos_y = getelementptr inbounds nuw %struct.Position, ptr %pos_ptr, i32 0, i32 1
  %y_val = load float, ptr %pos_y, align 4
  %f_to_d1 = fpext float %y_val to double
  %printf_call2 = call i32 (ptr, ...) @printf(ptr @fmt_f.1, double %f_to_d1)
  %next_i = add i32 %cur_i, 1
  store i32 %next_i, ptr %i, align 4
  %loop_more = icmp slt i32 %next_i, %total_count
  br i1 %loop_more, label %loop_body, label %exit

exit:                                             ; preds = %loop_body, %entry
  ret void
}

define void @pipeline_GameLoop() {
entry:
  call void @system_MovementSystem()
  call void @system_PrintSystem()
  ret void
}

define i32 @main() {
entry:
  %e1 = alloca i32, align 4
  %e0 = alloca i32, align 4
  %puts_call = call i32 @puts(ptr @str_lit.2)
  call void @world_set_Time(float 5.000000e-01)
  %world_spawn_call = call i32 @world_spawn()
  store i32 %world_spawn_call, ptr %e0, align 4
  %e01 = load i32, ptr %e0, align 4
  call void @world_set_Position(i32 %e01, float 1.000000e+01, float 2.000000e+01)
  %e02 = load i32, ptr %e0, align 4
  call void @world_set_Velocity(i32 %e02, float 2.000000e+00, float -1.000000e+00)
  %world_spawn_call3 = call i32 @world_spawn()
  store i32 %world_spawn_call3, ptr %e1, align 4
  %e14 = load i32, ptr %e1, align 4
  call void @world_set_Position(i32 %e14, float 1.000000e+02, float 2.000000e+02)
  %e15 = load i32, ptr %e1, align 4
  call void @world_set_Velocity(i32 %e15, float 5.000000e+00, float 1.000000e+01)
  %puts_call6 = call i32 @puts(ptr @str_lit.3)
  call void @pipeline_GameLoop()
  %puts_call7 = call i32 @puts(ptr @str_lit.4)
  call void @pipeline_GameLoop()
  %puts_call8 = call i32 @puts(ptr @str_lit.5)
  %puts_call9 = call i32 @puts(ptr @str_lit.6)
  %key_input = call i32 @getchar()
  ret i32 0
}
