; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.ChildOf = type { i32 }
%struct.TextWidget = type { ptr, float }
%struct.RectTransform = type { float, float, float, float }

@arch_count = global i32 0
@arch_cap = global i32 0
@arch_col_ChildOf = global ptr null
@arch_col_TextWidget = global ptr null
@arch_col_RectTransform = global ptr null
@str_lit = private unnamed_addr constant [47 x i8] c"----------------------------------------------\00", align 1
@str_lit.1 = private unnamed_addr constant [18 x i8] c"Rendering Widget:\00", align 1
@str_lit.2 = private unnamed_addr constant [11 x i8] c"Font Size:\00", align 1
@fmt_f = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.3 = private unnamed_addr constant [18 x i8] c"X, Y Coordinates:\00", align 1
@fmt_f.4 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@fmt_f.5 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.6 = private unnamed_addr constant [34 x i8] c"Parent Entity Index (-1 is Root):\00", align 1
@fmt_d = private unnamed_addr constant [4 x i8] c"%d\0A\00", align 1
@str_lit.7 = private unnamed_addr constant [47 x i8] c"==============================================\00", align 1
@str_lit.8 = private unnamed_addr constant [47 x i8] c"  ECS-Lang: Declarative GUI & Hierarchy Demo  \00", align 1
@str_lit.9 = private unnamed_addr constant [47 x i8] c"==============================================\00", align 1
@str_lit.10 = private unnamed_addr constant [22 x i8] c"[Button: Submit Form]\00", align 1
@str_lit.11 = private unnamed_addr constant [26 x i8] c"[Window: Settings Dialog]\00", align 1
@str_lit.12 = private unnamed_addr constant [17 x i8] c"[Label: OK Text]\00", align 1
@str_lit.13 = private unnamed_addr constant [61 x i8] c"Running GuiPipeline with automatic 'sort_hierarchy' stage...\00", align 1
@str_lit.14 = private unnamed_addr constant [68 x i8] c"Notice that the root Window renders first, then Button, then Label!\00", align 1
@str_lit.15 = private unnamed_addr constant [47 x i8] c"==============================================\00", align 1
@str_lit.16 = private unnamed_addr constant [44 x i8] c"GUI hierarchy layout successfully verified!\00", align 1
@str_lit.17 = private unnamed_addr constant [47 x i8] c"==============================================\00", align 1
@str_lit.18 = private unnamed_addr constant [23 x i8] c"Press Enter to exit...\00", align 1

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
  %TextWidget_ptr = load ptr, ptr @arch_col_TextWidget, align 8
  %alloc_bytes1 = mul i64 %new_cap64, 32
  %realloc_call2 = call ptr @realloc(ptr %TextWidget_ptr, i64 %alloc_bytes1)
  store ptr %realloc_call2, ptr @arch_col_TextWidget, align 8
  %RectTransform_ptr = load ptr, ptr @arch_col_RectTransform, align 8
  %alloc_bytes3 = mul i64 %new_cap64, 32
  %realloc_call4 = call ptr @realloc(ptr %RectTransform_ptr, i64 %alloc_bytes3)
  store ptr %realloc_call4, ptr @arch_col_RectTransform, align 8
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

define void @world_set_TextWidget(i32 %0, ptr %1, float %2) {
entry:
  %col_base = load ptr, ptr @arch_col_TextWidget, align 8
  %elem_ptr = getelementptr inbounds %struct.TextWidget, ptr %col_base, i32 %0
  %content_gep = getelementptr inbounds nuw %struct.TextWidget, ptr %elem_ptr, i32 0, i32 0
  store ptr %1, ptr %content_gep, align 8
  %font_size_gep = getelementptr inbounds nuw %struct.TextWidget, ptr %elem_ptr, i32 0, i32 1
  store float %2, ptr %font_size_gep, align 4
  ret void
}

define void @world_set_RectTransform(i32 %0, float %1, float %2, float %3, float %4) {
entry:
  %col_base = load ptr, ptr @arch_col_RectTransform, align 8
  %elem_ptr = getelementptr inbounds %struct.RectTransform, ptr %col_base, i32 %0
  %x_gep = getelementptr inbounds nuw %struct.RectTransform, ptr %elem_ptr, i32 0, i32 0
  store float %1, ptr %x_gep, align 4
  %y_gep = getelementptr inbounds nuw %struct.RectTransform, ptr %elem_ptr, i32 0, i32 1
  store float %2, ptr %y_gep, align 4
  %width_gep = getelementptr inbounds nuw %struct.RectTransform, ptr %elem_ptr, i32 0, i32 2
  store float %3, ptr %width_gep, align 4
  %height_gep = getelementptr inbounds nuw %struct.RectTransform, ptr %elem_ptr, i32 0, i32 3
  store float %4, ptr %height_gep, align 4
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
  %TextWidget_col_swap = load ptr, ptr @arch_col_TextWidget, align 8
  %TextWidget_i = getelementptr inbounds %struct.TextWidget, ptr %TextWidget_col_swap, i32 %cur_sort_i
  %TextWidget_j = getelementptr inbounds %struct.TextWidget, ptr %TextWidget_col_swap, i32 %cur_sort_j
  %TextWidget_val_i = load %struct.TextWidget, ptr %TextWidget_i, align 8
  %TextWidget_val_j = load %struct.TextWidget, ptr %TextWidget_j, align 8
  store %struct.TextWidget %TextWidget_val_j, ptr %TextWidget_i, align 8
  store %struct.TextWidget %TextWidget_val_i, ptr %TextWidget_j, align 8
  %RectTransform_col_swap = load ptr, ptr @arch_col_RectTransform, align 8
  %RectTransform_i = getelementptr inbounds %struct.RectTransform, ptr %RectTransform_col_swap, i32 %cur_sort_i
  %RectTransform_j = getelementptr inbounds %struct.RectTransform, ptr %RectTransform_col_swap, i32 %cur_sort_j
  %RectTransform_val_i = load %struct.RectTransform, ptr %RectTransform_i, align 4
  %RectTransform_val_j = load %struct.RectTransform, ptr %RectTransform_j, align 4
  store %struct.RectTransform %RectTransform_val_j, ptr %RectTransform_i, align 4
  store %struct.RectTransform %RectTransform_val_i, ptr %RectTransform_j, align 4
  br label %skip_swap

skip_swap:                                        ; preds = %do_swap, %sort_inner_body
  %next_j = add i32 %cur_sort_j, 1
  store i32 %next_j, ptr %sort_j, align 4
  br label %sort_inner_cond
}

declare ptr @malloc(i64)

declare void @free(ptr)

define void @system_RenderTextSystem() {
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
  %text_col = load ptr, ptr @arch_col_TextWidget, align 8
  %text_ptr = getelementptr inbounds %struct.TextWidget, ptr %text_col, i32 %cur_i
  %rect_col = load ptr, ptr @arch_col_RectTransform, align 8
  %rect_ptr = getelementptr inbounds %struct.RectTransform, ptr %rect_col, i32 %cur_i
  %child_col = load ptr, ptr @arch_col_ChildOf, align 8
  %child_ptr = getelementptr inbounds %struct.ChildOf, ptr %child_col, i32 %cur_i
  %puts_call = call i32 @puts(ptr @str_lit)
  %puts_call1 = call i32 @puts(ptr @str_lit.1)
  %text_content = getelementptr inbounds nuw %struct.TextWidget, ptr %text_ptr, i32 0, i32 0
  %content_val = load ptr, ptr %text_content, align 8
  %puts_call2 = call i32 @puts(ptr %content_val)
  %puts_call3 = call i32 @puts(ptr @str_lit.2)
  %text_font_size = getelementptr inbounds nuw %struct.TextWidget, ptr %text_ptr, i32 0, i32 1
  %font_size_val = load float, ptr %text_font_size, align 4
  %f_to_d = fpext float %font_size_val to double
  %printf_call = call i32 (ptr, ...) @printf(ptr @fmt_f, double %f_to_d)
  %puts_call4 = call i32 @puts(ptr @str_lit.3)
  %rect_x = getelementptr inbounds nuw %struct.RectTransform, ptr %rect_ptr, i32 0, i32 0
  %x_val = load float, ptr %rect_x, align 4
  %f_to_d5 = fpext float %x_val to double
  %printf_call6 = call i32 (ptr, ...) @printf(ptr @fmt_f.4, double %f_to_d5)
  %rect_y = getelementptr inbounds nuw %struct.RectTransform, ptr %rect_ptr, i32 0, i32 1
  %y_val = load float, ptr %rect_y, align 4
  %f_to_d7 = fpext float %y_val to double
  %printf_call8 = call i32 (ptr, ...) @printf(ptr @fmt_f.5, double %f_to_d7)
  %puts_call9 = call i32 @puts(ptr @str_lit.6)
  %child_parent = getelementptr inbounds nuw %struct.ChildOf, ptr %child_ptr, i32 0, i32 0
  %parent_val = load i32, ptr %child_parent, align 4
  %printf_call10 = call i32 (ptr, ...) @printf(ptr @fmt_d, i32 %parent_val)
  %next_i = add i32 %cur_i, 1
  store i32 %next_i, ptr %i, align 4
  %loop_more = icmp slt i32 %next_i, %total_count
  br i1 %loop_more, label %loop_body, label %exit

exit:                                             ; preds = %loop_body, %entry
  ret void
}

define void @pipeline_GuiPipeline() {
entry:
  call void @world_sort_hierarchy()
  call void @system_RenderTextSystem()
  ret void
}

define i32 @main() {
entry:
  %grandChild = alloca i32, align 4
  %parentWin = alloca i32, align 4
  %childBtn = alloca i32, align 4
  %puts_call = call i32 @puts(ptr @str_lit.7)
  %puts_call1 = call i32 @puts(ptr @str_lit.8)
  %puts_call2 = call i32 @puts(ptr @str_lit.9)
  %world_spawn_call = call i32 @world_spawn()
  store i32 %world_spawn_call, ptr %childBtn, align 4
  %childBtn3 = load i32, ptr %childBtn, align 4
  call void @world_set_TextWidget(i32 %childBtn3, ptr @str_lit.10, float 1.400000e+01)
  %childBtn4 = load i32, ptr %childBtn, align 4
  call void @world_set_RectTransform(i32 %childBtn4, float 2.000000e+01, float 5.000000e+01, float 1.200000e+02, float 4.000000e+01)
  %childBtn5 = load i32, ptr %childBtn, align 4
  call void @world_set_ChildOf(i32 %childBtn5, i32 1)
  %world_spawn_call6 = call i32 @world_spawn()
  store i32 %world_spawn_call6, ptr %parentWin, align 4
  %parentWin7 = load i32, ptr %parentWin, align 4
  call void @world_set_TextWidget(i32 %parentWin7, ptr @str_lit.11, float 1.800000e+01)
  %parentWin8 = load i32, ptr %parentWin, align 4
  call void @world_set_RectTransform(i32 %parentWin8, float 0.000000e+00, float 0.000000e+00, float 8.000000e+02, float 6.000000e+02)
  %parentWin9 = load i32, ptr %parentWin, align 4
  call void @world_set_ChildOf(i32 %parentWin9, i32 -1)
  %world_spawn_call10 = call i32 @world_spawn()
  store i32 %world_spawn_call10, ptr %grandChild, align 4
  %grandChild11 = load i32, ptr %grandChild, align 4
  call void @world_set_TextWidget(i32 %grandChild11, ptr @str_lit.12, float 1.200000e+01)
  %grandChild12 = load i32, ptr %grandChild, align 4
  call void @world_set_RectTransform(i32 %grandChild12, float 2.500000e+01, float 5.500000e+01, float 5.000000e+01, float 2.000000e+01)
  %grandChild13 = load i32, ptr %grandChild, align 4
  call void @world_set_ChildOf(i32 %grandChild13, i32 0)
  %puts_call14 = call i32 @puts(ptr @str_lit.13)
  %puts_call15 = call i32 @puts(ptr @str_lit.14)
  call void @pipeline_GuiPipeline()
  %puts_call16 = call i32 @puts(ptr @str_lit.15)
  %puts_call17 = call i32 @puts(ptr @str_lit.16)
  %puts_call18 = call i32 @puts(ptr @str_lit.17)
  %puts_call19 = call i32 @puts(ptr @str_lit.18)
  %key_input = call i32 @getchar()
  ret i32 0
}
