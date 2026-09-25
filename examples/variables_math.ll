; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.ChildOf = type { i32 }

@arch_count = global i32 0
@arch_cap = global i32 0
@arch_col_ChildOf = global ptr null
@str_lit = private unnamed_addr constant [33 x i8] c"Computed Total (should be 12.5):\00", align 1
@fmt_f = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.1 = private unnamed_addr constant [32 x i8] c"Iterations count (should be 5):\00", align 1
@fmt_d = private unnamed_addr constant [4 x i8] c"%d\0A\00", align 1
@str_lit.2 = private unnamed_addr constant [49 x i8] c"Verification passed: total is greater than 10.0!\00", align 1
@str_lit.3 = private unnamed_addr constant [21 x i8] c"Verification failed!\00", align 1
@str_lit.4 = private unnamed_addr constant [23 x i8] c"Press Enter to exit...\00", align 1

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
  br label %skip_swap

skip_swap:                                        ; preds = %do_swap, %sort_inner_body
  %next_j = add i32 %cur_sort_j, 1
  store i32 %next_j, ptr %sort_j, align 4
  br label %sort_inner_cond
}

declare ptr @malloc(i64)

declare void @free(ptr)

define i32 @main() {
entry:
  %total = alloca float, align 4
  %i = alloca i32, align 4
  store i32 0, ptr %i, align 4
  store float 0.000000e+00, ptr %total, align 4
  br label %while_cond

while_cond:                                       ; preds = %while_body, %entry
  %i1 = load i32, ptr %i, align 4
  %slt = icmp slt i32 %i1, 5
  br i1 %slt, label %while_body, label %while_exit

while_body:                                       ; preds = %while_cond
  %total_cur = load float, ptr %total, align 4
  %fadd = fadd float %total_cur, 2.500000e+00
  store float %fadd, ptr %total, align 4
  %i_cur = load i32, ptr %i, align 4
  %add = add i32 %i_cur, 1
  store i32 %add, ptr %i, align 4
  br label %while_cond

while_exit:                                       ; preds = %while_cond
  %puts_call = call i32 @puts(ptr @str_lit)
  %total2 = load float, ptr %total, align 4
  %f_to_d = fpext float %total2 to double
  %printf_call = call i32 (ptr, ...) @printf(ptr @fmt_f, double %f_to_d)
  %puts_call3 = call i32 @puts(ptr @str_lit.1)
  %i4 = load i32, ptr %i, align 4
  %printf_call5 = call i32 (ptr, ...) @printf(ptr @fmt_d, i32 %i4)
  %total6 = load float, ptr %total, align 4
  %fgt = fcmp ogt float %total6, 1.000000e+01
  br i1 %fgt, label %then, label %else

then:                                             ; preds = %while_exit
  %puts_call7 = call i32 @puts(ptr @str_lit.2)
  br label %if_merge

else:                                             ; preds = %while_exit
  %puts_call8 = call i32 @puts(ptr @str_lit.3)
  br label %if_merge

if_merge:                                         ; preds = %else, %then
  %puts_call9 = call i32 @puts(ptr @str_lit.4)
  %key_input = call i32 @getchar()
  ret i32 0
}
