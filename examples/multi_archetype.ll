; ModuleID = 'ecs_module'
source_filename = "ecs_module"
target datalayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-windows-msvc"

%struct.Archetype = type { i64, i32, i32, ptr, [5 x ptr] }
%struct.ChildOf = type { i32 }
%struct.Position = type { float, float }
%struct.Velocity = type { float, float }
%struct.PlayerTag = type { i32 }
%struct.Obstacle = type { i1 }

@NvOptimusEnablement = dllexport local_unnamed_addr global i32 1
@AmdPowerXpressRequestHighPerformance = dllexport local_unnamed_addr global i32 1
@str_lit = private unnamed_addr constant [17 x i8] c"Player Position:\00", align 1
@str_lit.2 = private unnamed_addr constant [19 x i8] c"Obstacle Position:\00", align 1
@fmt_f.4 = private unnamed_addr constant [4 x i8] c"%f\0A\00", align 1
@str_lit.6 = private unnamed_addr constant [51 x i8] c"  ECS-Lang: Multi-Archetype Dynamic ECS Demo      \00", align 1
@str_lit.8 = private unnamed_addr constant [38 x i8] c"Verifying initial component presence:\00", align 1
@str_lit.9 = private unnamed_addr constant [34 x i8] c"Player has Velocity (expected 1):\00", align 1
@str_lit.10 = private unnamed_addr constant [32 x i8] c"Rock has Velocity (expected 0):\00", align 1
@str_lit.12 = private unnamed_addr constant [32 x i8] c"Rock has Obstacle (expected 1):\00", align 1
@str_lit.14 = private unnamed_addr constant [37 x i8] c"--- Frame 1: Running PhysicsLoop ---\00", align 1
@str_lit.15 = private unnamed_addr constant [44 x i8] c"Player after frame 1 (expected 15.0, 22.0):\00", align 1
@str_lit.16 = private unnamed_addr constant [58 x i8] c"Obstacle after frame 1 (expected 100.0, 100.0 - unmoved):\00", align 1
@str_lit.17 = private unnamed_addr constant [38 x i8] c"--- Removing Velocity from Bullet ---\00", align 1
@str_lit.18 = private unnamed_addr constant [48 x i8] c"Bullet has Velocity after removal (expected 0):\00", align 1
@str_lit.20 = private unnamed_addr constant [44 x i8] c"--- Dynamically adding Velocity to Rock ---\00", align 1
@str_lit.21 = private unnamed_addr constant [47 x i8] c"Rock has Velocity after addition (expected 1):\00", align 1
@fmt_b.22 = private unnamed_addr constant [4 x i8] c"%d\0A\00", align 1
@str_lit.23 = private unnamed_addr constant [43 x i8] c"--- Frame 2: Running PhysicsLoop again ---\00", align 1
@str_lit.24 = private unnamed_addr constant [44 x i8] c"Player after frame 2 (expected 20.0, 24.0):\00", align 1
@str_lit.25 = private unnamed_addr constant [84 x i8] c"Obstacle after frame 2 (expected 101.0, 102.0 - moved because Velocity was added!):\00", align 1
@str_lit.27 = private unnamed_addr constant [41 x i8] c"Multi-Archetype verification successful!\00", align 1
@str_lit.28 = private unnamed_addr constant [51 x i8] c"==================================================\00", align 1
@str_lit.29 = private unnamed_addr constant [23 x i8] c"Press Enter to exit...\00", align 1

; Function Attrs: nofree nounwind
declare noundef i32 @puts(ptr nocapture noundef readonly) local_unnamed_addr #0

; Function Attrs: nofree nounwind
declare noundef i32 @printf(ptr nocapture noundef readonly, ...) local_unnamed_addr #0

; Function Attrs: mustprogress nounwind willreturn allockind("realloc") allocsize(1) memory(argmem: readwrite, inaccessiblemem: readwrite)
declare noalias noundef ptr @realloc(ptr allocptr nocapture, i64 noundef) local_unnamed_addr #1

; Function Attrs: nofree nounwind
declare noundef i32 @getchar() local_unnamed_addr #0

; Function Attrs: mustprogress nofree nounwind willreturn allockind("alloc,uninitialized") allocsize(0) memory(inaccessiblemem: readwrite)
declare noalias noundef ptr @malloc(i64 noundef) local_unnamed_addr #2

; Function Attrs: mustprogress nounwind willreturn allockind("free") memory(argmem: readwrite, inaccessiblemem: readwrite)
declare void @free(ptr allocptr nocapture noundef) local_unnamed_addr #3

; Function Attrs: nounwind
define i32 @world_get_or_create_archetype(ptr nocapture %0, i64 %1) local_unnamed_addr #4 {
entry:
  %arch_cap_slot = getelementptr inbounds nuw i8, ptr %0, i64 4
  %arch_tables_slot = getelementptr inbounds nuw i8, ptr %0, i64 8
  %cur_count = load i32, ptr %0, align 4
  %has_more2 = icmp sgt i32 %cur_count, 0
  %latest_tables.pre.pre = load ptr, ptr %arch_tables_slot, align 8
  br i1 %has_more2, label %search_body.lr.ph, label %not_found

search_body.lr.ph:                                ; preds = %entry
  %wide.trip.count = zext nneg i32 %cur_count to i64
  br label %search_body

search_body:                                      ; preds = %search_body.lr.ph, %search_next
  %indvars.iv = phi i64 [ 0, %search_body.lr.ph ], [ %indvars.iv.next, %search_next ]
  %arch_elem = getelementptr inbounds nuw %struct.Archetype, ptr %latest_tables.pre.pre, i64 %indvars.iv
  %existing_mask = load i64, ptr %arch_elem, align 8
  %is_match = icmp eq i64 %existing_mask, %1
  br i1 %is_match, label %common.ret.loopexit, label %search_next

not_found:                                        ; preds = %search_next, %entry
  %cur_cap = load i32, ptr %arch_cap_slot, align 4
  %need_grow.not = icmp slt i32 %cur_count, %cur_cap
  br i1 %need_grow.not, label %init_arch, label %grow_tables

common.ret.loopexit:                              ; preds = %search_body
  %2 = trunc nuw nsw i64 %indvars.iv to i32
  br label %common.ret

common.ret:                                       ; preds = %common.ret.loopexit, %init_arch
  %common.ret.op = phi i32 [ %cur_count, %init_arch ], [ %2, %common.ret.loopexit ]
  ret i32 %common.ret.op

search_next:                                      ; preds = %search_body
  %indvars.iv.next = add nuw nsw i64 %indvars.iv, 1
  %exitcond.not = icmp eq i64 %indvars.iv.next, %wide.trip.count
  br i1 %exitcond.not, label %not_found, label %search_body

grow_tables:                                      ; preds = %not_found
  %cap_zero = icmp eq i32 %cur_cap, 0
  %double_cap = shl i32 %cur_cap, 1
  %new_cap = select i1 %cap_zero, i32 8, i32 %double_cap
  store i32 %new_cap, ptr %arch_cap_slot, align 4
  %new_cap64 = zext i32 %new_cap to i64
  %alloc_bytes = shl nuw nsw i64 %new_cap64, 6
  %new_tables_i8 = tail call ptr @realloc(ptr %latest_tables.pre.pre, i64 %alloc_bytes)
  store ptr %new_tables_i8, ptr %arch_tables_slot, align 8
  br label %init_arch

init_arch:                                        ; preds = %grow_tables, %not_found
  %latest_tables = phi ptr [ %new_tables_i8, %grow_tables ], [ %latest_tables.pre.pre, %not_found ]
  %next_count = add i32 %cur_count, 1
  store i32 %next_count, ptr %0, align 4
  %3 = sext i32 %cur_count to i64
  %new_arch_elem = getelementptr inbounds %struct.Archetype, ptr %latest_tables, i64 %3
  store i64 %1, ptr %new_arch_elem, align 8
  %cnt_gep = getelementptr inbounds nuw i8, ptr %new_arch_elem, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep, i8 0, i64 56, i1 false)
  br label %common.ret
}

; Function Attrs: mustprogress nounwind willreturn
define void @world_grow_archetype(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #5 {
entry:
  %tables_slot_gr = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_base = load ptr, ptr %tables_slot_gr, align 8
  %2 = sext i32 %1 to i64
  %arch_elem = getelementptr inbounds %struct.Archetype, ptr %tables_base, i64 %2
  %cap_slot = getelementptr inbounds nuw i8, ptr %arch_elem, i64 12
  %cur_cap = load i32, ptr %cap_slot, align 4
  %is_zero = icmp eq i32 %cur_cap, 0
  %double_cap = shl i32 %cur_cap, 1
  %new_arch_cap = select i1 %is_zero, i32 32, i32 %double_cap
  store i32 %new_arch_cap, ptr %cap_slot, align 4
  %new_cap64 = zext i32 %new_arch_cap to i64
  %ent_slot = getelementptr inbounds nuw i8, ptr %arch_elem, i64 16
  %cur_ent_raw = load ptr, ptr %ent_slot, align 8
  %ent_bytes = shl nuw nsw i64 %new_cap64, 2
  %new_ent_raw = tail call ptr @realloc(ptr %cur_ent_raw, i64 %ent_bytes)
  store ptr %new_ent_raw, ptr %ent_slot, align 8
  %arch_mask = load i64, ptr %arch_elem, align 8
  %has_ChildOf = and i64 %arch_mask, 1
  %is_has_ChildOf.not = icmp eq i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf.not, label %skip_col_ChildOf, label %grow_col_ChildOf

grow_col_ChildOf:                                 ; preds = %entry
  %cols_arr = getelementptr inbounds nuw i8, ptr %arch_elem, i64 24
  %cur_col_ChildOf = load ptr, ptr %cols_arr, align 8
  %new_col_ChildOf = tail call ptr @realloc(ptr %cur_col_ChildOf, i64 %ent_bytes)
  store ptr %new_col_ChildOf, ptr %cols_arr, align 8
  br label %skip_col_ChildOf

skip_col_ChildOf:                                 ; preds = %grow_col_ChildOf, %entry
  %has_Position = and i64 %arch_mask, 2
  %is_has_Position.not = icmp eq i64 %has_Position, 0
  br i1 %is_has_Position.not, label %skip_col_Position, label %grow_col_Position

grow_col_Position:                                ; preds = %skip_col_ChildOf
  %col_slot_Position = getelementptr inbounds nuw i8, ptr %arch_elem, i64 32
  %cur_col_Position = load ptr, ptr %col_slot_Position, align 8
  %col_bytes_Position = shl nuw nsw i64 %new_cap64, 3
  %new_col_Position = tail call ptr @realloc(ptr %cur_col_Position, i64 %col_bytes_Position)
  store ptr %new_col_Position, ptr %col_slot_Position, align 8
  br label %skip_col_Position

skip_col_Position:                                ; preds = %grow_col_Position, %skip_col_ChildOf
  %has_Velocity = and i64 %arch_mask, 4
  %is_has_Velocity.not = icmp eq i64 %has_Velocity, 0
  br i1 %is_has_Velocity.not, label %skip_col_Velocity, label %grow_col_Velocity

grow_col_Velocity:                                ; preds = %skip_col_Position
  %col_slot_Velocity = getelementptr inbounds nuw i8, ptr %arch_elem, i64 40
  %cur_col_Velocity = load ptr, ptr %col_slot_Velocity, align 8
  %col_bytes_Velocity = shl nuw nsw i64 %new_cap64, 3
  %new_col_Velocity = tail call ptr @realloc(ptr %cur_col_Velocity, i64 %col_bytes_Velocity)
  store ptr %new_col_Velocity, ptr %col_slot_Velocity, align 8
  br label %skip_col_Velocity

skip_col_Velocity:                                ; preds = %grow_col_Velocity, %skip_col_Position
  %has_PlayerTag = and i64 %arch_mask, 8
  %is_has_PlayerTag.not = icmp eq i64 %has_PlayerTag, 0
  br i1 %is_has_PlayerTag.not, label %skip_col_PlayerTag, label %grow_col_PlayerTag

grow_col_PlayerTag:                               ; preds = %skip_col_Velocity
  %col_slot_PlayerTag = getelementptr inbounds nuw i8, ptr %arch_elem, i64 48
  %cur_col_PlayerTag = load ptr, ptr %col_slot_PlayerTag, align 8
  %new_col_PlayerTag = tail call ptr @realloc(ptr %cur_col_PlayerTag, i64 %ent_bytes)
  store ptr %new_col_PlayerTag, ptr %col_slot_PlayerTag, align 8
  br label %skip_col_PlayerTag

skip_col_PlayerTag:                               ; preds = %grow_col_PlayerTag, %skip_col_Velocity
  %has_Obstacle = and i64 %arch_mask, 16
  %is_has_Obstacle.not = icmp eq i64 %has_Obstacle, 0
  br i1 %is_has_Obstacle.not, label %skip_col_Obstacle, label %grow_col_Obstacle

grow_col_Obstacle:                                ; preds = %skip_col_PlayerTag
  %col_slot_Obstacle = getelementptr inbounds nuw i8, ptr %arch_elem, i64 56
  %cur_col_Obstacle = load ptr, ptr %col_slot_Obstacle, align 8
  %new_col_Obstacle = tail call ptr @realloc(ptr %cur_col_Obstacle, i64 %new_cap64)
  store ptr %new_col_Obstacle, ptr %col_slot_Obstacle, align 8
  br label %skip_col_Obstacle

skip_col_Obstacle:                                ; preds = %grow_col_Obstacle, %skip_col_PlayerTag
  ret void
}

; Function Attrs: mustprogress nofree nounwind willreturn memory(write, argmem: none, inaccessiblemem: readwrite)
define noalias noundef ptr @ecs_create_world() local_unnamed_addr #6 {
init_arch.i:
  %calloc = tail call dereferenceable_or_null(72) ptr @calloc(i64 1, i64 72)
  %arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %calloc, i64 8
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %calloc, i64 4
  store i32 8, ptr %arch_cap_slot.i, align 4
  %malloc = tail call dereferenceable_or_null(512) ptr @malloc(i64 512)
  store ptr %malloc, ptr %arch_tables_slot.i, align 8
  store i32 1, ptr %calloc, align 4
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 8 dereferenceable(64) %malloc, i8 0, i64 64, i1 false)
  ret ptr %calloc
}

declare void @AcquireSRWLockExclusive(ptr) local_unnamed_addr

declare void @ReleaseSRWLockExclusive(ptr) local_unnamed_addr

; Function Attrs: mustprogress nounwind willreturn
define i32 @world_alloc_entity(ptr nocapture %0) local_unnamed_addr #5 {
entry:
  %ent_count_slot = getelementptr inbounds nuw i8, ptr %0, i64 16
  %ent_cap_slot = getelementptr inbounds nuw i8, ptr %0, i64 20
  %ent_arch_slot = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot = getelementptr inbounds nuw i8, ptr %0, i64 32
  %cur_ent_count = load i32, ptr %ent_count_slot, align 4
  %cur_ent_cap = load i32, ptr %ent_cap_slot, align 4
  %need_grow_ent.not = icmp slt i32 %cur_ent_count, %cur_ent_cap
  br i1 %need_grow_ent.not, label %assign_ent, label %grow_ent

grow_ent:                                         ; preds = %entry
  %ent_cap_zero = icmp eq i32 %cur_ent_cap, 0
  %double_ent_cap = shl i32 %cur_ent_cap, 1
  %new_ent_cap = select i1 %ent_cap_zero, i32 64, i32 %double_ent_cap
  store i32 %new_ent_cap, ptr %ent_cap_slot, align 4
  %new_ent_cap64 = zext i32 %new_ent_cap to i64
  %bytes_for_ent = shl nuw nsw i64 %new_ent_cap64, 2
  %cur_arch_arr = load ptr, ptr %ent_arch_slot, align 8
  %new_arch_i8 = tail call ptr @realloc(ptr %cur_arch_arr, i64 %bytes_for_ent)
  store ptr %new_arch_i8, ptr %ent_arch_slot, align 8
  %cur_row_arr = load ptr, ptr %ent_row_slot, align 8
  %new_row_i8 = tail call ptr @realloc(ptr %cur_row_arr, i64 %bytes_for_ent)
  store ptr %new_row_i8, ptr %ent_row_slot, align 8
  br label %assign_ent

assign_ent:                                       ; preds = %grow_ent, %entry
  %next_ent_cnt = add i32 %cur_ent_count, 1
  store i32 %next_ent_cnt, ptr %ent_count_slot, align 4
  %cur_arch_arr_alloc = load ptr, ptr %ent_arch_slot, align 8
  %1 = sext i32 %cur_ent_count to i64
  %e_arch_slot_alloc = getelementptr inbounds i32, ptr %cur_arch_arr_alloc, i64 %1
  store i32 -1, ptr %e_arch_slot_alloc, align 4
  %cur_row_arr_alloc = load ptr, ptr %ent_row_slot, align 8
  %e_row_slot_alloc = getelementptr inbounds i32, ptr %cur_row_arr_alloc, i64 %1
  store i32 -1, ptr %e_row_slot_alloc, align 4
  ret i32 %cur_ent_count
}

; Function Attrs: nounwind
define void @world_assign_a0(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %arch_arr_a0_slot = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_a0 = load ptr, ptr %arch_arr_a0_slot, align 8
  %2 = sext i32 %1 to i64
  %e_arch_slot_a0 = getelementptr inbounds i32, ptr %arch_arr_a0, i64 %2
  %cur_arch_val_a0 = load i32, ptr %e_arch_slot_a0, align 4
  %is_unassigned = icmp slt i32 %cur_arch_val_a0, 0
  br i1 %is_unassigned, label %do_assign, label %exit_a0

do_assign:                                        ; preds = %entry
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 8
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  %latest_tables.pre.pre.i = load ptr, ptr %arch_tables_slot.i, align 8
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_assign
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %latest_tables.pre.pre.i, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, 0
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_assign
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %3 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr %latest_tables.pre.pre.i, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %arch_tables_slot.i, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %latest_tables.pre.pre.i, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %4 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %4
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 8 dereferenceable(64) %new_arch_elem.i, i8 0, i64 64, i1 false)
  %tables_a0.pre = load ptr, ptr %arch_tables_slot.i, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %4, %init_arch.i ]
  %tables_a0 = phi ptr [ %latest_tables.pre.pre.i, %common.ret.loopexit.i ], [ %tables_a0.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %3, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %a0_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_a0, i64 %.pre-phi
  %cnt_slot0 = getelementptr inbounds nuw i8, ptr %a0_ptr, i64 8
  %cur_cnt0 = load i32, ptr %cnt_slot0, align 4
  %cap_slot0 = getelementptr inbounds nuw i8, ptr %a0_ptr, i64 12
  %cur_cap0 = load i32, ptr %cap_slot0, align 4
  %need_grow0.not = icmp slt i32 %cur_cnt0, %cur_cap0
  br i1 %need_grow0.not, label %after_grow_a0, label %grow_a0

exit_a0:                                          ; preds = %after_grow_a0, %entry
  ret void

grow_a0:                                          ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_a0_2.pre = load ptr, ptr %arch_tables_slot.i, align 8
  %cnt_slot0_2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_a0_2.pre, i64 %.pre-phi, i32 1
  %row.pre = load i32, ptr %cnt_slot0_2.phi.trans.insert, align 4
  br label %after_grow_a0

after_grow_a0:                                    ; preds = %grow_a0, %world_get_or_create_archetype.exit
  %row = phi i32 [ %row.pre, %grow_a0 ], [ %cur_cnt0, %world_get_or_create_archetype.exit ]
  %tables_a0_2 = phi ptr [ %tables_a0_2.pre, %grow_a0 ], [ %tables_a0, %world_get_or_create_archetype.exit ]
  %a0_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_a0_2, i64 %.pre-phi
  %cnt_slot0_2 = getelementptr inbounds nuw i8, ptr %a0_ptr2, i64 8
  %next_cnt0 = add i32 %row, 1
  store i32 %next_cnt0, ptr %cnt_slot0_2, align 4
  %ent_slot0 = getelementptr inbounds nuw i8, ptr %a0_ptr2, i64 16
  %ent_raw0 = load ptr, ptr %ent_slot0, align 8
  %5 = sext i32 %row to i64
  %ent_elem0 = getelementptr inbounds i32, ptr %ent_raw0, i64 %5
  store i32 %1, ptr %ent_elem0, align 4
  store i32 %common.ret.op.i, ptr %e_arch_slot_a0, align 4
  %row_arr_a0_slot = getelementptr inbounds nuw i8, ptr %0, i64 32
  %row_arr_a0 = load ptr, ptr %row_arr_a0_slot, align 8
  %e_row_slot_a0 = getelementptr inbounds i32, ptr %row_arr_a0, i64 %2
  store i32 %row, ptr %e_row_slot_a0, align 4
  br label %exit_a0
}

; Function Attrs: nounwind
define i32 @world_spawn(ptr nocapture %0) local_unnamed_addr #4 {
entry:
  %ent_count_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 16
  %ent_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 20
  %ent_arch_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 32
  %cur_ent_count.i = load i32, ptr %ent_count_slot.i, align 4
  %cur_ent_cap.i = load i32, ptr %ent_cap_slot.i, align 4
  %need_grow_ent.not.i = icmp slt i32 %cur_ent_count.i, %cur_ent_cap.i
  br i1 %need_grow_ent.not.i, label %world_alloc_entity.exit, label %grow_ent.i

grow_ent.i:                                       ; preds = %entry
  %ent_cap_zero.i = icmp eq i32 %cur_ent_cap.i, 0
  %double_ent_cap.i = shl i32 %cur_ent_cap.i, 1
  %new_ent_cap.i = select i1 %ent_cap_zero.i, i32 64, i32 %double_ent_cap.i
  store i32 %new_ent_cap.i, ptr %ent_cap_slot.i, align 4
  %new_ent_cap64.i = zext i32 %new_ent_cap.i to i64
  %bytes_for_ent.i = shl nuw nsw i64 %new_ent_cap64.i, 2
  %cur_arch_arr.i = load ptr, ptr %ent_arch_slot.i, align 8
  %new_arch_i8.i = tail call ptr @realloc(ptr %cur_arch_arr.i, i64 %bytes_for_ent.i)
  store ptr %new_arch_i8.i, ptr %ent_arch_slot.i, align 8
  %cur_row_arr.i = load ptr, ptr %ent_row_slot.i, align 8
  %new_row_i8.i = tail call ptr @realloc(ptr %cur_row_arr.i, i64 %bytes_for_ent.i)
  store ptr %new_row_i8.i, ptr %ent_row_slot.i, align 8
  br label %world_alloc_entity.exit

world_alloc_entity.exit:                          ; preds = %entry, %grow_ent.i
  %next_ent_cnt.i = add i32 %cur_ent_count.i, 1
  store i32 %next_ent_cnt.i, ptr %ent_count_slot.i, align 4
  %cur_arch_arr_alloc.i = load ptr, ptr %ent_arch_slot.i, align 8
  %1 = sext i32 %cur_ent_count.i to i64
  %e_arch_slot_alloc.i = getelementptr inbounds i32, ptr %cur_arch_arr_alloc.i, i64 %1
  store i32 -1, ptr %e_arch_slot_alloc.i, align 4
  %cur_row_arr_alloc.i = load ptr, ptr %ent_row_slot.i, align 8
  %e_row_slot_alloc.i = getelementptr inbounds i32, ptr %cur_row_arr_alloc.i, i64 %1
  store i32 -1, ptr %e_row_slot_alloc.i, align 4
  tail call void @world_assign_a0(ptr nonnull %0, i32 %cur_ent_count.i)
  ret i32 %cur_ent_count.i
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(readwrite, inaccessiblemem: none)
define void @world_despawn(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #7 {
entry:
  %ent_count_slot_ds = getelementptr inbounds nuw i8, ptr %0, i64 16
  %total_ents_ds = load i32, ptr %ent_count_slot_ds, align 4
  %e_non_neg_ds = icmp sgt i32 %1, -1
  %e_in_bounds_ds = icmp slt i32 %1, %total_ents_ds
  %is_valid_id_ds = and i1 %e_non_neg_ds, %e_in_bounds_ds
  br i1 %is_valid_id_ds, label %check_arch, label %ds_exit

check_arch:                                       ; preds = %entry
  %ent_arch_slot_ds = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_ds = load ptr, ptr %ent_arch_slot_ds, align 8
  %2 = zext nneg i32 %1 to i64
  %e_arch_slot_ds_inst = getelementptr inbounds nuw i32, ptr %arch_arr_ds, i64 %2
  %cur_arch_idx_ds = load i32, ptr %e_arch_slot_ds_inst, align 4
  %is_alive_ds = icmp sgt i32 %cur_arch_idx_ds, -1
  br i1 %is_alive_ds, label %do_despawn, label %ds_exit

ds_exit:                                          ; preds = %after_swap_ds, %check_arch, %entry
  ret void

do_despawn:                                       ; preds = %check_arch
  %tables_slot_ds = getelementptr inbounds nuw i8, ptr %0, i64 8
  %ent_row_slot_ds = getelementptr inbounds nuw i8, ptr %0, i64 32
  %row_arr_ds = load ptr, ptr %ent_row_slot_ds, align 8
  %e_row_slot_ds_inst = getelementptr inbounds nuw i32, ptr %row_arr_ds, i64 %2
  %cur_row_ds = load i32, ptr %e_row_slot_ds_inst, align 4
  %tables_base_ds = load ptr, ptr %tables_slot_ds, align 8
  %3 = zext nneg i32 %cur_arch_idx_ds to i64
  %cur_arch_ptr_ds = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base_ds, i64 %3
  %cur_cnt_slot_ds = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 8
  %cur_arch_cnt_ds = load i32, ptr %cur_cnt_slot_ds, align 4
  %last_row_ds = add i32 %cur_arch_cnt_ds, -1
  store i32 %last_row_ds, ptr %cur_cnt_slot_ds, align 4
  %is_last_row_ds = icmp eq i32 %cur_row_ds, %last_row_ds
  br i1 %is_last_row_ds, label %after_swap_ds, label %do_swap_ds

do_swap_ds:                                       ; preds = %do_despawn
  %cur_ent_slot_ds = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 16
  %cur_ent_raw_ds = load ptr, ptr %cur_ent_slot_ds, align 8
  %4 = sext i32 %last_row_ds to i64
  %last_ent_elem_ds = getelementptr inbounds i32, ptr %cur_ent_raw_ds, i64 %4
  %moved_e_ds = load i32, ptr %last_ent_elem_ds, align 4
  %5 = sext i32 %cur_row_ds to i64
  %cur_ent_elem_ds = getelementptr inbounds i32, ptr %cur_ent_raw_ds, i64 %5
  store i32 %moved_e_ds, ptr %cur_ent_elem_ds, align 4
  %cur_mask_ds = load i64, ptr %cur_arch_ptr_ds, align 8
  %has_sw_ds_ChildOf = and i64 %cur_mask_ds, 1
  %is_has_sw_ds_ChildOf.not = icmp eq i64 %has_sw_ds_ChildOf, 0
  br i1 %is_has_sw_ds_ChildOf.not, label %skip_sw_ds_ChildOf, label %swap_ds_ChildOf

after_swap_ds:                                    ; preds = %skip_sw_ds_Obstacle, %do_despawn
  store i32 -1, ptr %e_arch_slot_ds_inst, align 4
  store i32 -1, ptr %e_row_slot_ds_inst, align 4
  br label %ds_exit

swap_ds_ChildOf:                                  ; preds = %do_swap_ds
  %cur_cols_arr_ds = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 24
  %sw_raw_ds_ChildOf = load ptr, ptr %cur_cols_arr_ds, align 8
  %sw_src_ds_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf, i64 %4
  %sw_dst_ds_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf, i64 %5
  %6 = load i32, ptr %sw_src_ds_ChildOf, align 1
  store i32 %6, ptr %sw_dst_ds_ChildOf, align 1
  br label %skip_sw_ds_ChildOf

skip_sw_ds_ChildOf:                               ; preds = %swap_ds_ChildOf, %do_swap_ds
  %has_sw_ds_Position = and i64 %cur_mask_ds, 2
  %is_has_sw_ds_Position.not = icmp eq i64 %has_sw_ds_Position, 0
  br i1 %is_has_sw_ds_Position.not, label %skip_sw_ds_Position, label %swap_ds_Position

swap_ds_Position:                                 ; preds = %skip_sw_ds_ChildOf
  %sw_col_ds_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 32
  %sw_raw_ds_Position = load ptr, ptr %sw_col_ds_Position, align 8
  %sw_src_ds_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position, i64 %4
  %sw_dst_ds_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position, i64 %5
  %7 = load i64, ptr %sw_src_ds_Position, align 1
  store i64 %7, ptr %sw_dst_ds_Position, align 1
  br label %skip_sw_ds_Position

skip_sw_ds_Position:                              ; preds = %swap_ds_Position, %skip_sw_ds_ChildOf
  %has_sw_ds_Velocity = and i64 %cur_mask_ds, 4
  %is_has_sw_ds_Velocity.not = icmp eq i64 %has_sw_ds_Velocity, 0
  br i1 %is_has_sw_ds_Velocity.not, label %skip_sw_ds_Velocity, label %swap_ds_Velocity

swap_ds_Velocity:                                 ; preds = %skip_sw_ds_Position
  %sw_col_ds_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 40
  %sw_raw_ds_Velocity = load ptr, ptr %sw_col_ds_Velocity, align 8
  %sw_src_ds_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_ds_Velocity, i64 %4
  %sw_dst_ds_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_ds_Velocity, i64 %5
  %8 = load i64, ptr %sw_src_ds_Velocity, align 1
  store i64 %8, ptr %sw_dst_ds_Velocity, align 1
  br label %skip_sw_ds_Velocity

skip_sw_ds_Velocity:                              ; preds = %swap_ds_Velocity, %skip_sw_ds_Position
  %has_sw_ds_PlayerTag = and i64 %cur_mask_ds, 8
  %is_has_sw_ds_PlayerTag.not = icmp eq i64 %has_sw_ds_PlayerTag, 0
  br i1 %is_has_sw_ds_PlayerTag.not, label %skip_sw_ds_PlayerTag, label %swap_ds_PlayerTag

swap_ds_PlayerTag:                                ; preds = %skip_sw_ds_Velocity
  %sw_col_ds_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 48
  %sw_raw_ds_PlayerTag = load ptr, ptr %sw_col_ds_PlayerTag, align 8
  %sw_src_ds_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_ds_PlayerTag, i64 %4
  %sw_dst_ds_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_ds_PlayerTag, i64 %5
  %9 = load i32, ptr %sw_src_ds_PlayerTag, align 1
  store i32 %9, ptr %sw_dst_ds_PlayerTag, align 1
  br label %skip_sw_ds_PlayerTag

skip_sw_ds_PlayerTag:                             ; preds = %swap_ds_PlayerTag, %skip_sw_ds_Velocity
  %has_sw_ds_Obstacle = and i64 %cur_mask_ds, 16
  %is_has_sw_ds_Obstacle.not = icmp eq i64 %has_sw_ds_Obstacle, 0
  br i1 %is_has_sw_ds_Obstacle.not, label %skip_sw_ds_Obstacle, label %swap_ds_Obstacle

swap_ds_Obstacle:                                 ; preds = %skip_sw_ds_PlayerTag
  %sw_col_ds_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds, i64 56
  %sw_raw_ds_Obstacle = load ptr, ptr %sw_col_ds_Obstacle, align 8
  %sw_src_ds_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_ds_Obstacle, i64 %4
  %sw_dst_ds_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_ds_Obstacle, i64 %5
  %10 = load i8, ptr %sw_src_ds_Obstacle, align 1
  store i8 %10, ptr %sw_dst_ds_Obstacle, align 1
  br label %skip_sw_ds_Obstacle

skip_sw_ds_Obstacle:                              ; preds = %swap_ds_Obstacle, %skip_sw_ds_PlayerTag
  %11 = sext i32 %moved_e_ds to i64
  %moved_e_row_slot_ds = getelementptr inbounds i32, ptr %row_arr_ds, i64 %11
  store i32 %cur_row_ds, ptr %moved_e_row_slot_ds, align 4
  br label %after_swap_ds
}

; Function Attrs: mustprogress nounwind willreturn
define void @world_cmd_ensure_cap(ptr nocapture %0, i32 %1) local_unnamed_addr #5 {
entry:
  %cmd_cnt_slot_ec = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt = load i32, ptr %cmd_cnt_slot_ec, align 4
  %cur_cmd_cap = load i32, ptr %cmd_cap_slot_ec, align 4
  %needed_total = add i32 %cur_cmd_cnt, %1
  %need_grow_cmd = icmp sgt i32 %needed_total, %cur_cmd_cap
  br i1 %need_grow_cmd, label %grow_cmd, label %ec_exit

grow_cmd:                                         ; preds = %entry
  %cmd_data_slot_ec = getelementptr inbounds nuw i8, ptr %0, i64 48
  %double_cmd_cap = shl i32 %cur_cmd_cap, 1
  %at_least_1k = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap, i32 %needed_total)
  %final_cap = tail call i32 @llvm.smax.i32(i32 %at_least_1k, i32 1024)
  store i32 %final_cap, ptr %cmd_cap_slot_ec, align 4
  %final_cap64 = zext nneg i32 %final_cap to i64
  %cur_cmd_data = load ptr, ptr %cmd_data_slot_ec, align 8
  %new_cmd_data = tail call ptr @realloc(ptr %cur_cmd_data, i64 %final_cap64)
  store ptr %new_cmd_data, ptr %cmd_data_slot_ec, align 8
  br label %ec_exit

ec_exit:                                          ; preds = %grow_cmd, %entry
  ret void
}

define i32 @world_cmd_spawn(ptr %0) local_unnamed_addr {
entry:
  %cmd_lock_slot_cs = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cs)
  %ent_count_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 16
  %ent_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 20
  %ent_arch_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 32
  %cur_ent_count.i = load i32, ptr %ent_count_slot.i, align 4
  %cur_ent_cap.i = load i32, ptr %ent_cap_slot.i, align 4
  %need_grow_ent.not.i = icmp slt i32 %cur_ent_count.i, %cur_ent_cap.i
  br i1 %need_grow_ent.not.i, label %world_alloc_entity.exit, label %grow_ent.i

grow_ent.i:                                       ; preds = %entry
  %ent_cap_zero.i = icmp eq i32 %cur_ent_cap.i, 0
  %double_ent_cap.i = shl i32 %cur_ent_cap.i, 1
  %new_ent_cap.i = select i1 %ent_cap_zero.i, i32 64, i32 %double_ent_cap.i
  store i32 %new_ent_cap.i, ptr %ent_cap_slot.i, align 4
  %new_ent_cap64.i = zext i32 %new_ent_cap.i to i64
  %bytes_for_ent.i = shl nuw nsw i64 %new_ent_cap64.i, 2
  %cur_arch_arr.i = load ptr, ptr %ent_arch_slot.i, align 8
  %new_arch_i8.i = tail call ptr @realloc(ptr %cur_arch_arr.i, i64 %bytes_for_ent.i)
  store ptr %new_arch_i8.i, ptr %ent_arch_slot.i, align 8
  %cur_row_arr.i = load ptr, ptr %ent_row_slot.i, align 8
  %new_row_i8.i = tail call ptr @realloc(ptr %cur_row_arr.i, i64 %bytes_for_ent.i)
  store ptr %new_row_i8.i, ptr %ent_row_slot.i, align 8
  br label %world_alloc_entity.exit

world_alloc_entity.exit:                          ; preds = %entry, %grow_ent.i
  %next_ent_cnt.i = add i32 %cur_ent_count.i, 1
  store i32 %next_ent_cnt.i, ptr %ent_count_slot.i, align 4
  %cur_arch_arr_alloc.i = load ptr, ptr %ent_arch_slot.i, align 8
  %1 = sext i32 %cur_ent_count.i to i64
  %e_arch_slot_alloc.i = getelementptr inbounds i32, ptr %cur_arch_arr_alloc.i, i64 %1
  store i32 -1, ptr %e_arch_slot_alloc.i, align 4
  %cur_row_arr_alloc.i = load ptr, ptr %ent_row_slot.i, align 8
  %e_row_slot_alloc.i = getelementptr inbounds i32, ptr %cur_row_arr_alloc.i, i64 %1
  store i32 -1, ptr %e_row_slot_alloc.i, align 4
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 8
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge

world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge: ; preds = %world_alloc_entity.exit
  %data_ptr_cs.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %world_alloc_entity.exit
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cs.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cs.pre, 8
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cs.pre-phi = phi i32 [ %needed_total.i, %world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cs = phi ptr [ %data_ptr_cs.pre, %world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cs = phi i32 [ %cur_cmd_cnt.i, %world_alloc_entity.exit.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cs.pre, %grow_cmd.i ]
  %cur_cnt_cs64 = zext i32 %cur_cnt_cs to i64
  %write_ptr_cs = getelementptr inbounds nuw i8, ptr %data_ptr_cs, i64 %cur_cnt_cs64
  store i32 1, ptr %write_ptr_cs, align 4
  %e_slot_cs = getelementptr inbounds nuw i8, ptr %write_ptr_cs, i64 4
  store i32 %cur_ent_count.i, ptr %e_slot_cs, align 4
  store i32 %new_cnt_cs.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cs)
  ret i32 %cur_ent_count.i
}

define void @world_cmd_despawn(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_cd = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cd)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 8
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cd.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cd.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cd.pre, 8
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cd.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cd = phi ptr [ %data_ptr_cd.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cd = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cd.pre, %grow_cmd.i ]
  %cur_cnt_cd64 = zext i32 %cur_cnt_cd to i64
  %write_ptr_cd = getelementptr inbounds nuw i8, ptr %data_ptr_cd, i64 %cur_cnt_cd64
  store i32 2, ptr %write_ptr_cd, align 4
  %e_slot_cd = getelementptr inbounds nuw i8, ptr %write_ptr_cd, i64 4
  store i32 %1, ptr %e_slot_cd, align 4
  store i32 %new_cnt_cd.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cd)
  ret void
}

; Function Attrs: nounwind
define void @world_add_ChildOf(ptr nocapture %0, i32 %1, i32 %2) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %3 = sext i32 %1 to i64
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i64 %3
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_assign_a0, label %set_cont

set_assign_a0:                                    ; preds = %entry
  tail call void @world_assign_a0(ptr nonnull %0, i32 %1)
  %cur_arch_idx.pre = load i32, ptr %ent_arch_slot, align 4
  br label %set_cont

set_cont:                                         ; preds = %set_assign_a0, %entry
  %cur_arch_idx = phi i32 [ %cur_arch_idx.pre, %set_assign_a0 ], [ %cur_arch_idx_raw, %entry ]
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i64 %3
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %4 = sext i32 %cur_arch_idx to i64
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i64 %4
  %cur_mask = load i64, ptr %cur_arch_ptr, align 8
  %has_bit = and i64 %cur_mask, 1
  %already_has.not = icmp eq i64 %has_bit, 0
  br i1 %already_has.not, label %transition, label %set_cont.store_fields_crit_edge

set_cont.store_fields_crit_edge:                  ; preds = %set_cont
  %.pre9 = sext i32 %cur_row to i64
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or disjoint i64 %cur_mask, 1
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %transition
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_set, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %transition
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %5 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_set, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_set, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_set, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %6 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %6
  store i64 %new_mask, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_tr1.pre = load ptr, ptr %tables_slot_set, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi8 = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %6, %init_arch.i ]
  %tables_tr1 = phi ptr [ %tables_set, %common.ret.loopexit.i ], [ %tables_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %5, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i64 %.pre-phi8
  %new_cnt_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 8
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 12
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new.not = icmp slt i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new.not, label %after_grow_new_arch, label %grow_new_arch

store_fields:                                     ; preds = %set_cont.store_fields_crit_edge, %after_swap_remove
  %.pre-phi10 = phi i64 [ %.pre9, %set_cont.store_fields_crit_edge ], [ %7, %after_swap_remove ]
  %.pre-phi = phi i64 [ %4, %set_cont.store_fields_crit_edge ], [ %.pre-phi8, %after_swap_remove ]
  %latest_tables_sf = phi ptr [ %tables_set, %set_cont.store_fields_crit_edge ], [ %latest_tables_sf.pre, %after_swap_remove ]
  %final_cols = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i64 %.pre-phi, i32 4
  %final_col_raw = load ptr, ptr %final_cols, align 8
  %final_elem = getelementptr inbounds %struct.ChildOf, ptr %final_col_raw, i64 %.pre-phi10
  store i32 %2, ptr %final_elem, align 4
  ret void

grow_new_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_tr2.pre = load ptr, ptr %tables_slot_set, align 8
  %new_cnt_slot2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_tr2.pre, i64 %.pre-phi8, i32 1
  %new_row.pre = load i32, ptr %new_cnt_slot2.phi.trans.insert, align 4
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %world_get_or_create_archetype.exit
  %new_row = phi i32 [ %new_row.pre, %grow_new_arch ], [ %new_cnt1, %world_get_or_create_archetype.exit ]
  %tables_tr2 = phi ptr [ %tables_tr2.pre, %grow_new_arch ], [ %tables_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %4
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %.pre-phi8
  %new_cnt_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 8
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 16
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %7 = sext i32 %new_row to i64
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i64 %7
  store i32 %1, ptr %new_ent_elem2, align 4
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position.not = icmp eq i64 %has_Position, 0
  br i1 %is_has_Position.not, label %skip_Position, label %copy_Position

copy_Position:                                    ; preds = %after_grow_new_arch
  %src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %8 = sext i32 %cur_row to i64
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i64 %8
  %dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 32
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i64 %7
  %9 = load i64, ptr %src_elem_Position, align 1
  store i64 %9, ptr %dst_elem_Position, align 1
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %after_grow_new_arch
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity.not = icmp eq i64 %has_Velocity, 0
  br i1 %is_has_Velocity.not, label %skip_Velocity, label %copy_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %10 = sext i32 %cur_row to i64
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i64 %10
  %dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 40
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i64 %7
  %11 = load i64, ptr %src_elem_Velocity, align 1
  store i64 %11, ptr %dst_elem_Velocity, align 1
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_PlayerTag = and i64 %cur_mask, 8
  %is_has_PlayerTag.not = icmp eq i64 %has_PlayerTag, 0
  br i1 %is_has_PlayerTag.not, label %skip_PlayerTag, label %copy_PlayerTag

copy_PlayerTag:                                   ; preds = %skip_Velocity
  %src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %src_raw_PlayerTag = load ptr, ptr %src_col_PlayerTag, align 8
  %12 = sext i32 %cur_row to i64
  %src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %src_raw_PlayerTag, i64 %12
  %dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 48
  %dst_raw_PlayerTag = load ptr, ptr %dst_col_PlayerTag, align 8
  %dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %dst_raw_PlayerTag, i64 %7
  %13 = load i32, ptr %src_elem_PlayerTag, align 1
  store i32 %13, ptr %dst_elem_PlayerTag, align 1
  br label %skip_PlayerTag

skip_PlayerTag:                                   ; preds = %copy_PlayerTag, %skip_Velocity
  %has_Obstacle = and i64 %cur_mask, 16
  %is_has_Obstacle.not = icmp eq i64 %has_Obstacle, 0
  br i1 %is_has_Obstacle.not, label %skip_Obstacle, label %copy_Obstacle

copy_Obstacle:                                    ; preds = %skip_PlayerTag
  %src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %src_raw_Obstacle = load ptr, ptr %src_col_Obstacle, align 8
  %14 = sext i32 %cur_row to i64
  %src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %src_raw_Obstacle, i64 %14
  %dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 56
  %dst_raw_Obstacle = load ptr, ptr %dst_col_Obstacle, align 8
  %dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %dst_raw_Obstacle, i64 %7
  %15 = load i8, ptr %src_elem_Obstacle, align 1
  store i8 %15, ptr %dst_elem_Obstacle, align 1
  br label %skip_Obstacle

skip_Obstacle:                                    ; preds = %copy_Obstacle, %skip_PlayerTag
  %cur_cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 8
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = add i32 %cur_arch_count, -1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Obstacle
  %cur_ent_sr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 16
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %16 = sext i32 %last_row to i64
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %16
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %17 = sext i32 %cur_row to i64
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %17
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  br i1 %is_has_Position.not, label %skip_sw_Position, label %swap_Position

after_swap_remove:                                ; preds = %skip_sw_Obstacle, %skip_Obstacle
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i64 %3
  store i32 %common.ret.op.i, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i64 %3
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  %latest_tables_sf.pre = load ptr, ptr %tables_slot_set, align 8
  br label %store_fields

swap_Position:                                    ; preds = %do_swap_remove
  %sw_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %16
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %17
  %18 = load i64, ptr %sw_src_Position, align 1
  store i64 %18, ptr %sw_dst_Position, align 1
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %do_swap_remove
  br i1 %is_has_Velocity.not, label %skip_sw_Velocity, label %swap_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %16
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %17
  %19 = load i64, ptr %sw_src_Velocity, align 1
  store i64 %19, ptr %sw_dst_Velocity, align 1
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  br i1 %is_has_PlayerTag.not, label %skip_sw_PlayerTag, label %swap_PlayerTag

swap_PlayerTag:                                   ; preds = %skip_sw_Velocity
  %sw_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %sw_raw_PlayerTag = load ptr, ptr %sw_col_PlayerTag, align 8
  %sw_src_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %16
  %sw_dst_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %17
  %20 = load i32, ptr %sw_src_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_PlayerTag, align 1
  br label %skip_sw_PlayerTag

skip_sw_PlayerTag:                                ; preds = %swap_PlayerTag, %skip_sw_Velocity
  br i1 %is_has_Obstacle.not, label %skip_sw_Obstacle, label %swap_Obstacle

swap_Obstacle:                                    ; preds = %skip_sw_PlayerTag
  %sw_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %sw_raw_Obstacle = load ptr, ptr %sw_col_Obstacle, align 8
  %sw_src_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %16
  %sw_dst_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %17
  %21 = load i8, ptr %sw_src_Obstacle, align 1
  store i8 %21, ptr %sw_dst_Obstacle, align 1
  br label %skip_sw_Obstacle

skip_sw_Obstacle:                                 ; preds = %swap_Obstacle, %skip_sw_PlayerTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %22 = sext i32 %moved_e to i64
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i64 %22
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

; Function Attrs: nounwind
define void @world_remove_ChildOf(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %2 = sext i32 %1 to i64
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i64 %2
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i64 %2
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %3 = sext i32 %cur_arch_rem to i64
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i64 %3
  %cur_mask_val_rem = load i64, ptr %cur_arch_ptr_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 1
  %has_comp_rem.not = icmp eq i64 %rem_has_bit, 0
  br i1 %has_comp_rem.not, label %exit_remove, label %do_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -2
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_remove
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_rem, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask_rem
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_remove
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %4 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_rem, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_rem, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_rem, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %5 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %5
  store i64 %new_mask_rem, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_rem_tr1.pre = load ptr, ptr %tables_slot_rem, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %5, %init_arch.i ]
  %tables_rem_tr1 = phi ptr [ %tables_rem, %common.ret.loopexit.i ], [ %tables_rem_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %4, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i64 %.pre-phi
  %cnt_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 8
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 12
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem.not = icmp slt i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem.not, label %after_grow_rem_arch, label %grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
  ret void

grow_rem_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_rem_tr2.pre = load ptr, ptr %tables_slot_rem, align 8
  %cnt_slot_rem2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2.pre, i64 %.pre-phi, i32 1
  %new_row_rem.pre = load i32, ptr %cnt_slot_rem2.phi.trans.insert, align 4
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %world_get_or_create_archetype.exit
  %new_row_rem = phi i32 [ %new_row_rem.pre, %grow_rem_arch ], [ %cnt_rem1, %world_get_or_create_archetype.exit ]
  %tables_rem_tr2 = phi ptr [ %tables_rem_tr2.pre, %grow_rem_arch ], [ %tables_rem_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %3
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %.pre-phi
  %cnt_slot_rem2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 8
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 16
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %6 = sext i32 %new_row_rem to i64
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i64 %6
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 24
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position.not = icmp eq i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position.not, label %skip_rem_Position, label %copy_rem_Position

copy_rem_Position:                                ; preds = %after_grow_rem_arch
  %rem_src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %7 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i64 %7
  %rem_dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 32
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i64 %6
  %8 = load i64, ptr %rem_src_elem_Position, align 1
  store i64 %8, ptr %rem_dst_elem_Position, align 1
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %after_grow_rem_arch
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity.not = icmp eq i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity.not, label %skip_rem_Velocity, label %copy_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_Position
  %rem_src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %9 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i64 %9
  %rem_dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 40
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i64 %6
  %10 = load i64, ptr %rem_src_elem_Velocity, align 1
  store i64 %10, ptr %rem_dst_elem_Velocity, align 1
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_Position
  %rem_has_PlayerTag = and i64 %cur_mask_val_rem, 8
  %is_has_rem_PlayerTag.not = icmp eq i64 %rem_has_PlayerTag, 0
  br i1 %is_has_rem_PlayerTag.not, label %skip_rem_PlayerTag, label %copy_rem_PlayerTag

copy_rem_PlayerTag:                               ; preds = %skip_rem_Velocity
  %rem_src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %rem_src_raw_PlayerTag = load ptr, ptr %rem_src_col_PlayerTag, align 8
  %11 = sext i32 %cur_row_rem to i64
  %rem_src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_src_raw_PlayerTag, i64 %11
  %rem_dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 48
  %rem_dst_raw_PlayerTag = load ptr, ptr %rem_dst_col_PlayerTag, align 8
  %rem_dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_dst_raw_PlayerTag, i64 %6
  %12 = load i32, ptr %rem_src_elem_PlayerTag, align 1
  store i32 %12, ptr %rem_dst_elem_PlayerTag, align 1
  br label %skip_rem_PlayerTag

skip_rem_PlayerTag:                               ; preds = %copy_rem_PlayerTag, %skip_rem_Velocity
  %rem_has_Obstacle = and i64 %cur_mask_val_rem, 16
  %is_has_rem_Obstacle.not = icmp eq i64 %rem_has_Obstacle, 0
  br i1 %is_has_rem_Obstacle.not, label %skip_rem_Obstacle, label %copy_rem_Obstacle

copy_rem_Obstacle:                                ; preds = %skip_rem_PlayerTag
  %rem_src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %rem_src_raw_Obstacle = load ptr, ptr %rem_src_col_Obstacle, align 8
  %13 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_src_raw_Obstacle, i64 %13
  %rem_dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 56
  %rem_dst_raw_Obstacle = load ptr, ptr %rem_dst_col_Obstacle, align 8
  %rem_dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_dst_raw_Obstacle, i64 %6
  %14 = load i8, ptr %rem_src_elem_Obstacle, align 1
  store i8 %14, ptr %rem_dst_elem_Obstacle, align 1
  br label %skip_rem_Obstacle

skip_rem_Obstacle:                                ; preds = %copy_rem_Obstacle, %skip_rem_PlayerTag
  %cnt_slot_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 8
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = add i32 %cnt_rem, -1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Obstacle
  %ent_sr_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 16
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %15 = sext i32 %last_row_rem to i64
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %15
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %16 = sext i32 %cur_row_rem to i64
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %16
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  %sw_raw_rem_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %15
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %16
  %17 = load i32, ptr %sw_src_rem_ChildOf, align 1
  store i32 %17, ptr %sw_dst_rem_ChildOf, align 1
  br i1 %is_has_rem_Position.not, label %skip_sw_rem_Position, label %swap_rem_Position

after_swap_rem:                                   ; preds = %skip_sw_rem_Obstacle, %skip_rem_Obstacle
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i64 %2
  store i32 %common.ret.op.i, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i64 %2
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_Position:                                ; preds = %do_swap_rem
  %sw_col_rem_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %15
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %16
  %18 = load i64, ptr %sw_src_rem_Position, align 1
  store i64 %18, ptr %sw_dst_rem_Position, align 1
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %do_swap_rem
  br i1 %is_has_rem_Velocity.not, label %skip_sw_rem_Velocity, label %swap_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %15
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %16
  %19 = load i64, ptr %sw_src_rem_Velocity, align 1
  store i64 %19, ptr %sw_dst_rem_Velocity, align 1
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  br i1 %is_has_rem_PlayerTag.not, label %skip_sw_rem_PlayerTag, label %swap_rem_PlayerTag

swap_rem_PlayerTag:                               ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %sw_raw_rem_PlayerTag = load ptr, ptr %sw_col_rem_PlayerTag, align 8
  %sw_src_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %15
  %sw_dst_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %16
  %20 = load i32, ptr %sw_src_rem_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_rem_PlayerTag, align 1
  br label %skip_sw_rem_PlayerTag

skip_sw_rem_PlayerTag:                            ; preds = %swap_rem_PlayerTag, %skip_sw_rem_Velocity
  br i1 %is_has_rem_Obstacle.not, label %skip_sw_rem_Obstacle, label %swap_rem_Obstacle

swap_rem_Obstacle:                                ; preds = %skip_sw_rem_PlayerTag
  %sw_col_rem_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %sw_raw_rem_Obstacle = load ptr, ptr %sw_col_rem_Obstacle, align 8
  %sw_src_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %15
  %sw_dst_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %16
  %21 = load i8, ptr %sw_src_rem_Obstacle, align 1
  store i8 %21, ptr %sw_dst_rem_Obstacle, align 1
  br label %skip_sw_rem_Obstacle

skip_sw_rem_Obstacle:                             ; preds = %swap_rem_Obstacle, %skip_sw_rem_PlayerTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %22 = sext i32 %moved_e_rem to i64
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i64 %22
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none)
define i1 @world_has_ChildOf(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #8 {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %2 = sext i32 %1 to i64
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i64 %2
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sgt i32 %cur_arch_idx_has, -1
  br i1 %is_alive_has, label %check_mask, label %common.ret

common.ret:                                       ; preds = %entry, %check_mask
  %common.ret.op = phi i1 [ %res_has, %check_mask ], [ false, %entry ]
  ret i1 %common.ret.op

check_mask:                                       ; preds = %entry
  %tables_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %3 = zext nneg i32 %cur_arch_idx_has to i64
  %arch_ptr_has = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has, i64 %3
  %arch_mask_has = load i64, ptr %arch_ptr_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 1
  %res_has = icmp ne i64 %bit_and_has, 0
  br label %common.ret
}

define void @world_cmd_add_ChildOf(ptr %0, i32 %1, i32 %2) local_unnamed_addr {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 16
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cset.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cset.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cset.pre, 16
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cset.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cset = phi ptr [ %data_ptr_cset.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cset = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cset.pre, %grow_cmd.i ]
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds nuw i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  store i32 3, ptr %write_ptr_cset, align 4
  %e_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 4
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 8
  store i32 0, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 12
  store i32 %2, ptr %payload_raw, align 4
  store i32 %new_cnt_cset.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_ChildOf(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 12
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_crem.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_crem.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_crem.pre, 12
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_crem.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_crem = phi ptr [ %data_ptr_crem.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_crem = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_crem.pre, %grow_cmd.i ]
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds nuw i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  store i32 4, ptr %write_ptr_crem, align 4
  %e_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 4
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 8
  store i32 0, ptr %comp_id_slot_crem, align 4
  store i32 %new_cnt_crem.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  ret void
}

; Function Attrs: nounwind
define void @world_add_Position(ptr nocapture %0, i32 %1, float %2, float %3) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %4 = sext i32 %1 to i64
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i64 %4
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_assign_a0, label %set_cont

set_assign_a0:                                    ; preds = %entry
  tail call void @world_assign_a0(ptr nonnull %0, i32 %1)
  %cur_arch_idx.pre = load i32, ptr %ent_arch_slot, align 4
  br label %set_cont

set_cont:                                         ; preds = %set_assign_a0, %entry
  %cur_arch_idx = phi i32 [ %cur_arch_idx.pre, %set_assign_a0 ], [ %cur_arch_idx_raw, %entry ]
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i64 %4
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %5 = sext i32 %cur_arch_idx to i64
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i64 %5
  %cur_mask = load i64, ptr %cur_arch_ptr, align 8
  %has_bit = and i64 %cur_mask, 2
  %already_has.not = icmp eq i64 %has_bit, 0
  br i1 %already_has.not, label %transition, label %set_cont.store_fields_crit_edge

set_cont.store_fields_crit_edge:                  ; preds = %set_cont
  %.pre9 = sext i32 %cur_row to i64
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or disjoint i64 %cur_mask, 2
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %transition
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_set, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %transition
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %6 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_set, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_set, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_set, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %7 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %7
  store i64 %new_mask, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_tr1.pre = load ptr, ptr %tables_slot_set, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi8 = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %7, %init_arch.i ]
  %tables_tr1 = phi ptr [ %tables_set, %common.ret.loopexit.i ], [ %tables_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %6, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i64 %.pre-phi8
  %new_cnt_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 8
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 12
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new.not = icmp slt i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new.not, label %after_grow_new_arch, label %grow_new_arch

store_fields:                                     ; preds = %set_cont.store_fields_crit_edge, %after_swap_remove
  %.pre-phi10 = phi i64 [ %.pre9, %set_cont.store_fields_crit_edge ], [ %8, %after_swap_remove ]
  %.pre-phi = phi i64 [ %5, %set_cont.store_fields_crit_edge ], [ %.pre-phi8, %after_swap_remove ]
  %latest_tables_sf = phi ptr [ %tables_set, %set_cont.store_fields_crit_edge ], [ %latest_tables_sf.pre, %after_swap_remove ]
  %final_col_slot = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i64 %.pre-phi, i32 4, i64 1
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Position, ptr %final_col_raw, i64 %.pre-phi10
  store float %2, ptr %final_elem, align 4
  %y_gep = getelementptr inbounds nuw i8, ptr %final_elem, i64 4
  store float %3, ptr %y_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_tr2.pre = load ptr, ptr %tables_slot_set, align 8
  %new_cnt_slot2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_tr2.pre, i64 %.pre-phi8, i32 1
  %new_row.pre = load i32, ptr %new_cnt_slot2.phi.trans.insert, align 4
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %world_get_or_create_archetype.exit
  %new_row = phi i32 [ %new_row.pre, %grow_new_arch ], [ %new_cnt1, %world_get_or_create_archetype.exit ]
  %tables_tr2 = phi ptr [ %tables_tr2.pre, %grow_new_arch ], [ %tables_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %5
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %.pre-phi8
  %new_cnt_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 8
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 16
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %8 = sext i32 %new_row to i64
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i64 %8
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 24
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf.not = icmp eq i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf.not, label %skip_Position, label %copy_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %new_cols_arr = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 24
  %src_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %9 = sext i32 %cur_row to i64
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i64 %9
  %dst_raw_ChildOf = load ptr, ptr %new_cols_arr, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i64 %8
  %10 = load i32, ptr %src_elem_ChildOf, align 1
  store i32 %10, ptr %dst_elem_ChildOf, align 1
  br label %skip_Position

skip_Position:                                    ; preds = %after_grow_new_arch, %copy_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity.not = icmp eq i64 %has_Velocity, 0
  br i1 %is_has_Velocity.not, label %skip_Velocity, label %copy_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %11 = sext i32 %cur_row to i64
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i64 %11
  %dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 40
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i64 %8
  %12 = load i64, ptr %src_elem_Velocity, align 1
  store i64 %12, ptr %dst_elem_Velocity, align 1
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_PlayerTag = and i64 %cur_mask, 8
  %is_has_PlayerTag.not = icmp eq i64 %has_PlayerTag, 0
  br i1 %is_has_PlayerTag.not, label %skip_PlayerTag, label %copy_PlayerTag

copy_PlayerTag:                                   ; preds = %skip_Velocity
  %src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %src_raw_PlayerTag = load ptr, ptr %src_col_PlayerTag, align 8
  %13 = sext i32 %cur_row to i64
  %src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %src_raw_PlayerTag, i64 %13
  %dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 48
  %dst_raw_PlayerTag = load ptr, ptr %dst_col_PlayerTag, align 8
  %dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %dst_raw_PlayerTag, i64 %8
  %14 = load i32, ptr %src_elem_PlayerTag, align 1
  store i32 %14, ptr %dst_elem_PlayerTag, align 1
  br label %skip_PlayerTag

skip_PlayerTag:                                   ; preds = %copy_PlayerTag, %skip_Velocity
  %has_Obstacle = and i64 %cur_mask, 16
  %is_has_Obstacle.not = icmp eq i64 %has_Obstacle, 0
  br i1 %is_has_Obstacle.not, label %skip_Obstacle, label %copy_Obstacle

copy_Obstacle:                                    ; preds = %skip_PlayerTag
  %src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %src_raw_Obstacle = load ptr, ptr %src_col_Obstacle, align 8
  %15 = sext i32 %cur_row to i64
  %src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %src_raw_Obstacle, i64 %15
  %dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 56
  %dst_raw_Obstacle = load ptr, ptr %dst_col_Obstacle, align 8
  %dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %dst_raw_Obstacle, i64 %8
  %16 = load i8, ptr %src_elem_Obstacle, align 1
  store i8 %16, ptr %dst_elem_Obstacle, align 1
  br label %skip_Obstacle

skip_Obstacle:                                    ; preds = %copy_Obstacle, %skip_PlayerTag
  %cur_cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 8
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = add i32 %cur_arch_count, -1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Obstacle
  %cur_ent_sr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 16
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %17 = sext i32 %last_row to i64
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %17
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %18 = sext i32 %cur_row to i64
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %18
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  br i1 %is_has_ChildOf.not, label %skip_sw_Position, label %swap_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Obstacle, %skip_Obstacle
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i64 %4
  store i32 %common.ret.op.i, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i64 %4
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  %latest_tables_sf.pre = load ptr, ptr %tables_slot_set, align 8
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %17
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %18
  %19 = load i32, ptr %sw_src_ChildOf, align 1
  store i32 %19, ptr %sw_dst_ChildOf, align 1
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %do_swap_remove, %swap_ChildOf
  br i1 %is_has_Velocity.not, label %skip_sw_Velocity, label %swap_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %17
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %18
  %20 = load i64, ptr %sw_src_Velocity, align 1
  store i64 %20, ptr %sw_dst_Velocity, align 1
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  br i1 %is_has_PlayerTag.not, label %skip_sw_PlayerTag, label %swap_PlayerTag

swap_PlayerTag:                                   ; preds = %skip_sw_Velocity
  %sw_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %sw_raw_PlayerTag = load ptr, ptr %sw_col_PlayerTag, align 8
  %sw_src_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %17
  %sw_dst_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %18
  %21 = load i32, ptr %sw_src_PlayerTag, align 1
  store i32 %21, ptr %sw_dst_PlayerTag, align 1
  br label %skip_sw_PlayerTag

skip_sw_PlayerTag:                                ; preds = %swap_PlayerTag, %skip_sw_Velocity
  br i1 %is_has_Obstacle.not, label %skip_sw_Obstacle, label %swap_Obstacle

swap_Obstacle:                                    ; preds = %skip_sw_PlayerTag
  %sw_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %sw_raw_Obstacle = load ptr, ptr %sw_col_Obstacle, align 8
  %sw_src_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %17
  %sw_dst_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %18
  %22 = load i8, ptr %sw_src_Obstacle, align 1
  store i8 %22, ptr %sw_dst_Obstacle, align 1
  br label %skip_sw_Obstacle

skip_sw_Obstacle:                                 ; preds = %swap_Obstacle, %skip_sw_PlayerTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %23 = sext i32 %moved_e to i64
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i64 %23
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

; Function Attrs: nounwind
define void @world_remove_Position(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %2 = sext i32 %1 to i64
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i64 %2
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i64 %2
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %3 = sext i32 %cur_arch_rem to i64
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i64 %3
  %cur_mask_val_rem = load i64, ptr %cur_arch_ptr_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 2
  %has_comp_rem.not = icmp eq i64 %rem_has_bit, 0
  br i1 %has_comp_rem.not, label %exit_remove, label %do_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -3
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_remove
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_rem, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask_rem
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_remove
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %4 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_rem, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_rem, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_rem, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %5 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %5
  store i64 %new_mask_rem, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_rem_tr1.pre = load ptr, ptr %tables_slot_rem, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %5, %init_arch.i ]
  %tables_rem_tr1 = phi ptr [ %tables_rem, %common.ret.loopexit.i ], [ %tables_rem_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %4, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i64 %.pre-phi
  %cnt_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 8
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 12
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem.not = icmp slt i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem.not, label %after_grow_rem_arch, label %grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
  ret void

grow_rem_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_rem_tr2.pre = load ptr, ptr %tables_slot_rem, align 8
  %cnt_slot_rem2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2.pre, i64 %.pre-phi, i32 1
  %new_row_rem.pre = load i32, ptr %cnt_slot_rem2.phi.trans.insert, align 4
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %world_get_or_create_archetype.exit
  %new_row_rem = phi i32 [ %new_row_rem.pre, %grow_rem_arch ], [ %cnt_rem1, %world_get_or_create_archetype.exit ]
  %tables_rem_tr2 = phi ptr [ %tables_rem_tr2.pre, %grow_rem_arch ], [ %tables_rem_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %3
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %.pre-phi
  %cnt_slot_rem2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 8
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 16
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %6 = sext i32 %new_row_rem to i64
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i64 %6
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 24
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf.not = icmp eq i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf.not, label %skip_rem_ChildOf, label %copy_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %new_cols_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 24
  %rem_src_raw_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %7 = sext i32 %cur_row_rem to i64
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i64 %7
  %rem_dst_raw_ChildOf = load ptr, ptr %new_cols_rem, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i64 %6
  %8 = load i32, ptr %rem_src_elem_ChildOf, align 1
  store i32 %8, ptr %rem_dst_elem_ChildOf, align 1
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity.not = icmp eq i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity.not, label %skip_rem_Velocity, label %copy_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %9 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i64 %9
  %rem_dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 40
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i64 %6
  %10 = load i64, ptr %rem_src_elem_Velocity, align 1
  store i64 %10, ptr %rem_dst_elem_Velocity, align 1
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_ChildOf
  %rem_has_PlayerTag = and i64 %cur_mask_val_rem, 8
  %is_has_rem_PlayerTag.not = icmp eq i64 %rem_has_PlayerTag, 0
  br i1 %is_has_rem_PlayerTag.not, label %skip_rem_PlayerTag, label %copy_rem_PlayerTag

copy_rem_PlayerTag:                               ; preds = %skip_rem_Velocity
  %rem_src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %rem_src_raw_PlayerTag = load ptr, ptr %rem_src_col_PlayerTag, align 8
  %11 = sext i32 %cur_row_rem to i64
  %rem_src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_src_raw_PlayerTag, i64 %11
  %rem_dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 48
  %rem_dst_raw_PlayerTag = load ptr, ptr %rem_dst_col_PlayerTag, align 8
  %rem_dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_dst_raw_PlayerTag, i64 %6
  %12 = load i32, ptr %rem_src_elem_PlayerTag, align 1
  store i32 %12, ptr %rem_dst_elem_PlayerTag, align 1
  br label %skip_rem_PlayerTag

skip_rem_PlayerTag:                               ; preds = %copy_rem_PlayerTag, %skip_rem_Velocity
  %rem_has_Obstacle = and i64 %cur_mask_val_rem, 16
  %is_has_rem_Obstacle.not = icmp eq i64 %rem_has_Obstacle, 0
  br i1 %is_has_rem_Obstacle.not, label %skip_rem_Obstacle, label %copy_rem_Obstacle

copy_rem_Obstacle:                                ; preds = %skip_rem_PlayerTag
  %rem_src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %rem_src_raw_Obstacle = load ptr, ptr %rem_src_col_Obstacle, align 8
  %13 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_src_raw_Obstacle, i64 %13
  %rem_dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 56
  %rem_dst_raw_Obstacle = load ptr, ptr %rem_dst_col_Obstacle, align 8
  %rem_dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_dst_raw_Obstacle, i64 %6
  %14 = load i8, ptr %rem_src_elem_Obstacle, align 1
  store i8 %14, ptr %rem_dst_elem_Obstacle, align 1
  br label %skip_rem_Obstacle

skip_rem_Obstacle:                                ; preds = %copy_rem_Obstacle, %skip_rem_PlayerTag
  %cnt_slot_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 8
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = add i32 %cnt_rem, -1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Obstacle
  %ent_sr_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 16
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %15 = sext i32 %last_row_rem to i64
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %15
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %16 = sext i32 %cur_row_rem to i64
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %16
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  br i1 %is_has_rem_ChildOf.not, label %swap_rem_Position, label %swap_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Obstacle, %skip_rem_Obstacle
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i64 %2
  store i32 %common.ret.op.i, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i64 %2
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_raw_rem_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %15
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %16
  %17 = load i32, ptr %sw_src_rem_ChildOf, align 1
  store i32 %17, ptr %sw_dst_rem_ChildOf, align 1
  br label %swap_rem_Position

swap_rem_Position:                                ; preds = %do_swap_rem, %swap_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %15
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %16
  %18 = load i64, ptr %sw_src_rem_Position, align 1
  store i64 %18, ptr %sw_dst_rem_Position, align 1
  br i1 %is_has_rem_Velocity.not, label %skip_sw_rem_Velocity, label %swap_rem_Velocity

swap_rem_Velocity:                                ; preds = %swap_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %15
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %16
  %19 = load i64, ptr %sw_src_rem_Velocity, align 1
  store i64 %19, ptr %sw_dst_rem_Velocity, align 1
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %swap_rem_Position
  br i1 %is_has_rem_PlayerTag.not, label %skip_sw_rem_PlayerTag, label %swap_rem_PlayerTag

swap_rem_PlayerTag:                               ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %sw_raw_rem_PlayerTag = load ptr, ptr %sw_col_rem_PlayerTag, align 8
  %sw_src_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %15
  %sw_dst_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %16
  %20 = load i32, ptr %sw_src_rem_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_rem_PlayerTag, align 1
  br label %skip_sw_rem_PlayerTag

skip_sw_rem_PlayerTag:                            ; preds = %swap_rem_PlayerTag, %skip_sw_rem_Velocity
  br i1 %is_has_rem_Obstacle.not, label %skip_sw_rem_Obstacle, label %swap_rem_Obstacle

swap_rem_Obstacle:                                ; preds = %skip_sw_rem_PlayerTag
  %sw_col_rem_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %sw_raw_rem_Obstacle = load ptr, ptr %sw_col_rem_Obstacle, align 8
  %sw_src_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %15
  %sw_dst_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %16
  %21 = load i8, ptr %sw_src_rem_Obstacle, align 1
  store i8 %21, ptr %sw_dst_rem_Obstacle, align 1
  br label %skip_sw_rem_Obstacle

skip_sw_rem_Obstacle:                             ; preds = %swap_rem_Obstacle, %skip_sw_rem_PlayerTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %22 = sext i32 %moved_e_rem to i64
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i64 %22
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none)
define i1 @world_has_Position(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #8 {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %2 = sext i32 %1 to i64
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i64 %2
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sgt i32 %cur_arch_idx_has, -1
  br i1 %is_alive_has, label %check_mask, label %common.ret

common.ret:                                       ; preds = %entry, %check_mask
  %common.ret.op = phi i1 [ %res_has, %check_mask ], [ false, %entry ]
  ret i1 %common.ret.op

check_mask:                                       ; preds = %entry
  %tables_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %3 = zext nneg i32 %cur_arch_idx_has to i64
  %arch_ptr_has = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has, i64 %3
  %arch_mask_has = load i64, ptr %arch_ptr_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 2
  %res_has = icmp ne i64 %bit_and_has, 0
  br label %common.ret
}

define void @world_cmd_add_Position(ptr %0, i32 %1, float %2, float %3) local_unnamed_addr {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 20
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cset.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cset.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cset.pre, 20
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cset.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cset = phi ptr [ %data_ptr_cset.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cset = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cset.pre, %grow_cmd.i ]
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds nuw i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  store i32 3, ptr %write_ptr_cset, align 4
  %e_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 4
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 8
  store i32 1, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 12
  store float %2, ptr %payload_raw, align 4
  %f_slot_1 = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 16
  store float %3, ptr %f_slot_1, align 4
  store i32 %new_cnt_cset.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_Position(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 12
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_crem.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_crem.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_crem.pre, 12
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_crem.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_crem = phi ptr [ %data_ptr_crem.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_crem = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_crem.pre, %grow_cmd.i ]
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds nuw i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  store i32 4, ptr %write_ptr_crem, align 4
  %e_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 4
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 8
  store i32 1, ptr %comp_id_slot_crem, align 4
  store i32 %new_cnt_crem.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  ret void
}

; Function Attrs: nounwind
define void @world_add_Velocity(ptr nocapture %0, i32 %1, float %2, float %3) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %4 = sext i32 %1 to i64
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i64 %4
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_assign_a0, label %set_cont

set_assign_a0:                                    ; preds = %entry
  tail call void @world_assign_a0(ptr nonnull %0, i32 %1)
  %cur_arch_idx.pre = load i32, ptr %ent_arch_slot, align 4
  br label %set_cont

set_cont:                                         ; preds = %set_assign_a0, %entry
  %cur_arch_idx = phi i32 [ %cur_arch_idx.pre, %set_assign_a0 ], [ %cur_arch_idx_raw, %entry ]
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i64 %4
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %5 = sext i32 %cur_arch_idx to i64
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i64 %5
  %cur_mask = load i64, ptr %cur_arch_ptr, align 8
  %has_bit = and i64 %cur_mask, 4
  %already_has.not = icmp eq i64 %has_bit, 0
  br i1 %already_has.not, label %transition, label %set_cont.store_fields_crit_edge

set_cont.store_fields_crit_edge:                  ; preds = %set_cont
  %.pre9 = sext i32 %cur_row to i64
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or disjoint i64 %cur_mask, 4
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %transition
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_set, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %transition
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %6 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_set, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_set, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_set, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %7 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %7
  store i64 %new_mask, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_tr1.pre = load ptr, ptr %tables_slot_set, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi8 = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %7, %init_arch.i ]
  %tables_tr1 = phi ptr [ %tables_set, %common.ret.loopexit.i ], [ %tables_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %6, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i64 %.pre-phi8
  %new_cnt_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 8
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 12
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new.not = icmp slt i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new.not, label %after_grow_new_arch, label %grow_new_arch

store_fields:                                     ; preds = %set_cont.store_fields_crit_edge, %after_swap_remove
  %.pre-phi10 = phi i64 [ %.pre9, %set_cont.store_fields_crit_edge ], [ %8, %after_swap_remove ]
  %.pre-phi = phi i64 [ %5, %set_cont.store_fields_crit_edge ], [ %.pre-phi8, %after_swap_remove ]
  %latest_tables_sf = phi ptr [ %tables_set, %set_cont.store_fields_crit_edge ], [ %latest_tables_sf.pre, %after_swap_remove ]
  %final_col_slot = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i64 %.pre-phi, i32 4, i64 2
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Velocity, ptr %final_col_raw, i64 %.pre-phi10
  store float %2, ptr %final_elem, align 4
  %vy_gep = getelementptr inbounds nuw i8, ptr %final_elem, i64 4
  store float %3, ptr %vy_gep, align 4
  ret void

grow_new_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_tr2.pre = load ptr, ptr %tables_slot_set, align 8
  %new_cnt_slot2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_tr2.pre, i64 %.pre-phi8, i32 1
  %new_row.pre = load i32, ptr %new_cnt_slot2.phi.trans.insert, align 4
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %world_get_or_create_archetype.exit
  %new_row = phi i32 [ %new_row.pre, %grow_new_arch ], [ %new_cnt1, %world_get_or_create_archetype.exit ]
  %tables_tr2 = phi ptr [ %tables_tr2.pre, %grow_new_arch ], [ %tables_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %5
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %.pre-phi8
  %new_cnt_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 8
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 16
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %8 = sext i32 %new_row to i64
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i64 %8
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 24
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf.not = icmp eq i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf.not, label %skip_ChildOf, label %copy_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %new_cols_arr = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 24
  %src_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %9 = sext i32 %cur_row to i64
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i64 %9
  %dst_raw_ChildOf = load ptr, ptr %new_cols_arr, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i64 %8
  %10 = load i32, ptr %src_elem_ChildOf, align 1
  store i32 %10, ptr %dst_elem_ChildOf, align 1
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position.not = icmp eq i64 %has_Position, 0
  br i1 %is_has_Position.not, label %skip_Velocity, label %copy_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %11 = sext i32 %cur_row to i64
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i64 %11
  %dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 32
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i64 %8
  %12 = load i64, ptr %src_elem_Position, align 1
  store i64 %12, ptr %dst_elem_Position, align 1
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %skip_ChildOf, %copy_Position
  %has_PlayerTag = and i64 %cur_mask, 8
  %is_has_PlayerTag.not = icmp eq i64 %has_PlayerTag, 0
  br i1 %is_has_PlayerTag.not, label %skip_PlayerTag, label %copy_PlayerTag

copy_PlayerTag:                                   ; preds = %skip_Velocity
  %src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %src_raw_PlayerTag = load ptr, ptr %src_col_PlayerTag, align 8
  %13 = sext i32 %cur_row to i64
  %src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %src_raw_PlayerTag, i64 %13
  %dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 48
  %dst_raw_PlayerTag = load ptr, ptr %dst_col_PlayerTag, align 8
  %dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %dst_raw_PlayerTag, i64 %8
  %14 = load i32, ptr %src_elem_PlayerTag, align 1
  store i32 %14, ptr %dst_elem_PlayerTag, align 1
  br label %skip_PlayerTag

skip_PlayerTag:                                   ; preds = %copy_PlayerTag, %skip_Velocity
  %has_Obstacle = and i64 %cur_mask, 16
  %is_has_Obstacle.not = icmp eq i64 %has_Obstacle, 0
  br i1 %is_has_Obstacle.not, label %skip_Obstacle, label %copy_Obstacle

copy_Obstacle:                                    ; preds = %skip_PlayerTag
  %src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %src_raw_Obstacle = load ptr, ptr %src_col_Obstacle, align 8
  %15 = sext i32 %cur_row to i64
  %src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %src_raw_Obstacle, i64 %15
  %dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 56
  %dst_raw_Obstacle = load ptr, ptr %dst_col_Obstacle, align 8
  %dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %dst_raw_Obstacle, i64 %8
  %16 = load i8, ptr %src_elem_Obstacle, align 1
  store i8 %16, ptr %dst_elem_Obstacle, align 1
  br label %skip_Obstacle

skip_Obstacle:                                    ; preds = %copy_Obstacle, %skip_PlayerTag
  %cur_cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 8
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = add i32 %cur_arch_count, -1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Obstacle
  %cur_ent_sr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 16
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %17 = sext i32 %last_row to i64
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %17
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %18 = sext i32 %cur_row to i64
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %18
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  br i1 %is_has_ChildOf.not, label %skip_sw_ChildOf, label %swap_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Obstacle, %skip_Obstacle
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i64 %4
  store i32 %common.ret.op.i, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i64 %4
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  %latest_tables_sf.pre = load ptr, ptr %tables_slot_set, align 8
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %17
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %18
  %19 = load i32, ptr %sw_src_ChildOf, align 1
  store i32 %19, ptr %sw_dst_ChildOf, align 1
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  br i1 %is_has_Position.not, label %skip_sw_Velocity, label %swap_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %17
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %18
  %20 = load i64, ptr %sw_src_Position, align 1
  store i64 %20, ptr %sw_dst_Position, align 1
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %skip_sw_ChildOf, %swap_Position
  br i1 %is_has_PlayerTag.not, label %skip_sw_PlayerTag, label %swap_PlayerTag

swap_PlayerTag:                                   ; preds = %skip_sw_Velocity
  %sw_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %sw_raw_PlayerTag = load ptr, ptr %sw_col_PlayerTag, align 8
  %sw_src_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %17
  %sw_dst_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %18
  %21 = load i32, ptr %sw_src_PlayerTag, align 1
  store i32 %21, ptr %sw_dst_PlayerTag, align 1
  br label %skip_sw_PlayerTag

skip_sw_PlayerTag:                                ; preds = %swap_PlayerTag, %skip_sw_Velocity
  br i1 %is_has_Obstacle.not, label %skip_sw_Obstacle, label %swap_Obstacle

swap_Obstacle:                                    ; preds = %skip_sw_PlayerTag
  %sw_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %sw_raw_Obstacle = load ptr, ptr %sw_col_Obstacle, align 8
  %sw_src_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %17
  %sw_dst_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %18
  %22 = load i8, ptr %sw_src_Obstacle, align 1
  store i8 %22, ptr %sw_dst_Obstacle, align 1
  br label %skip_sw_Obstacle

skip_sw_Obstacle:                                 ; preds = %swap_Obstacle, %skip_sw_PlayerTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %23 = sext i32 %moved_e to i64
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i64 %23
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

; Function Attrs: nounwind
define void @world_remove_Velocity(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %2 = sext i32 %1 to i64
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i64 %2
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i64 %2
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %3 = sext i32 %cur_arch_rem to i64
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i64 %3
  %cur_mask_val_rem = load i64, ptr %cur_arch_ptr_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 4
  %has_comp_rem.not = icmp eq i64 %rem_has_bit, 0
  br i1 %has_comp_rem.not, label %exit_remove, label %do_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -5
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_remove
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_rem, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask_rem
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_remove
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %4 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_rem, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_rem, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_rem, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %5 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %5
  store i64 %new_mask_rem, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_rem_tr1.pre = load ptr, ptr %tables_slot_rem, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %5, %init_arch.i ]
  %tables_rem_tr1 = phi ptr [ %tables_rem, %common.ret.loopexit.i ], [ %tables_rem_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %4, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i64 %.pre-phi
  %cnt_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 8
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 12
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem.not = icmp slt i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem.not, label %after_grow_rem_arch, label %grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
  ret void

grow_rem_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_rem_tr2.pre = load ptr, ptr %tables_slot_rem, align 8
  %cnt_slot_rem2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2.pre, i64 %.pre-phi, i32 1
  %new_row_rem.pre = load i32, ptr %cnt_slot_rem2.phi.trans.insert, align 4
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %world_get_or_create_archetype.exit
  %new_row_rem = phi i32 [ %new_row_rem.pre, %grow_rem_arch ], [ %cnt_rem1, %world_get_or_create_archetype.exit ]
  %tables_rem_tr2 = phi ptr [ %tables_rem_tr2.pre, %grow_rem_arch ], [ %tables_rem_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %3
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %.pre-phi
  %cnt_slot_rem2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 8
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 16
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %6 = sext i32 %new_row_rem to i64
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i64 %6
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 24
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf.not = icmp eq i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf.not, label %skip_rem_ChildOf, label %copy_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %new_cols_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 24
  %rem_src_raw_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %7 = sext i32 %cur_row_rem to i64
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i64 %7
  %rem_dst_raw_ChildOf = load ptr, ptr %new_cols_rem, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i64 %6
  %8 = load i32, ptr %rem_src_elem_ChildOf, align 1
  store i32 %8, ptr %rem_dst_elem_ChildOf, align 1
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position.not = icmp eq i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position.not, label %skip_rem_Position, label %copy_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %9 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i64 %9
  %rem_dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 32
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i64 %6
  %10 = load i64, ptr %rem_src_elem_Position, align 1
  store i64 %10, ptr %rem_dst_elem_Position, align 1
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %rem_has_PlayerTag = and i64 %cur_mask_val_rem, 8
  %is_has_rem_PlayerTag.not = icmp eq i64 %rem_has_PlayerTag, 0
  br i1 %is_has_rem_PlayerTag.not, label %skip_rem_PlayerTag, label %copy_rem_PlayerTag

copy_rem_PlayerTag:                               ; preds = %skip_rem_Position
  %rem_src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %rem_src_raw_PlayerTag = load ptr, ptr %rem_src_col_PlayerTag, align 8
  %11 = sext i32 %cur_row_rem to i64
  %rem_src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_src_raw_PlayerTag, i64 %11
  %rem_dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 48
  %rem_dst_raw_PlayerTag = load ptr, ptr %rem_dst_col_PlayerTag, align 8
  %rem_dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_dst_raw_PlayerTag, i64 %6
  %12 = load i32, ptr %rem_src_elem_PlayerTag, align 1
  store i32 %12, ptr %rem_dst_elem_PlayerTag, align 1
  br label %skip_rem_PlayerTag

skip_rem_PlayerTag:                               ; preds = %copy_rem_PlayerTag, %skip_rem_Position
  %rem_has_Obstacle = and i64 %cur_mask_val_rem, 16
  %is_has_rem_Obstacle.not = icmp eq i64 %rem_has_Obstacle, 0
  br i1 %is_has_rem_Obstacle.not, label %skip_rem_Obstacle, label %copy_rem_Obstacle

copy_rem_Obstacle:                                ; preds = %skip_rem_PlayerTag
  %rem_src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %rem_src_raw_Obstacle = load ptr, ptr %rem_src_col_Obstacle, align 8
  %13 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_src_raw_Obstacle, i64 %13
  %rem_dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 56
  %rem_dst_raw_Obstacle = load ptr, ptr %rem_dst_col_Obstacle, align 8
  %rem_dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_dst_raw_Obstacle, i64 %6
  %14 = load i8, ptr %rem_src_elem_Obstacle, align 1
  store i8 %14, ptr %rem_dst_elem_Obstacle, align 1
  br label %skip_rem_Obstacle

skip_rem_Obstacle:                                ; preds = %copy_rem_Obstacle, %skip_rem_PlayerTag
  %cnt_slot_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 8
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = add i32 %cnt_rem, -1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Obstacle
  %ent_sr_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 16
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %15 = sext i32 %last_row_rem to i64
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %15
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %16 = sext i32 %cur_row_rem to i64
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %16
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  br i1 %is_has_rem_ChildOf.not, label %skip_sw_rem_ChildOf, label %swap_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Obstacle, %skip_rem_Obstacle
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i64 %2
  store i32 %common.ret.op.i, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i64 %2
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_raw_rem_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %15
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %16
  %17 = load i32, ptr %sw_src_rem_ChildOf, align 1
  store i32 %17, ptr %sw_dst_rem_ChildOf, align 1
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  br i1 %is_has_rem_Position.not, label %swap_rem_Velocity, label %swap_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %15
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %16
  %18 = load i64, ptr %sw_src_rem_Position, align 1
  store i64 %18, ptr %sw_dst_rem_Position, align 1
  br label %swap_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_ChildOf, %swap_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %15
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %16
  %19 = load i64, ptr %sw_src_rem_Velocity, align 1
  store i64 %19, ptr %sw_dst_rem_Velocity, align 1
  br i1 %is_has_rem_PlayerTag.not, label %skip_sw_rem_PlayerTag, label %swap_rem_PlayerTag

swap_rem_PlayerTag:                               ; preds = %swap_rem_Velocity
  %sw_col_rem_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %sw_raw_rem_PlayerTag = load ptr, ptr %sw_col_rem_PlayerTag, align 8
  %sw_src_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %15
  %sw_dst_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %16
  %20 = load i32, ptr %sw_src_rem_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_rem_PlayerTag, align 1
  br label %skip_sw_rem_PlayerTag

skip_sw_rem_PlayerTag:                            ; preds = %swap_rem_PlayerTag, %swap_rem_Velocity
  br i1 %is_has_rem_Obstacle.not, label %skip_sw_rem_Obstacle, label %swap_rem_Obstacle

swap_rem_Obstacle:                                ; preds = %skip_sw_rem_PlayerTag
  %sw_col_rem_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %sw_raw_rem_Obstacle = load ptr, ptr %sw_col_rem_Obstacle, align 8
  %sw_src_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %15
  %sw_dst_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %16
  %21 = load i8, ptr %sw_src_rem_Obstacle, align 1
  store i8 %21, ptr %sw_dst_rem_Obstacle, align 1
  br label %skip_sw_rem_Obstacle

skip_sw_rem_Obstacle:                             ; preds = %swap_rem_Obstacle, %skip_sw_rem_PlayerTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %22 = sext i32 %moved_e_rem to i64
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i64 %22
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none)
define i1 @world_has_Velocity(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #8 {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %2 = sext i32 %1 to i64
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i64 %2
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sgt i32 %cur_arch_idx_has, -1
  br i1 %is_alive_has, label %check_mask, label %common.ret

common.ret:                                       ; preds = %entry, %check_mask
  %common.ret.op = phi i1 [ %res_has, %check_mask ], [ false, %entry ]
  ret i1 %common.ret.op

check_mask:                                       ; preds = %entry
  %tables_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %3 = zext nneg i32 %cur_arch_idx_has to i64
  %arch_ptr_has = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has, i64 %3
  %arch_mask_has = load i64, ptr %arch_ptr_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 4
  %res_has = icmp ne i64 %bit_and_has, 0
  br label %common.ret
}

define void @world_cmd_add_Velocity(ptr %0, i32 %1, float %2, float %3) local_unnamed_addr {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 20
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cset.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cset.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cset.pre, 20
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cset.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cset = phi ptr [ %data_ptr_cset.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cset = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cset.pre, %grow_cmd.i ]
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds nuw i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  store i32 3, ptr %write_ptr_cset, align 4
  %e_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 4
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 8
  store i32 2, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 12
  store float %2, ptr %payload_raw, align 4
  %f_slot_1 = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 16
  store float %3, ptr %f_slot_1, align 4
  store i32 %new_cnt_cset.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_Velocity(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 12
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_crem.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_crem.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_crem.pre, 12
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_crem.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_crem = phi ptr [ %data_ptr_crem.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_crem = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_crem.pre, %grow_cmd.i ]
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds nuw i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  store i32 4, ptr %write_ptr_crem, align 4
  %e_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 4
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 8
  store i32 2, ptr %comp_id_slot_crem, align 4
  store i32 %new_cnt_crem.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  ret void
}

; Function Attrs: nounwind
define void @world_add_PlayerTag(ptr nocapture %0, i32 %1, i32 %2) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %3 = sext i32 %1 to i64
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i64 %3
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_assign_a0, label %set_cont

set_assign_a0:                                    ; preds = %entry
  tail call void @world_assign_a0(ptr nonnull %0, i32 %1)
  %cur_arch_idx.pre = load i32, ptr %ent_arch_slot, align 4
  br label %set_cont

set_cont:                                         ; preds = %set_assign_a0, %entry
  %cur_arch_idx = phi i32 [ %cur_arch_idx.pre, %set_assign_a0 ], [ %cur_arch_idx_raw, %entry ]
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i64 %3
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %4 = sext i32 %cur_arch_idx to i64
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i64 %4
  %cur_mask = load i64, ptr %cur_arch_ptr, align 8
  %has_bit = and i64 %cur_mask, 8
  %already_has.not = icmp eq i64 %has_bit, 0
  br i1 %already_has.not, label %transition, label %set_cont.store_fields_crit_edge

set_cont.store_fields_crit_edge:                  ; preds = %set_cont
  %.pre9 = sext i32 %cur_row to i64
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or disjoint i64 %cur_mask, 8
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %transition
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_set, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %transition
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %5 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_set, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_set, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_set, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %6 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %6
  store i64 %new_mask, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_tr1.pre = load ptr, ptr %tables_slot_set, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi8 = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %6, %init_arch.i ]
  %tables_tr1 = phi ptr [ %tables_set, %common.ret.loopexit.i ], [ %tables_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %5, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i64 %.pre-phi8
  %new_cnt_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 8
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 12
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new.not = icmp slt i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new.not, label %after_grow_new_arch, label %grow_new_arch

store_fields:                                     ; preds = %set_cont.store_fields_crit_edge, %after_swap_remove
  %.pre-phi10 = phi i64 [ %.pre9, %set_cont.store_fields_crit_edge ], [ %7, %after_swap_remove ]
  %.pre-phi = phi i64 [ %4, %set_cont.store_fields_crit_edge ], [ %.pre-phi8, %after_swap_remove ]
  %latest_tables_sf = phi ptr [ %tables_set, %set_cont.store_fields_crit_edge ], [ %latest_tables_sf.pre, %after_swap_remove ]
  %final_col_slot = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i64 %.pre-phi, i32 4, i64 3
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.PlayerTag, ptr %final_col_raw, i64 %.pre-phi10
  store i32 %2, ptr %final_elem, align 4
  ret void

grow_new_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_tr2.pre = load ptr, ptr %tables_slot_set, align 8
  %new_cnt_slot2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_tr2.pre, i64 %.pre-phi8, i32 1
  %new_row.pre = load i32, ptr %new_cnt_slot2.phi.trans.insert, align 4
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %world_get_or_create_archetype.exit
  %new_row = phi i32 [ %new_row.pre, %grow_new_arch ], [ %new_cnt1, %world_get_or_create_archetype.exit ]
  %tables_tr2 = phi ptr [ %tables_tr2.pre, %grow_new_arch ], [ %tables_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %4
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %.pre-phi8
  %new_cnt_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 8
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 16
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %7 = sext i32 %new_row to i64
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i64 %7
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 24
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf.not = icmp eq i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf.not, label %skip_ChildOf, label %copy_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %new_cols_arr = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 24
  %src_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %8 = sext i32 %cur_row to i64
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i64 %8
  %dst_raw_ChildOf = load ptr, ptr %new_cols_arr, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i64 %7
  %9 = load i32, ptr %src_elem_ChildOf, align 1
  store i32 %9, ptr %dst_elem_ChildOf, align 1
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position.not = icmp eq i64 %has_Position, 0
  br i1 %is_has_Position.not, label %skip_Position, label %copy_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %10 = sext i32 %cur_row to i64
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i64 %10
  %dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 32
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i64 %7
  %11 = load i64, ptr %src_elem_Position, align 1
  store i64 %11, ptr %dst_elem_Position, align 1
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity.not = icmp eq i64 %has_Velocity, 0
  br i1 %is_has_Velocity.not, label %skip_PlayerTag, label %copy_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %12 = sext i32 %cur_row to i64
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i64 %12
  %dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 40
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i64 %7
  %13 = load i64, ptr %src_elem_Velocity, align 1
  store i64 %13, ptr %dst_elem_Velocity, align 1
  br label %skip_PlayerTag

skip_PlayerTag:                                   ; preds = %skip_Position, %copy_Velocity
  %has_Obstacle = and i64 %cur_mask, 16
  %is_has_Obstacle.not = icmp eq i64 %has_Obstacle, 0
  br i1 %is_has_Obstacle.not, label %skip_Obstacle, label %copy_Obstacle

copy_Obstacle:                                    ; preds = %skip_PlayerTag
  %src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %src_raw_Obstacle = load ptr, ptr %src_col_Obstacle, align 8
  %14 = sext i32 %cur_row to i64
  %src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %src_raw_Obstacle, i64 %14
  %dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 56
  %dst_raw_Obstacle = load ptr, ptr %dst_col_Obstacle, align 8
  %dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %dst_raw_Obstacle, i64 %7
  %15 = load i8, ptr %src_elem_Obstacle, align 1
  store i8 %15, ptr %dst_elem_Obstacle, align 1
  br label %skip_Obstacle

skip_Obstacle:                                    ; preds = %copy_Obstacle, %skip_PlayerTag
  %cur_cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 8
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = add i32 %cur_arch_count, -1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Obstacle
  %cur_ent_sr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 16
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %16 = sext i32 %last_row to i64
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %16
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %17 = sext i32 %cur_row to i64
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %17
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  br i1 %is_has_ChildOf.not, label %skip_sw_ChildOf, label %swap_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Obstacle, %skip_Obstacle
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i64 %3
  store i32 %common.ret.op.i, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i64 %3
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  %latest_tables_sf.pre = load ptr, ptr %tables_slot_set, align 8
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %16
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %17
  %18 = load i32, ptr %sw_src_ChildOf, align 1
  store i32 %18, ptr %sw_dst_ChildOf, align 1
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  br i1 %is_has_Position.not, label %skip_sw_Position, label %swap_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %16
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %17
  %19 = load i64, ptr %sw_src_Position, align 1
  store i64 %19, ptr %sw_dst_Position, align 1
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  br i1 %is_has_Velocity.not, label %skip_sw_PlayerTag, label %swap_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %16
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %17
  %20 = load i64, ptr %sw_src_Velocity, align 1
  store i64 %20, ptr %sw_dst_Velocity, align 1
  br label %skip_sw_PlayerTag

skip_sw_PlayerTag:                                ; preds = %skip_sw_Position, %swap_Velocity
  br i1 %is_has_Obstacle.not, label %skip_sw_Obstacle, label %swap_Obstacle

swap_Obstacle:                                    ; preds = %skip_sw_PlayerTag
  %sw_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 56
  %sw_raw_Obstacle = load ptr, ptr %sw_col_Obstacle, align 8
  %sw_src_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %16
  %sw_dst_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_Obstacle, i64 %17
  %21 = load i8, ptr %sw_src_Obstacle, align 1
  store i8 %21, ptr %sw_dst_Obstacle, align 1
  br label %skip_sw_Obstacle

skip_sw_Obstacle:                                 ; preds = %swap_Obstacle, %skip_sw_PlayerTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %22 = sext i32 %moved_e to i64
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i64 %22
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

; Function Attrs: nounwind
define void @world_remove_PlayerTag(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %2 = sext i32 %1 to i64
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i64 %2
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i64 %2
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %3 = sext i32 %cur_arch_rem to i64
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i64 %3
  %cur_mask_val_rem = load i64, ptr %cur_arch_ptr_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 8
  %has_comp_rem.not = icmp eq i64 %rem_has_bit, 0
  br i1 %has_comp_rem.not, label %exit_remove, label %do_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -9
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_remove
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_rem, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask_rem
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_remove
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %4 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_rem, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_rem, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_rem, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %5 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %5
  store i64 %new_mask_rem, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_rem_tr1.pre = load ptr, ptr %tables_slot_rem, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %5, %init_arch.i ]
  %tables_rem_tr1 = phi ptr [ %tables_rem, %common.ret.loopexit.i ], [ %tables_rem_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %4, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i64 %.pre-phi
  %cnt_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 8
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 12
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem.not = icmp slt i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem.not, label %after_grow_rem_arch, label %grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
  ret void

grow_rem_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_rem_tr2.pre = load ptr, ptr %tables_slot_rem, align 8
  %cnt_slot_rem2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2.pre, i64 %.pre-phi, i32 1
  %new_row_rem.pre = load i32, ptr %cnt_slot_rem2.phi.trans.insert, align 4
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %world_get_or_create_archetype.exit
  %new_row_rem = phi i32 [ %new_row_rem.pre, %grow_rem_arch ], [ %cnt_rem1, %world_get_or_create_archetype.exit ]
  %tables_rem_tr2 = phi ptr [ %tables_rem_tr2.pre, %grow_rem_arch ], [ %tables_rem_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %3
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %.pre-phi
  %cnt_slot_rem2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 8
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 16
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %6 = sext i32 %new_row_rem to i64
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i64 %6
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 24
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf.not = icmp eq i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf.not, label %skip_rem_ChildOf, label %copy_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %new_cols_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 24
  %rem_src_raw_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %7 = sext i32 %cur_row_rem to i64
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i64 %7
  %rem_dst_raw_ChildOf = load ptr, ptr %new_cols_rem, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i64 %6
  %8 = load i32, ptr %rem_src_elem_ChildOf, align 1
  store i32 %8, ptr %rem_dst_elem_ChildOf, align 1
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position.not = icmp eq i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position.not, label %skip_rem_Position, label %copy_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %9 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i64 %9
  %rem_dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 32
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i64 %6
  %10 = load i64, ptr %rem_src_elem_Position, align 1
  store i64 %10, ptr %rem_dst_elem_Position, align 1
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity.not = icmp eq i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity.not, label %skip_rem_Velocity, label %copy_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_Position
  %rem_src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %11 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i64 %11
  %rem_dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 40
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i64 %6
  %12 = load i64, ptr %rem_src_elem_Velocity, align 1
  store i64 %12, ptr %rem_dst_elem_Velocity, align 1
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_Position
  %rem_has_Obstacle = and i64 %cur_mask_val_rem, 16
  %is_has_rem_Obstacle.not = icmp eq i64 %rem_has_Obstacle, 0
  br i1 %is_has_rem_Obstacle.not, label %skip_rem_Obstacle, label %copy_rem_Obstacle

copy_rem_Obstacle:                                ; preds = %skip_rem_Velocity
  %rem_src_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %rem_src_raw_Obstacle = load ptr, ptr %rem_src_col_Obstacle, align 8
  %13 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_src_raw_Obstacle, i64 %13
  %rem_dst_col_Obstacle = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 56
  %rem_dst_raw_Obstacle = load ptr, ptr %rem_dst_col_Obstacle, align 8
  %rem_dst_elem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %rem_dst_raw_Obstacle, i64 %6
  %14 = load i8, ptr %rem_src_elem_Obstacle, align 1
  store i8 %14, ptr %rem_dst_elem_Obstacle, align 1
  br label %skip_rem_Obstacle

skip_rem_Obstacle:                                ; preds = %copy_rem_Obstacle, %skip_rem_Velocity
  %cnt_slot_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 8
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = add i32 %cnt_rem, -1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_Obstacle
  %ent_sr_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 16
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %15 = sext i32 %last_row_rem to i64
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %15
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %16 = sext i32 %cur_row_rem to i64
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %16
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  br i1 %is_has_rem_ChildOf.not, label %skip_sw_rem_ChildOf, label %swap_rem_ChildOf

after_swap_rem:                                   ; preds = %skip_sw_rem_Obstacle, %skip_rem_Obstacle
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i64 %2
  store i32 %common.ret.op.i, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i64 %2
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_raw_rem_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %15
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %16
  %17 = load i32, ptr %sw_src_rem_ChildOf, align 1
  store i32 %17, ptr %sw_dst_rem_ChildOf, align 1
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  br i1 %is_has_rem_Position.not, label %skip_sw_rem_Position, label %swap_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %15
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %16
  %18 = load i64, ptr %sw_src_rem_Position, align 1
  store i64 %18, ptr %sw_dst_rem_Position, align 1
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  br i1 %is_has_rem_Velocity.not, label %swap_rem_PlayerTag, label %swap_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %15
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %16
  %19 = load i64, ptr %sw_src_rem_Velocity, align 1
  store i64 %19, ptr %sw_dst_rem_Velocity, align 1
  br label %swap_rem_PlayerTag

swap_rem_PlayerTag:                               ; preds = %skip_sw_rem_Position, %swap_rem_Velocity
  %sw_col_rem_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %sw_raw_rem_PlayerTag = load ptr, ptr %sw_col_rem_PlayerTag, align 8
  %sw_src_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %15
  %sw_dst_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %16
  %20 = load i32, ptr %sw_src_rem_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_rem_PlayerTag, align 1
  br i1 %is_has_rem_Obstacle.not, label %skip_sw_rem_Obstacle, label %swap_rem_Obstacle

swap_rem_Obstacle:                                ; preds = %swap_rem_PlayerTag
  %sw_col_rem_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %sw_raw_rem_Obstacle = load ptr, ptr %sw_col_rem_Obstacle, align 8
  %sw_src_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %15
  %sw_dst_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %16
  %21 = load i8, ptr %sw_src_rem_Obstacle, align 1
  store i8 %21, ptr %sw_dst_rem_Obstacle, align 1
  br label %skip_sw_rem_Obstacle

skip_sw_rem_Obstacle:                             ; preds = %swap_rem_Obstacle, %swap_rem_PlayerTag
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %22 = sext i32 %moved_e_rem to i64
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i64 %22
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none)
define i1 @world_has_PlayerTag(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #8 {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %2 = sext i32 %1 to i64
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i64 %2
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sgt i32 %cur_arch_idx_has, -1
  br i1 %is_alive_has, label %check_mask, label %common.ret

common.ret:                                       ; preds = %entry, %check_mask
  %common.ret.op = phi i1 [ %res_has, %check_mask ], [ false, %entry ]
  ret i1 %common.ret.op

check_mask:                                       ; preds = %entry
  %tables_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %3 = zext nneg i32 %cur_arch_idx_has to i64
  %arch_ptr_has = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has, i64 %3
  %arch_mask_has = load i64, ptr %arch_ptr_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 8
  %res_has = icmp ne i64 %bit_and_has, 0
  br label %common.ret
}

define void @world_cmd_add_PlayerTag(ptr %0, i32 %1, i32 %2) local_unnamed_addr {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 16
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cset.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cset.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cset.pre, 16
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cset.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cset = phi ptr [ %data_ptr_cset.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cset = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cset.pre, %grow_cmd.i ]
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds nuw i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  store i32 3, ptr %write_ptr_cset, align 4
  %e_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 4
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 8
  store i32 3, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 12
  store i32 %2, ptr %payload_raw, align 4
  store i32 %new_cnt_cset.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_PlayerTag(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 12
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_crem.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_crem.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_crem.pre, 12
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_crem.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_crem = phi ptr [ %data_ptr_crem.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_crem = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_crem.pre, %grow_cmd.i ]
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds nuw i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  store i32 4, ptr %write_ptr_crem, align 4
  %e_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 4
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 8
  store i32 3, ptr %comp_id_slot_crem, align 4
  store i32 %new_cnt_crem.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  ret void
}

; Function Attrs: nounwind
define void @world_add_Obstacle(ptr nocapture %0, i32 %1, i1 %2) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_set = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr = load ptr, ptr %ent_arch_slot_set, align 8
  %3 = sext i32 %1 to i64
  %ent_arch_slot = getelementptr inbounds i32, ptr %arch_arr, i64 %3
  %cur_arch_idx_raw = load i32, ptr %ent_arch_slot, align 4
  %is_neg_arch = icmp slt i32 %cur_arch_idx_raw, 0
  br i1 %is_neg_arch, label %set_assign_a0, label %set_cont

set_assign_a0:                                    ; preds = %entry
  tail call void @world_assign_a0(ptr nonnull %0, i32 %1)
  %cur_arch_idx.pre = load i32, ptr %ent_arch_slot, align 4
  br label %set_cont

set_cont:                                         ; preds = %set_assign_a0, %entry
  %cur_arch_idx = phi i32 [ %cur_arch_idx.pre, %set_assign_a0 ], [ %cur_arch_idx_raw, %entry ]
  %row_arr = load ptr, ptr %ent_row_slot_set, align 8
  %ent_row_slot = getelementptr inbounds i32, ptr %row_arr, i64 %3
  %cur_row = load i32, ptr %ent_row_slot, align 4
  %tables_set = load ptr, ptr %tables_slot_set, align 8
  %4 = sext i32 %cur_arch_idx to i64
  %cur_arch_ptr = getelementptr inbounds %struct.Archetype, ptr %tables_set, i64 %4
  %cur_mask = load i64, ptr %cur_arch_ptr, align 8
  %has_bit = and i64 %cur_mask, 16
  %already_has.not = icmp eq i64 %has_bit, 0
  br i1 %already_has.not, label %transition, label %set_cont.store_fields_crit_edge

set_cont.store_fields_crit_edge:                  ; preds = %set_cont
  %.pre9 = sext i32 %cur_row to i64
  br label %store_fields

transition:                                       ; preds = %set_cont
  %new_mask = or disjoint i64 %cur_mask, 16
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %transition
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_set, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %transition
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %5 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_set, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_set, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_set, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %6 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %6
  store i64 %new_mask, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_tr1.pre = load ptr, ptr %tables_slot_set, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi8 = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %6, %init_arch.i ]
  %tables_tr1 = phi ptr [ %tables_set, %common.ret.loopexit.i ], [ %tables_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %5, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr1 = getelementptr inbounds %struct.Archetype, ptr %tables_tr1, i64 %.pre-phi8
  %new_cnt_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 8
  %new_cnt1 = load i32, ptr %new_cnt_slot1, align 4
  %new_cap_slot1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr1, i64 12
  %new_cap1 = load i32, ptr %new_cap_slot1, align 4
  %need_grow_new.not = icmp slt i32 %new_cnt1, %new_cap1
  br i1 %need_grow_new.not, label %after_grow_new_arch, label %grow_new_arch

store_fields:                                     ; preds = %set_cont.store_fields_crit_edge, %after_swap_remove
  %.pre-phi10 = phi i64 [ %.pre9, %set_cont.store_fields_crit_edge ], [ %7, %after_swap_remove ]
  %.pre-phi = phi i64 [ %4, %set_cont.store_fields_crit_edge ], [ %.pre-phi8, %after_swap_remove ]
  %latest_tables_sf = phi ptr [ %tables_set, %set_cont.store_fields_crit_edge ], [ %latest_tables_sf.pre, %after_swap_remove ]
  %final_col_slot = getelementptr inbounds %struct.Archetype, ptr %latest_tables_sf, i64 %.pre-phi, i32 4, i64 4
  %final_col_raw = load ptr, ptr %final_col_slot, align 8
  %final_elem = getelementptr inbounds %struct.Obstacle, ptr %final_col_raw, i64 %.pre-phi10
  store i1 %2, ptr %final_elem, align 1
  ret void

grow_new_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_tr2.pre = load ptr, ptr %tables_slot_set, align 8
  %new_cnt_slot2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_tr2.pre, i64 %.pre-phi8, i32 1
  %new_row.pre = load i32, ptr %new_cnt_slot2.phi.trans.insert, align 4
  br label %after_grow_new_arch

after_grow_new_arch:                              ; preds = %grow_new_arch, %world_get_or_create_archetype.exit
  %new_row = phi i32 [ %new_row.pre, %grow_new_arch ], [ %new_cnt1, %world_get_or_create_archetype.exit ]
  %tables_tr2 = phi ptr [ %tables_tr2.pre, %grow_new_arch ], [ %tables_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %4
  %new_arch_ptr2 = getelementptr inbounds %struct.Archetype, ptr %tables_tr2, i64 %.pre-phi8
  %new_cnt_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 8
  %next_new_cnt = add i32 %new_row, 1
  store i32 %next_new_cnt, ptr %new_cnt_slot2, align 4
  %new_ent_slot2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 16
  %new_ent_raw2 = load ptr, ptr %new_ent_slot2, align 8
  %7 = sext i32 %new_row to i64
  %new_ent_elem2 = getelementptr inbounds i32, ptr %new_ent_raw2, i64 %7
  store i32 %1, ptr %new_ent_elem2, align 4
  %cur_cols_arr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 24
  %has_ChildOf = and i64 %cur_mask, 1
  %is_has_ChildOf.not = icmp eq i64 %has_ChildOf, 0
  br i1 %is_has_ChildOf.not, label %skip_ChildOf, label %copy_ChildOf

copy_ChildOf:                                     ; preds = %after_grow_new_arch
  %new_cols_arr = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 24
  %src_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %8 = sext i32 %cur_row to i64
  %src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %src_raw_ChildOf, i64 %8
  %dst_raw_ChildOf = load ptr, ptr %new_cols_arr, align 8
  %dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %dst_raw_ChildOf, i64 %7
  %9 = load i32, ptr %src_elem_ChildOf, align 1
  store i32 %9, ptr %dst_elem_ChildOf, align 1
  br label %skip_ChildOf

skip_ChildOf:                                     ; preds = %copy_ChildOf, %after_grow_new_arch
  %has_Position = and i64 %cur_mask, 2
  %is_has_Position.not = icmp eq i64 %has_Position, 0
  br i1 %is_has_Position.not, label %skip_Position, label %copy_Position

copy_Position:                                    ; preds = %skip_ChildOf
  %src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %src_raw_Position = load ptr, ptr %src_col_Position, align 8
  %10 = sext i32 %cur_row to i64
  %src_elem_Position = getelementptr inbounds %struct.Position, ptr %src_raw_Position, i64 %10
  %dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 32
  %dst_raw_Position = load ptr, ptr %dst_col_Position, align 8
  %dst_elem_Position = getelementptr inbounds %struct.Position, ptr %dst_raw_Position, i64 %7
  %11 = load i64, ptr %src_elem_Position, align 1
  store i64 %11, ptr %dst_elem_Position, align 1
  br label %skip_Position

skip_Position:                                    ; preds = %copy_Position, %skip_ChildOf
  %has_Velocity = and i64 %cur_mask, 4
  %is_has_Velocity.not = icmp eq i64 %has_Velocity, 0
  br i1 %is_has_Velocity.not, label %skip_Velocity, label %copy_Velocity

copy_Velocity:                                    ; preds = %skip_Position
  %src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %src_raw_Velocity = load ptr, ptr %src_col_Velocity, align 8
  %12 = sext i32 %cur_row to i64
  %src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %src_raw_Velocity, i64 %12
  %dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 40
  %dst_raw_Velocity = load ptr, ptr %dst_col_Velocity, align 8
  %dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %dst_raw_Velocity, i64 %7
  %13 = load i64, ptr %src_elem_Velocity, align 1
  store i64 %13, ptr %dst_elem_Velocity, align 1
  br label %skip_Velocity

skip_Velocity:                                    ; preds = %copy_Velocity, %skip_Position
  %has_PlayerTag = and i64 %cur_mask, 8
  %is_has_PlayerTag.not = icmp eq i64 %has_PlayerTag, 0
  br i1 %is_has_PlayerTag.not, label %skip_Obstacle, label %copy_PlayerTag

copy_PlayerTag:                                   ; preds = %skip_Velocity
  %src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %src_raw_PlayerTag = load ptr, ptr %src_col_PlayerTag, align 8
  %14 = sext i32 %cur_row to i64
  %src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %src_raw_PlayerTag, i64 %14
  %dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr2, i64 48
  %dst_raw_PlayerTag = load ptr, ptr %dst_col_PlayerTag, align 8
  %dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %dst_raw_PlayerTag, i64 %7
  %15 = load i32, ptr %src_elem_PlayerTag, align 1
  store i32 %15, ptr %dst_elem_PlayerTag, align 1
  br label %skip_Obstacle

skip_Obstacle:                                    ; preds = %skip_Velocity, %copy_PlayerTag
  %cur_cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 8
  %cur_arch_count = load i32, ptr %cur_cnt_slot, align 4
  %last_row = add i32 %cur_arch_count, -1
  store i32 %last_row, ptr %cur_cnt_slot, align 4
  %is_last_row = icmp eq i32 %cur_row, %last_row
  br i1 %is_last_row, label %after_swap_remove, label %do_swap_remove

do_swap_remove:                                   ; preds = %skip_Obstacle
  %cur_ent_sr = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 16
  %cur_ent_raw_sr = load ptr, ptr %cur_ent_sr, align 8
  %16 = sext i32 %last_row to i64
  %last_ent_elem = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %16
  %moved_e = load i32, ptr %last_ent_elem, align 4
  %17 = sext i32 %cur_row to i64
  %cur_ent_elem_sr = getelementptr inbounds i32, ptr %cur_ent_raw_sr, i64 %17
  store i32 %moved_e, ptr %cur_ent_elem_sr, align 4
  br i1 %is_has_ChildOf.not, label %skip_sw_ChildOf, label %swap_ChildOf

after_swap_remove:                                ; preds = %skip_sw_Obstacle, %skip_Obstacle
  %arch_arr_tr = load ptr, ptr %ent_arch_slot_set, align 8
  %e_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_tr, i64 %3
  store i32 %common.ret.op.i, ptr %e_arch_slot_tr, align 4
  %row_arr_tr = load ptr, ptr %ent_row_slot_set, align 8
  %e_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_tr, i64 %3
  store i32 %new_row, ptr %e_row_slot_tr, align 4
  %latest_tables_sf.pre = load ptr, ptr %tables_slot_set, align 8
  br label %store_fields

swap_ChildOf:                                     ; preds = %do_swap_remove
  %sw_raw_ChildOf = load ptr, ptr %cur_cols_arr, align 8
  %sw_src_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %16
  %sw_dst_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ChildOf, i64 %17
  %18 = load i32, ptr %sw_src_ChildOf, align 1
  store i32 %18, ptr %sw_dst_ChildOf, align 1
  br label %skip_sw_ChildOf

skip_sw_ChildOf:                                  ; preds = %swap_ChildOf, %do_swap_remove
  br i1 %is_has_Position.not, label %skip_sw_Position, label %swap_Position

swap_Position:                                    ; preds = %skip_sw_ChildOf
  %sw_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 32
  %sw_raw_Position = load ptr, ptr %sw_col_Position, align 8
  %sw_src_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %16
  %sw_dst_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_Position, i64 %17
  %19 = load i64, ptr %sw_src_Position, align 1
  store i64 %19, ptr %sw_dst_Position, align 1
  br label %skip_sw_Position

skip_sw_Position:                                 ; preds = %swap_Position, %skip_sw_ChildOf
  br i1 %is_has_Velocity.not, label %skip_sw_Velocity, label %swap_Velocity

swap_Velocity:                                    ; preds = %skip_sw_Position
  %sw_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 40
  %sw_raw_Velocity = load ptr, ptr %sw_col_Velocity, align 8
  %sw_src_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %16
  %sw_dst_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_Velocity, i64 %17
  %20 = load i64, ptr %sw_src_Velocity, align 1
  store i64 %20, ptr %sw_dst_Velocity, align 1
  br label %skip_sw_Velocity

skip_sw_Velocity:                                 ; preds = %swap_Velocity, %skip_sw_Position
  br i1 %is_has_PlayerTag.not, label %skip_sw_Obstacle, label %swap_PlayerTag

swap_PlayerTag:                                   ; preds = %skip_sw_Velocity
  %sw_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr2, i64 48
  %sw_raw_PlayerTag = load ptr, ptr %sw_col_PlayerTag, align 8
  %sw_src_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %16
  %sw_dst_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_PlayerTag, i64 %17
  %21 = load i32, ptr %sw_src_PlayerTag, align 1
  store i32 %21, ptr %sw_dst_PlayerTag, align 1
  br label %skip_sw_Obstacle

skip_sw_Obstacle:                                 ; preds = %skip_sw_Velocity, %swap_PlayerTag
  %row_arr_sr = load ptr, ptr %ent_row_slot_set, align 8
  %22 = sext i32 %moved_e to i64
  %moved_e_row_slot = getelementptr inbounds i32, ptr %row_arr_sr, i64 %22
  store i32 %cur_row, ptr %moved_e_row_slot, align 4
  br label %after_swap_remove
}

; Function Attrs: nounwind
define void @world_remove_Obstacle(ptr nocapture %0, i32 %1) local_unnamed_addr #4 {
entry:
  %ent_arch_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 24
  %ent_row_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 32
  %tables_slot_rem = getelementptr inbounds nuw i8, ptr %0, i64 8
  %arch_arr_rem = load ptr, ptr %ent_arch_slot_rem, align 8
  %2 = sext i32 %1 to i64
  %rem_arch_slot = getelementptr inbounds i32, ptr %arch_arr_rem, i64 %2
  %cur_arch_rem = load i32, ptr %rem_arch_slot, align 4
  %row_arr_rem = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot = getelementptr inbounds i32, ptr %row_arr_rem, i64 %2
  %cur_row_rem = load i32, ptr %rem_row_slot, align 4
  %tables_rem = load ptr, ptr %tables_slot_rem, align 8
  %3 = sext i32 %cur_arch_rem to i64
  %cur_arch_ptr_rem = getelementptr inbounds %struct.Archetype, ptr %tables_rem, i64 %3
  %cur_mask_val_rem = load i64, ptr %cur_arch_ptr_rem, align 8
  %rem_has_bit = and i64 %cur_mask_val_rem, 16
  %has_comp_rem.not = icmp eq i64 %rem_has_bit, 0
  br i1 %has_comp_rem.not, label %exit_remove, label %do_remove

do_remove:                                        ; preds = %entry
  %new_mask_rem = and i64 %cur_mask_val_rem, -17
  %arch_cap_slot.i = getelementptr inbounds nuw i8, ptr %0, i64 4
  %cur_count.i = load i32, ptr %0, align 4
  %has_more2.i = icmp sgt i32 %cur_count.i, 0
  br i1 %has_more2.i, label %search_body.lr.ph.i, label %not_found.i

search_body.lr.ph.i:                              ; preds = %do_remove
  %wide.trip.count.i = zext nneg i32 %cur_count.i to i64
  br label %search_body.i

search_body.i:                                    ; preds = %search_next.i, %search_body.lr.ph.i
  %indvars.iv.i = phi i64 [ 0, %search_body.lr.ph.i ], [ %indvars.iv.next.i, %search_next.i ]
  %arch_elem.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_rem, i64 %indvars.iv.i
  %existing_mask.i = load i64, ptr %arch_elem.i, align 8
  %is_match.i = icmp eq i64 %existing_mask.i, %new_mask_rem
  br i1 %is_match.i, label %common.ret.loopexit.i, label %search_next.i

not_found.i:                                      ; preds = %search_next.i, %do_remove
  %cur_cap.i = load i32, ptr %arch_cap_slot.i, align 4
  %need_grow.not.i = icmp slt i32 %cur_count.i, %cur_cap.i
  br i1 %need_grow.not.i, label %init_arch.i, label %grow_tables.i

common.ret.loopexit.i:                            ; preds = %search_body.i
  %4 = trunc nuw nsw i64 %indvars.iv.i to i32
  %sext = shl i64 %indvars.iv.i, 32
  %.pre = ashr exact i64 %sext, 32
  br label %world_get_or_create_archetype.exit

search_next.i:                                    ; preds = %search_body.i
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %not_found.i, label %search_body.i

grow_tables.i:                                    ; preds = %not_found.i
  %cap_zero.i = icmp eq i32 %cur_cap.i, 0
  %double_cap.i = shl i32 %cur_cap.i, 1
  %new_cap.i = select i1 %cap_zero.i, i32 8, i32 %double_cap.i
  store i32 %new_cap.i, ptr %arch_cap_slot.i, align 4
  %new_cap64.i = zext i32 %new_cap.i to i64
  %alloc_bytes.i = shl nuw nsw i64 %new_cap64.i, 6
  %new_tables_i8.i = tail call ptr @realloc(ptr nonnull %tables_rem, i64 %alloc_bytes.i)
  store ptr %new_tables_i8.i, ptr %tables_slot_rem, align 8
  br label %init_arch.i

init_arch.i:                                      ; preds = %grow_tables.i, %not_found.i
  %latest_tables.i = phi ptr [ %new_tables_i8.i, %grow_tables.i ], [ %tables_rem, %not_found.i ]
  %next_count.i = add i32 %cur_count.i, 1
  store i32 %next_count.i, ptr %0, align 4
  %5 = sext i32 %cur_count.i to i64
  %new_arch_elem.i = getelementptr inbounds %struct.Archetype, ptr %latest_tables.i, i64 %5
  store i64 %new_mask_rem, ptr %new_arch_elem.i, align 8
  %cnt_gep.i = getelementptr inbounds nuw i8, ptr %new_arch_elem.i, i64 8
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 4 dereferenceable(56) %cnt_gep.i, i8 0, i64 56, i1 false)
  %tables_rem_tr1.pre = load ptr, ptr %tables_slot_rem, align 8
  br label %world_get_or_create_archetype.exit

world_get_or_create_archetype.exit:               ; preds = %common.ret.loopexit.i, %init_arch.i
  %.pre-phi = phi i64 [ %.pre, %common.ret.loopexit.i ], [ %5, %init_arch.i ]
  %tables_rem_tr1 = phi ptr [ %tables_rem, %common.ret.loopexit.i ], [ %tables_rem_tr1.pre, %init_arch.i ]
  %common.ret.op.i = phi i32 [ %4, %common.ret.loopexit.i ], [ %cur_count.i, %init_arch.i ]
  %new_arch_ptr_rem1 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr1, i64 %.pre-phi
  %cnt_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 8
  %cnt_rem1 = load i32, ptr %cnt_slot_rem1, align 4
  %cap_slot_rem1 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem1, i64 12
  %cap_rem1 = load i32, ptr %cap_slot_rem1, align 4
  %need_grow_rem.not = icmp slt i32 %cnt_rem1, %cap_rem1
  br i1 %need_grow_rem.not, label %after_grow_rem_arch, label %grow_rem_arch

exit_remove:                                      ; preds = %after_swap_rem, %entry
  ret void

grow_rem_arch:                                    ; preds = %world_get_or_create_archetype.exit
  tail call void @world_grow_archetype(ptr nonnull %0, i32 %common.ret.op.i)
  %tables_rem_tr2.pre = load ptr, ptr %tables_slot_rem, align 8
  %cnt_slot_rem2.phi.trans.insert = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2.pre, i64 %.pre-phi, i32 1
  %new_row_rem.pre = load i32, ptr %cnt_slot_rem2.phi.trans.insert, align 4
  br label %after_grow_rem_arch

after_grow_rem_arch:                              ; preds = %grow_rem_arch, %world_get_or_create_archetype.exit
  %new_row_rem = phi i32 [ %new_row_rem.pre, %grow_rem_arch ], [ %cnt_rem1, %world_get_or_create_archetype.exit ]
  %tables_rem_tr2 = phi ptr [ %tables_rem_tr2.pre, %grow_rem_arch ], [ %tables_rem_tr1, %world_get_or_create_archetype.exit ]
  %cur_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %3
  %new_arch_ptr_rem2 = getelementptr inbounds %struct.Archetype, ptr %tables_rem_tr2, i64 %.pre-phi
  %cnt_slot_rem2 = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 8
  %next_cnt_rem = add i32 %new_row_rem, 1
  store i32 %next_cnt_rem, ptr %cnt_slot_rem2, align 4
  %new_ent_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 16
  %new_ent_raw_rem = load ptr, ptr %new_ent_rem, align 8
  %6 = sext i32 %new_row_rem to i64
  %new_ent_elem_rem = getelementptr inbounds i32, ptr %new_ent_raw_rem, i64 %6
  store i32 %1, ptr %new_ent_elem_rem, align 4
  %cur_cols_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 24
  %rem_has_ChildOf = and i64 %cur_mask_val_rem, 1
  %is_has_rem_ChildOf.not = icmp eq i64 %rem_has_ChildOf, 0
  br i1 %is_has_rem_ChildOf.not, label %skip_rem_ChildOf, label %copy_rem_ChildOf

copy_rem_ChildOf:                                 ; preds = %after_grow_rem_arch
  %new_cols_rem = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 24
  %rem_src_raw_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %7 = sext i32 %cur_row_rem to i64
  %rem_src_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_src_raw_ChildOf, i64 %7
  %rem_dst_raw_ChildOf = load ptr, ptr %new_cols_rem, align 8
  %rem_dst_elem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %rem_dst_raw_ChildOf, i64 %6
  %8 = load i32, ptr %rem_src_elem_ChildOf, align 1
  store i32 %8, ptr %rem_dst_elem_ChildOf, align 1
  br label %skip_rem_ChildOf

skip_rem_ChildOf:                                 ; preds = %copy_rem_ChildOf, %after_grow_rem_arch
  %rem_has_Position = and i64 %cur_mask_val_rem, 2
  %is_has_rem_Position.not = icmp eq i64 %rem_has_Position, 0
  br i1 %is_has_rem_Position.not, label %skip_rem_Position, label %copy_rem_Position

copy_rem_Position:                                ; preds = %skip_rem_ChildOf
  %rem_src_col_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %rem_src_raw_Position = load ptr, ptr %rem_src_col_Position, align 8
  %9 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_src_raw_Position, i64 %9
  %rem_dst_col_Position = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 32
  %rem_dst_raw_Position = load ptr, ptr %rem_dst_col_Position, align 8
  %rem_dst_elem_Position = getelementptr inbounds %struct.Position, ptr %rem_dst_raw_Position, i64 %6
  %10 = load i64, ptr %rem_src_elem_Position, align 1
  store i64 %10, ptr %rem_dst_elem_Position, align 1
  br label %skip_rem_Position

skip_rem_Position:                                ; preds = %copy_rem_Position, %skip_rem_ChildOf
  %rem_has_Velocity = and i64 %cur_mask_val_rem, 4
  %is_has_rem_Velocity.not = icmp eq i64 %rem_has_Velocity, 0
  br i1 %is_has_rem_Velocity.not, label %skip_rem_Velocity, label %copy_rem_Velocity

copy_rem_Velocity:                                ; preds = %skip_rem_Position
  %rem_src_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %rem_src_raw_Velocity = load ptr, ptr %rem_src_col_Velocity, align 8
  %11 = sext i32 %cur_row_rem to i64
  %rem_src_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_src_raw_Velocity, i64 %11
  %rem_dst_col_Velocity = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 40
  %rem_dst_raw_Velocity = load ptr, ptr %rem_dst_col_Velocity, align 8
  %rem_dst_elem_Velocity = getelementptr inbounds %struct.Velocity, ptr %rem_dst_raw_Velocity, i64 %6
  %12 = load i64, ptr %rem_src_elem_Velocity, align 1
  store i64 %12, ptr %rem_dst_elem_Velocity, align 1
  br label %skip_rem_Velocity

skip_rem_Velocity:                                ; preds = %copy_rem_Velocity, %skip_rem_Position
  %rem_has_PlayerTag = and i64 %cur_mask_val_rem, 8
  %is_has_rem_PlayerTag.not = icmp eq i64 %rem_has_PlayerTag, 0
  br i1 %is_has_rem_PlayerTag.not, label %skip_rem_PlayerTag, label %copy_rem_PlayerTag

copy_rem_PlayerTag:                               ; preds = %skip_rem_Velocity
  %rem_src_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %rem_src_raw_PlayerTag = load ptr, ptr %rem_src_col_PlayerTag, align 8
  %13 = sext i32 %cur_row_rem to i64
  %rem_src_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_src_raw_PlayerTag, i64 %13
  %rem_dst_col_PlayerTag = getelementptr inbounds nuw i8, ptr %new_arch_ptr_rem2, i64 48
  %rem_dst_raw_PlayerTag = load ptr, ptr %rem_dst_col_PlayerTag, align 8
  %rem_dst_elem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %rem_dst_raw_PlayerTag, i64 %6
  %14 = load i32, ptr %rem_src_elem_PlayerTag, align 1
  store i32 %14, ptr %rem_dst_elem_PlayerTag, align 1
  br label %skip_rem_PlayerTag

skip_rem_PlayerTag:                               ; preds = %copy_rem_PlayerTag, %skip_rem_Velocity
  %cnt_slot_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 8
  %cnt_rem = load i32, ptr %cnt_slot_rem, align 4
  %last_row_rem = add i32 %cnt_rem, -1
  store i32 %last_row_rem, ptr %cnt_slot_rem, align 4
  %is_last_rem = icmp eq i32 %cur_row_rem, %last_row_rem
  br i1 %is_last_rem, label %after_swap_rem, label %do_swap_rem

do_swap_rem:                                      ; preds = %skip_rem_PlayerTag
  %ent_sr_rem = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 16
  %ent_raw_sr_rem = load ptr, ptr %ent_sr_rem, align 8
  %15 = sext i32 %last_row_rem to i64
  %last_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %15
  %moved_e_rem = load i32, ptr %last_ent_rem, align 4
  %16 = sext i32 %cur_row_rem to i64
  %cur_ent_rem = getelementptr inbounds i32, ptr %ent_raw_sr_rem, i64 %16
  store i32 %moved_e_rem, ptr %cur_ent_rem, align 4
  br i1 %is_has_rem_ChildOf.not, label %skip_sw_rem_ChildOf, label %swap_rem_ChildOf

after_swap_rem:                                   ; preds = %swap_rem_Obstacle, %skip_rem_PlayerTag
  %arch_arr_rem_tr = load ptr, ptr %ent_arch_slot_rem, align 8
  %rem_arch_slot_tr = getelementptr inbounds i32, ptr %arch_arr_rem_tr, i64 %2
  store i32 %common.ret.op.i, ptr %rem_arch_slot_tr, align 4
  %row_arr_rem_tr = load ptr, ptr %ent_row_slot_rem, align 8
  %rem_row_slot_tr = getelementptr inbounds i32, ptr %row_arr_rem_tr, i64 %2
  store i32 %new_row_rem, ptr %rem_row_slot_tr, align 4
  br label %exit_remove

swap_rem_ChildOf:                                 ; preds = %do_swap_rem
  %sw_raw_rem_ChildOf = load ptr, ptr %cur_cols_rem, align 8
  %sw_src_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %15
  %sw_dst_rem_ChildOf = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_rem_ChildOf, i64 %16
  %17 = load i32, ptr %sw_src_rem_ChildOf, align 1
  store i32 %17, ptr %sw_dst_rem_ChildOf, align 1
  br label %skip_sw_rem_ChildOf

skip_sw_rem_ChildOf:                              ; preds = %swap_rem_ChildOf, %do_swap_rem
  br i1 %is_has_rem_Position.not, label %skip_sw_rem_Position, label %swap_rem_Position

swap_rem_Position:                                ; preds = %skip_sw_rem_ChildOf
  %sw_col_rem_Position = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 32
  %sw_raw_rem_Position = load ptr, ptr %sw_col_rem_Position, align 8
  %sw_src_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %15
  %sw_dst_rem_Position = getelementptr inbounds %struct.Position, ptr %sw_raw_rem_Position, i64 %16
  %18 = load i64, ptr %sw_src_rem_Position, align 1
  store i64 %18, ptr %sw_dst_rem_Position, align 1
  br label %skip_sw_rem_Position

skip_sw_rem_Position:                             ; preds = %swap_rem_Position, %skip_sw_rem_ChildOf
  br i1 %is_has_rem_Velocity.not, label %skip_sw_rem_Velocity, label %swap_rem_Velocity

swap_rem_Velocity:                                ; preds = %skip_sw_rem_Position
  %sw_col_rem_Velocity = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 40
  %sw_raw_rem_Velocity = load ptr, ptr %sw_col_rem_Velocity, align 8
  %sw_src_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %15
  %sw_dst_rem_Velocity = getelementptr inbounds %struct.Velocity, ptr %sw_raw_rem_Velocity, i64 %16
  %19 = load i64, ptr %sw_src_rem_Velocity, align 1
  store i64 %19, ptr %sw_dst_rem_Velocity, align 1
  br label %skip_sw_rem_Velocity

skip_sw_rem_Velocity:                             ; preds = %swap_rem_Velocity, %skip_sw_rem_Position
  br i1 %is_has_rem_PlayerTag.not, label %swap_rem_Obstacle, label %swap_rem_PlayerTag

swap_rem_PlayerTag:                               ; preds = %skip_sw_rem_Velocity
  %sw_col_rem_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 48
  %sw_raw_rem_PlayerTag = load ptr, ptr %sw_col_rem_PlayerTag, align 8
  %sw_src_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %15
  %sw_dst_rem_PlayerTag = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_rem_PlayerTag, i64 %16
  %20 = load i32, ptr %sw_src_rem_PlayerTag, align 1
  store i32 %20, ptr %sw_dst_rem_PlayerTag, align 1
  br label %swap_rem_Obstacle

swap_rem_Obstacle:                                ; preds = %skip_sw_rem_Velocity, %swap_rem_PlayerTag
  %sw_col_rem_Obstacle = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_rem2, i64 56
  %sw_raw_rem_Obstacle = load ptr, ptr %sw_col_rem_Obstacle, align 8
  %sw_src_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %15
  %sw_dst_rem_Obstacle = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_rem_Obstacle, i64 %16
  %21 = load i8, ptr %sw_src_rem_Obstacle, align 1
  store i8 %21, ptr %sw_dst_rem_Obstacle, align 1
  %row_arr_rem_sr = load ptr, ptr %ent_row_slot_rem, align 8
  %22 = sext i32 %moved_e_rem to i64
  %moved_e_row_slot_rem = getelementptr inbounds i32, ptr %row_arr_rem_sr, i64 %22
  store i32 %cur_row_rem, ptr %moved_e_row_slot_rem, align 4
  br label %after_swap_rem
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none)
define i1 @world_has_Obstacle(ptr nocapture readonly %0, i32 %1) local_unnamed_addr #8 {
entry:
  %ent_arch_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 24
  %arch_arr_has = load ptr, ptr %ent_arch_slot_has, align 8
  %2 = sext i32 %1 to i64
  %has_arch_slot = getelementptr inbounds i32, ptr %arch_arr_has, i64 %2
  %cur_arch_idx_has = load i32, ptr %has_arch_slot, align 4
  %is_alive_has = icmp sgt i32 %cur_arch_idx_has, -1
  br i1 %is_alive_has, label %check_mask, label %common.ret

common.ret:                                       ; preds = %entry, %check_mask
  %common.ret.op = phi i1 [ %res_has, %check_mask ], [ false, %entry ]
  ret i1 %common.ret.op

check_mask:                                       ; preds = %entry
  %tables_slot_has = getelementptr inbounds nuw i8, ptr %0, i64 8
  %tables_has = load ptr, ptr %tables_slot_has, align 8
  %3 = zext nneg i32 %cur_arch_idx_has to i64
  %arch_ptr_has = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has, i64 %3
  %arch_mask_has = load i64, ptr %arch_ptr_has, align 8
  %bit_and_has = and i64 %arch_mask_has, 16
  %res_has = icmp ne i64 %bit_and_has, 0
  br label %common.ret
}

define void @world_cmd_add_Obstacle(ptr %0, i32 %1, i1 %2) local_unnamed_addr {
entry:
  %cmd_lock_slot_cset = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 13
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_cset.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_cset.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_cset.pre, 13
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_cset.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_cset = phi ptr [ %data_ptr_cset.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_cset = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_cset.pre, %grow_cmd.i ]
  %cur_cnt_cset64 = zext i32 %cur_cnt_cset to i64
  %write_ptr_cset = getelementptr inbounds nuw i8, ptr %data_ptr_cset, i64 %cur_cnt_cset64
  store i32 3, ptr %write_ptr_cset, align 4
  %e_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 4
  store i32 %1, ptr %e_slot_cset, align 4
  %comp_id_slot_cset = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 8
  store i32 4, ptr %comp_id_slot_cset, align 4
  %payload_raw = getelementptr inbounds nuw i8, ptr %write_ptr_cset, i64 12
  store i1 %2, ptr %payload_raw, align 1
  store i32 %new_cnt_cset.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_cset)
  ret void
}

define void @world_cmd_remove_Obstacle(ptr %0, i32 %1) local_unnamed_addr {
entry:
  %cmd_lock_slot_crem = getelementptr inbounds nuw i8, ptr %0, i64 56
  tail call void @AcquireSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  %cmd_cnt_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_cap_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 44
  %cur_cmd_cnt.i = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %cur_cmd_cap.i = load i32, ptr %cmd_cap_slot_ec.i, align 4
  %needed_total.i = add i32 %cur_cmd_cnt.i, 12
  %need_grow_cmd.i = icmp sgt i32 %needed_total.i, %cur_cmd_cap.i
  %cmd_data_slot_ec.i = getelementptr inbounds nuw i8, ptr %0, i64 48
  br i1 %need_grow_cmd.i, label %grow_cmd.i, label %entry.world_cmd_ensure_cap.exit_crit_edge

entry.world_cmd_ensure_cap.exit_crit_edge:        ; preds = %entry
  %data_ptr_crem.pre = load ptr, ptr %cmd_data_slot_ec.i, align 8
  br label %world_cmd_ensure_cap.exit

grow_cmd.i:                                       ; preds = %entry
  %double_cmd_cap.i = shl i32 %cur_cmd_cap.i, 1
  %at_least_1k.i = tail call i32 @llvm.smax.i32(i32 %double_cmd_cap.i, i32 %needed_total.i)
  %final_cap.i = tail call i32 @llvm.smax.i32(i32 %at_least_1k.i, i32 1024)
  store i32 %final_cap.i, ptr %cmd_cap_slot_ec.i, align 4
  %final_cap64.i = zext nneg i32 %final_cap.i to i64
  %cur_cmd_data.i = load ptr, ptr %cmd_data_slot_ec.i, align 8
  %new_cmd_data.i = tail call ptr @realloc(ptr %cur_cmd_data.i, i64 %final_cap64.i)
  store ptr %new_cmd_data.i, ptr %cmd_data_slot_ec.i, align 8
  %cur_cnt_crem.pre = load i32, ptr %cmd_cnt_slot_ec.i, align 4
  %.pre = add i32 %cur_cnt_crem.pre, 12
  br label %world_cmd_ensure_cap.exit

world_cmd_ensure_cap.exit:                        ; preds = %entry.world_cmd_ensure_cap.exit_crit_edge, %grow_cmd.i
  %new_cnt_crem.pre-phi = phi i32 [ %needed_total.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %.pre, %grow_cmd.i ]
  %data_ptr_crem = phi ptr [ %data_ptr_crem.pre, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %new_cmd_data.i, %grow_cmd.i ]
  %cur_cnt_crem = phi i32 [ %cur_cmd_cnt.i, %entry.world_cmd_ensure_cap.exit_crit_edge ], [ %cur_cnt_crem.pre, %grow_cmd.i ]
  %cur_cnt_crem64 = zext i32 %cur_cnt_crem to i64
  %write_ptr_crem = getelementptr inbounds nuw i8, ptr %data_ptr_crem, i64 %cur_cnt_crem64
  store i32 4, ptr %write_ptr_crem, align 4
  %e_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 4
  store i32 %1, ptr %e_slot_crem, align 4
  %comp_id_slot_crem = getelementptr inbounds nuw i8, ptr %write_ptr_crem, i64 8
  store i32 4, ptr %comp_id_slot_crem, align 4
  store i32 %new_cnt_crem.pre-phi, ptr %cmd_cnt_slot_ec.i, align 4
  tail call void @ReleaseSRWLockExclusive(ptr nonnull %cmd_lock_slot_crem)
  ret void
}

; Function Attrs: nounwind
define void @world_apply_commands(ptr nocapture %0) local_unnamed_addr #4 {
entry:
  %cmd_cnt_slot_ac = getelementptr inbounds nuw i8, ptr %0, i64 40
  %cmd_data_slot_ac = getelementptr inbounds nuw i8, ptr %0, i64 48
  %total_cmd_bytes = load i32, ptr %cmd_cnt_slot_ac, align 4
  %has_cmds = icmp sgt i32 %total_cmd_bytes, 0
  br i1 %has_cmds, label %ac_loop_body.lr.ph, label %ac_exit

ac_loop_body.lr.ph:                               ; preds = %entry
  %ent_count_slot_ds.i = getelementptr inbounds nuw i8, ptr %0, i64 16
  %ent_arch_slot_ds.i = getelementptr inbounds nuw i8, ptr %0, i64 24
  %tables_slot_ds.i = getelementptr inbounds nuw i8, ptr %0, i64 8
  %ent_row_slot_ds.i = getelementptr inbounds nuw i8, ptr %0, i64 32
  br label %ac_loop_body

ac_exit:                                          ; preds = %ac_loop_cond.backedge, %entry
  store i32 0, ptr %cmd_cnt_slot_ac, align 4
  ret void

ac_loop_body:                                     ; preds = %ac_loop_body.lr.ph, %ac_loop_cond.backedge
  %next_off_def1114 = phi i32 [ 0, %ac_loop_body.lr.ph ], [ %next_off_1, %ac_loop_cond.backedge ]
  %data_ptr_ac = load ptr, ptr %cmd_data_slot_ac, align 8
  %cur_offset64 = zext i32 %next_off_def1114 to i64
  %cur_cmd_ptr = getelementptr inbounds nuw i8, ptr %data_ptr_ac, i64 %cur_offset64
  %op_val = load i32, ptr %cur_cmd_ptr, align 4
  %e_slot = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 4
  %e_val = load i32, ptr %e_slot, align 4
  switch i32 %op_val, label %ac_loop_cond.backedge [
    i32 1, label %op_spawn
    i32 2, label %op_despawn
    i32 3, label %op_set
    i32 4, label %op_remove
  ]

op_spawn:                                         ; preds = %ac_loop_body
  tail call void @world_assign_a0(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge

ac_loop_cond.backedge:                            ; preds = %op_remove, %op_set, %ac_loop_body, %after_swap_ds.i, %check_arch.i, %op_despawn, %op_spawn, %set_case_ChildOf, %set_case_Position, %set_case_Velocity, %set_case_PlayerTag, %set_case_Obstacle, %rem_case_ChildOf, %rem_case_Position, %rem_case_Velocity, %rem_case_PlayerTag, %rem_case_Obstacle
  %.sink = phi i32 [ 8, %op_spawn ], [ 16, %set_case_ChildOf ], [ 20, %set_case_Position ], [ 20, %set_case_Velocity ], [ 16, %set_case_PlayerTag ], [ 13, %set_case_Obstacle ], [ 12, %rem_case_ChildOf ], [ 12, %rem_case_Position ], [ 12, %rem_case_Velocity ], [ 12, %rem_case_PlayerTag ], [ 12, %rem_case_Obstacle ], [ 8, %op_despawn ], [ 8, %check_arch.i ], [ 8, %after_swap_ds.i ], [ 4, %ac_loop_body ], [ 12, %op_set ], [ 12, %op_remove ]
  %next_off_1 = add i32 %next_off_def1114, %.sink
  %has_more_cmds = icmp slt i32 %next_off_1, %total_cmd_bytes
  br i1 %has_more_cmds, label %ac_loop_body, label %ac_exit

op_despawn:                                       ; preds = %ac_loop_body
  %total_ents_ds.i = load i32, ptr %ent_count_slot_ds.i, align 4
  %e_non_neg_ds.i = icmp sgt i32 %e_val, -1
  %e_in_bounds_ds.i = icmp slt i32 %e_val, %total_ents_ds.i
  %is_valid_id_ds.i = and i1 %e_non_neg_ds.i, %e_in_bounds_ds.i
  br i1 %is_valid_id_ds.i, label %check_arch.i, label %ac_loop_cond.backedge

check_arch.i:                                     ; preds = %op_despawn
  %arch_arr_ds.i = load ptr, ptr %ent_arch_slot_ds.i, align 8
  %1 = zext nneg i32 %e_val to i64
  %e_arch_slot_ds_inst.i = getelementptr inbounds nuw i32, ptr %arch_arr_ds.i, i64 %1
  %cur_arch_idx_ds.i = load i32, ptr %e_arch_slot_ds_inst.i, align 4
  %is_alive_ds.i = icmp sgt i32 %cur_arch_idx_ds.i, -1
  br i1 %is_alive_ds.i, label %do_despawn.i, label %ac_loop_cond.backedge

do_despawn.i:                                     ; preds = %check_arch.i
  %row_arr_ds.i = load ptr, ptr %ent_row_slot_ds.i, align 8
  %e_row_slot_ds_inst.i = getelementptr inbounds nuw i32, ptr %row_arr_ds.i, i64 %1
  %cur_row_ds.i = load i32, ptr %e_row_slot_ds_inst.i, align 4
  %tables_base_ds.i = load ptr, ptr %tables_slot_ds.i, align 8
  %2 = zext nneg i32 %cur_arch_idx_ds.i to i64
  %cur_arch_ptr_ds.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base_ds.i, i64 %2
  %cur_cnt_slot_ds.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 8
  %cur_arch_cnt_ds.i = load i32, ptr %cur_cnt_slot_ds.i, align 4
  %last_row_ds.i = add i32 %cur_arch_cnt_ds.i, -1
  store i32 %last_row_ds.i, ptr %cur_cnt_slot_ds.i, align 4
  %is_last_row_ds.i = icmp eq i32 %cur_row_ds.i, %last_row_ds.i
  br i1 %is_last_row_ds.i, label %after_swap_ds.i, label %do_swap_ds.i

do_swap_ds.i:                                     ; preds = %do_despawn.i
  %cur_ent_slot_ds.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 16
  %cur_ent_raw_ds.i = load ptr, ptr %cur_ent_slot_ds.i, align 8
  %3 = sext i32 %last_row_ds.i to i64
  %last_ent_elem_ds.i = getelementptr inbounds i32, ptr %cur_ent_raw_ds.i, i64 %3
  %moved_e_ds.i = load i32, ptr %last_ent_elem_ds.i, align 4
  %4 = sext i32 %cur_row_ds.i to i64
  %cur_ent_elem_ds.i = getelementptr inbounds i32, ptr %cur_ent_raw_ds.i, i64 %4
  store i32 %moved_e_ds.i, ptr %cur_ent_elem_ds.i, align 4
  %cur_mask_ds.i = load i64, ptr %cur_arch_ptr_ds.i, align 8
  %has_sw_ds_ChildOf.i = and i64 %cur_mask_ds.i, 1
  %is_has_sw_ds_ChildOf.not.i = icmp eq i64 %has_sw_ds_ChildOf.i, 0
  br i1 %is_has_sw_ds_ChildOf.not.i, label %skip_sw_ds_ChildOf.i, label %swap_ds_ChildOf.i

after_swap_ds.i:                                  ; preds = %skip_sw_ds_Obstacle.i, %do_despawn.i
  store i32 -1, ptr %e_arch_slot_ds_inst.i, align 4
  store i32 -1, ptr %e_row_slot_ds_inst.i, align 4
  br label %ac_loop_cond.backedge

swap_ds_ChildOf.i:                                ; preds = %do_swap_ds.i
  %cur_cols_arr_ds.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 24
  %sw_raw_ds_ChildOf.i = load ptr, ptr %cur_cols_arr_ds.i, align 8
  %sw_src_ds_ChildOf.i = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf.i, i64 %3
  %sw_dst_ds_ChildOf.i = getelementptr inbounds %struct.ChildOf, ptr %sw_raw_ds_ChildOf.i, i64 %4
  %5 = load i32, ptr %sw_src_ds_ChildOf.i, align 1
  store i32 %5, ptr %sw_dst_ds_ChildOf.i, align 1
  br label %skip_sw_ds_ChildOf.i

skip_sw_ds_ChildOf.i:                             ; preds = %swap_ds_ChildOf.i, %do_swap_ds.i
  %has_sw_ds_Position.i = and i64 %cur_mask_ds.i, 2
  %is_has_sw_ds_Position.not.i = icmp eq i64 %has_sw_ds_Position.i, 0
  br i1 %is_has_sw_ds_Position.not.i, label %skip_sw_ds_Position.i, label %swap_ds_Position.i

swap_ds_Position.i:                               ; preds = %skip_sw_ds_ChildOf.i
  %sw_col_ds_Position.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 32
  %sw_raw_ds_Position.i = load ptr, ptr %sw_col_ds_Position.i, align 8
  %sw_src_ds_Position.i = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position.i, i64 %3
  %sw_dst_ds_Position.i = getelementptr inbounds %struct.Position, ptr %sw_raw_ds_Position.i, i64 %4
  %6 = load i64, ptr %sw_src_ds_Position.i, align 1
  store i64 %6, ptr %sw_dst_ds_Position.i, align 1
  br label %skip_sw_ds_Position.i

skip_sw_ds_Position.i:                            ; preds = %swap_ds_Position.i, %skip_sw_ds_ChildOf.i
  %has_sw_ds_Velocity.i = and i64 %cur_mask_ds.i, 4
  %is_has_sw_ds_Velocity.not.i = icmp eq i64 %has_sw_ds_Velocity.i, 0
  br i1 %is_has_sw_ds_Velocity.not.i, label %skip_sw_ds_Velocity.i, label %swap_ds_Velocity.i

swap_ds_Velocity.i:                               ; preds = %skip_sw_ds_Position.i
  %sw_col_ds_Velocity.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 40
  %sw_raw_ds_Velocity.i = load ptr, ptr %sw_col_ds_Velocity.i, align 8
  %sw_src_ds_Velocity.i = getelementptr inbounds %struct.Velocity, ptr %sw_raw_ds_Velocity.i, i64 %3
  %sw_dst_ds_Velocity.i = getelementptr inbounds %struct.Velocity, ptr %sw_raw_ds_Velocity.i, i64 %4
  %7 = load i64, ptr %sw_src_ds_Velocity.i, align 1
  store i64 %7, ptr %sw_dst_ds_Velocity.i, align 1
  br label %skip_sw_ds_Velocity.i

skip_sw_ds_Velocity.i:                            ; preds = %swap_ds_Velocity.i, %skip_sw_ds_Position.i
  %has_sw_ds_PlayerTag.i = and i64 %cur_mask_ds.i, 8
  %is_has_sw_ds_PlayerTag.not.i = icmp eq i64 %has_sw_ds_PlayerTag.i, 0
  br i1 %is_has_sw_ds_PlayerTag.not.i, label %skip_sw_ds_PlayerTag.i, label %swap_ds_PlayerTag.i

swap_ds_PlayerTag.i:                              ; preds = %skip_sw_ds_Velocity.i
  %sw_col_ds_PlayerTag.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 48
  %sw_raw_ds_PlayerTag.i = load ptr, ptr %sw_col_ds_PlayerTag.i, align 8
  %sw_src_ds_PlayerTag.i = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_ds_PlayerTag.i, i64 %3
  %sw_dst_ds_PlayerTag.i = getelementptr inbounds %struct.PlayerTag, ptr %sw_raw_ds_PlayerTag.i, i64 %4
  %8 = load i32, ptr %sw_src_ds_PlayerTag.i, align 1
  store i32 %8, ptr %sw_dst_ds_PlayerTag.i, align 1
  br label %skip_sw_ds_PlayerTag.i

skip_sw_ds_PlayerTag.i:                           ; preds = %swap_ds_PlayerTag.i, %skip_sw_ds_Velocity.i
  %has_sw_ds_Obstacle.i = and i64 %cur_mask_ds.i, 16
  %is_has_sw_ds_Obstacle.not.i = icmp eq i64 %has_sw_ds_Obstacle.i, 0
  br i1 %is_has_sw_ds_Obstacle.not.i, label %skip_sw_ds_Obstacle.i, label %swap_ds_Obstacle.i

swap_ds_Obstacle.i:                               ; preds = %skip_sw_ds_PlayerTag.i
  %sw_col_ds_Obstacle.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr_ds.i, i64 56
  %sw_raw_ds_Obstacle.i = load ptr, ptr %sw_col_ds_Obstacle.i, align 8
  %sw_src_ds_Obstacle.i = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_ds_Obstacle.i, i64 %3
  %sw_dst_ds_Obstacle.i = getelementptr inbounds %struct.Obstacle, ptr %sw_raw_ds_Obstacle.i, i64 %4
  %9 = load i8, ptr %sw_src_ds_Obstacle.i, align 1
  store i8 %9, ptr %sw_dst_ds_Obstacle.i, align 1
  br label %skip_sw_ds_Obstacle.i

skip_sw_ds_Obstacle.i:                            ; preds = %swap_ds_Obstacle.i, %skip_sw_ds_PlayerTag.i
  %10 = sext i32 %moved_e_ds.i to i64
  %moved_e_row_slot_ds.i = getelementptr inbounds i32, ptr %row_arr_ds.i, i64 %10
  store i32 %cur_row_ds.i, ptr %moved_e_row_slot_ds.i, align 4
  br label %after_swap_ds.i

op_set:                                           ; preds = %ac_loop_body
  %comp_id_slot_set = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 8
  %comp_id_set = load i32, ptr %comp_id_slot_set, align 4
  %payload_ptr_set = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 12
  switch i32 %comp_id_set, label %ac_loop_cond.backedge [
    i32 0, label %set_case_ChildOf
    i32 1, label %set_case_Position
    i32 2, label %set_case_Velocity
    i32 3, label %set_case_PlayerTag
    i32 4, label %set_case_Obstacle
  ]

op_remove:                                        ; preds = %ac_loop_body
  %comp_id_slot_rem = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 8
  %comp_id_rem = load i32, ptr %comp_id_slot_rem, align 4
  switch i32 %comp_id_rem, label %ac_loop_cond.backedge [
    i32 0, label %rem_case_ChildOf
    i32 1, label %rem_case_Position
    i32 2, label %rem_case_Velocity
    i32 3, label %rem_case_PlayerTag
    i32 4, label %rem_case_Obstacle
  ]

set_case_ChildOf:                                 ; preds = %op_set
  %f_val_0 = load i32, ptr %payload_ptr_set, align 4
  tail call void @world_add_ChildOf(ptr nonnull %0, i32 %e_val, i32 %f_val_0)
  br label %ac_loop_cond.backedge

set_case_Position:                                ; preds = %op_set
  %f_val_02 = load float, ptr %payload_ptr_set, align 4
  %p_f_1 = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 16
  %f_val_1 = load float, ptr %p_f_1, align 4
  tail call void @world_add_Position(ptr nonnull %0, i32 %e_val, float %f_val_02, float %f_val_1)
  br label %ac_loop_cond.backedge

set_case_Velocity:                                ; preds = %op_set
  %f_val_04 = load float, ptr %payload_ptr_set, align 4
  %p_f_15 = getelementptr inbounds nuw i8, ptr %cur_cmd_ptr, i64 16
  %f_val_16 = load float, ptr %p_f_15, align 4
  tail call void @world_add_Velocity(ptr nonnull %0, i32 %e_val, float %f_val_04, float %f_val_16)
  br label %ac_loop_cond.backedge

set_case_PlayerTag:                               ; preds = %op_set
  %f_val_08 = load i32, ptr %payload_ptr_set, align 4
  tail call void @world_add_PlayerTag(ptr nonnull %0, i32 %e_val, i32 %f_val_08)
  br label %ac_loop_cond.backedge

set_case_Obstacle:                                ; preds = %op_set
  %f_val_010 = load i1, ptr %payload_ptr_set, align 1
  tail call void @world_add_Obstacle(ptr nonnull %0, i32 %e_val, i1 %f_val_010)
  br label %ac_loop_cond.backedge

rem_case_ChildOf:                                 ; preds = %op_remove
  tail call void @world_remove_ChildOf(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge

rem_case_Position:                                ; preds = %op_remove
  tail call void @world_remove_Position(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge

rem_case_Velocity:                                ; preds = %op_remove
  tail call void @world_remove_Velocity(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge

rem_case_PlayerTag:                               ; preds = %op_remove
  tail call void @world_remove_PlayerTag(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge

rem_case_Obstacle:                                ; preds = %op_remove
  tail call void @world_remove_Obstacle(ptr nonnull %0, i32 %e_val)
  br label %ac_loop_cond.backedge
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(argmem: write)
define void @world_set_Time(ptr nocapture writeonly initializes((64, 68)) %0, float %1) local_unnamed_addr #9 {
entry:
  %res_Time_slot = getelementptr inbounds nuw i8, ptr %0, i64 64
  store float %1, ptr %res_Time_slot, align 4
  ret void
}

; Function Attrs: nounwind
define void @world_sort_hierarchy(ptr nocapture readonly %0) local_unnamed_addr #4 {
entry:
  %tables_slot_sh = getelementptr inbounds nuw i8, ptr %0, i64 8
  %ent_row_slot_sh = getelementptr inbounds nuw i8, ptr %0, i64 32
  %num_archs = load i32, ptr %0, align 4
  %has_more_archs6 = icmp sgt i32 %num_archs, 0
  br i1 %has_more_archs6, label %arch_loop_body.preheader, label %arch_loop_exit

arch_loop_body.preheader:                         ; preds = %entry
  %wide.trip.count24 = zext nneg i32 %num_archs to i64
  br label %arch_loop_body

arch_loop_body:                                   ; preds = %arch_loop_body.preheader, %next_arch
  %indvars.iv21 = phi i64 [ 0, %arch_loop_body.preheader ], [ %indvars.iv.next22, %next_arch ]
  %t_base = load ptr, ptr %tables_slot_sh, align 8
  %cur_a = getelementptr inbounds nuw %struct.Archetype, ptr %t_base, i64 %indvars.iv21
  %m_val = load i64, ptr %cur_a, align 8
  %and_co = and i64 %m_val, 1
  %has_co.not = icmp eq i64 %and_co, 0
  br i1 %has_co.not, label %next_arch, label %check_sort

arch_loop_exit:                                   ; preds = %next_arch, %entry
  ret void

check_sort:                                       ; preds = %arch_loop_body
  %cnt_slot_sh = getelementptr inbounds nuw i8, ptr %cur_a, i64 8
  %cnt_sh = load i32, ptr %cnt_slot_sh, align 4
  %can_sort = icmp sgt i32 %cnt_sh, 1
  br i1 %can_sort, label %do_sort, label %next_arch

next_arch:                                        ; preds = %sort_outer_exit, %check_sort, %arch_loop_body
  %indvars.iv.next22 = add nuw nsw i64 %indvars.iv21, 1
  %exitcond25.not = icmp eq i64 %indvars.iv.next22, %wide.trip.count24
  br i1 %exitcond25.not, label %arch_loop_exit, label %arch_loop_body

do_sort:                                          ; preds = %check_sort
  %count64 = zext nneg i32 %cnt_sh to i64
  %bytes_needed = shl nuw nsw i64 %count64, 2
  %depths_raw = tail call ptr @malloc(i64 %bytes_needed)
  %cols_sh = getelementptr inbounds nuw i8, ptr %cur_a, i64 24
  %co_col_raw = load ptr, ptr %cols_sh, align 8
  %min.iters.check = icmp ult i32 %cnt_sh, 8
  br i1 %min.iters.check, label %d_init_body.preheader, label %vector.ph

vector.ph:                                        ; preds = %do_sort
  %n.vec = and i64 %count64, 2147483640
  br label %vector.body

vector.body:                                      ; preds = %vector.body, %vector.ph
  %index = phi i64 [ 0, %vector.ph ], [ %index.next, %vector.body ]
  %1 = getelementptr inbounds nuw %struct.ChildOf, ptr %co_col_raw, i64 %index
  %2 = getelementptr inbounds nuw i8, ptr %1, i64 16
  %wide.load = load <4 x i32>, ptr %1, align 4
  %wide.load26 = load <4 x i32>, ptr %2, align 4
  %3 = icmp sgt <4 x i32> %wide.load, splat (i32 -1)
  %4 = icmp sgt <4 x i32> %wide.load26, splat (i32 -1)
  %5 = zext <4 x i1> %3 to <4 x i32>
  %6 = zext <4 x i1> %4 to <4 x i32>
  %7 = getelementptr inbounds nuw i32, ptr %depths_raw, i64 %index
  %8 = getelementptr inbounds nuw i8, ptr %7, i64 16
  store <4 x i32> %5, ptr %7, align 4
  store <4 x i32> %6, ptr %8, align 4
  %index.next = add nuw i64 %index, 8
  %9 = icmp eq i64 %index.next, %n.vec
  br i1 %9, label %middle.block, label %vector.body, !llvm.loop !0

middle.block:                                     ; preds = %vector.body
  %cmp.n = icmp eq i64 %n.vec, %count64
  br i1 %cmp.n, label %sort_outer_body.lr.ph, label %d_init_body.preheader

d_init_body.preheader:                            ; preds = %do_sort, %middle.block
  %indvars.iv.ph = phi i64 [ 0, %do_sort ], [ %n.vec, %middle.block ]
  br label %d_init_body

sort_outer_body.lr.ph:                            ; preds = %d_init_body, %middle.block
  %outer_limit = add nsw i32 %cnt_sh, -1
  %ent_slot_sh = getelementptr inbounds nuw i8, ptr %cur_a, i64 16
  %sw_co_has_Position = and i64 %m_val, 2
  %is_sw_co_Position.not = icmp eq i64 %sw_co_has_Position, 0
  %sw_sh_col_Position = getelementptr inbounds nuw i8, ptr %cur_a, i64 32
  %sw_co_has_Velocity = and i64 %m_val, 4
  %is_sw_co_Velocity.not = icmp eq i64 %sw_co_has_Velocity, 0
  %sw_sh_col_Velocity = getelementptr inbounds nuw i8, ptr %cur_a, i64 40
  %sw_co_has_PlayerTag = and i64 %m_val, 8
  %is_sw_co_PlayerTag.not = icmp eq i64 %sw_co_has_PlayerTag, 0
  %sw_sh_col_PlayerTag = getelementptr inbounds nuw i8, ptr %cur_a, i64 48
  %sw_co_has_Obstacle = and i64 %m_val, 16
  %is_sw_co_Obstacle.not = icmp eq i64 %sw_co_has_Obstacle, 0
  %sw_sh_col_Obstacle = getelementptr inbounds nuw i8, ptr %cur_a, i64 56
  %wide.trip.count19 = zext i32 %outer_limit to i64
  br label %sort_inner_body.lr.ph

d_init_body:                                      ; preds = %d_init_body.preheader, %d_init_body
  %indvars.iv = phi i64 [ %indvars.iv.next, %d_init_body ], [ %indvars.iv.ph, %d_init_body.preheader ]
  %child_elem = getelementptr inbounds nuw %struct.ChildOf, ptr %co_col_raw, i64 %indvars.iv
  %parent_id = load i32, ptr %child_elem, align 4
  %is_root = icmp sgt i32 %parent_id, -1
  %depth_val = zext i1 %is_root to i32
  %depth_slot = getelementptr inbounds nuw i32, ptr %depths_raw, i64 %indvars.iv
  store i32 %depth_val, ptr %depth_slot, align 4
  %indvars.iv.next = add nuw nsw i64 %indvars.iv, 1
  %exitcond.not = icmp eq i64 %indvars.iv.next, %count64
  br i1 %exitcond.not, label %sort_outer_body.lr.ph, label %d_init_body, !llvm.loop !3

sort_outer_cond.loopexit:                         ; preds = %skip_swap_row
  %indvars.iv.next10 = add nuw nsw i64 %indvars.iv9, 1
  %exitcond20.not = icmp eq i64 %indvars.iv.next17, %wide.trip.count19
  br i1 %exitcond20.not, label %sort_outer_exit, label %sort_inner_body.lr.ph

sort_inner_body.lr.ph:                            ; preds = %sort_outer_cond.loopexit, %sort_outer_body.lr.ph
  %indvars.iv16 = phi i64 [ 0, %sort_outer_body.lr.ph ], [ %indvars.iv.next17, %sort_outer_cond.loopexit ]
  %indvars.iv9 = phi i64 [ 1, %sort_outer_body.lr.ph ], [ %indvars.iv.next10, %sort_outer_cond.loopexit ]
  %indvars.iv.next17 = add nuw nsw i64 %indvars.iv16, 1
  %di_slot = getelementptr inbounds nuw i32, ptr %depths_raw, i64 %indvars.iv16
  %10 = trunc nuw nsw i64 %indvars.iv16 to i32
  br label %sort_inner_body

sort_outer_exit:                                  ; preds = %sort_outer_cond.loopexit
  tail call void @free(ptr nonnull %depths_raw)
  br label %next_arch

sort_inner_body:                                  ; preds = %sort_inner_body.lr.ph, %skip_swap_row
  %indvars.iv11 = phi i64 [ %indvars.iv9, %sort_inner_body.lr.ph ], [ %indvars.iv.next12, %skip_swap_row ]
  %dj_slot = getelementptr inbounds nuw i32, ptr %depths_raw, i64 %indvars.iv11
  %di = load i32, ptr %di_slot, align 4
  %dj = load i32, ptr %dj_slot, align 4
  %need_swap = icmp sgt i32 %di, %dj
  br i1 %need_swap, label %do_swap_row, label %skip_swap_row

do_swap_row:                                      ; preds = %sort_inner_body
  store i32 %dj, ptr %di_slot, align 4
  store i32 %di, ptr %dj_slot, align 4
  %ent_raw_sh = load ptr, ptr %ent_slot_sh, align 8
  %ei_slot = getelementptr inbounds nuw i32, ptr %ent_raw_sh, i64 %indvars.iv16
  %ej_slot = getelementptr inbounds nuw i32, ptr %ent_raw_sh, i64 %indvars.iv11
  %ei = load i32, ptr %ei_slot, align 4
  %ej = load i32, ptr %ej_slot, align 4
  store i32 %ej, ptr %ei_slot, align 4
  store i32 %ei, ptr %ej_slot, align 4
  %row_arr_sh = load ptr, ptr %ent_row_slot_sh, align 8
  %11 = sext i32 %ei to i64
  %ei_row_slot = getelementptr inbounds i32, ptr %row_arr_sh, i64 %11
  %12 = sext i32 %ej to i64
  %ej_row_slot = getelementptr inbounds i32, ptr %row_arr_sh, i64 %12
  %13 = trunc nuw nsw i64 %indvars.iv11 to i32
  store i32 %13, ptr %ei_row_slot, align 4
  store i32 %10, ptr %ej_row_slot, align 4
  %sw_sh_raw_ChildOf = load ptr, ptr %cols_sh, align 8
  %elem_i_ChildOf = getelementptr inbounds nuw %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i64 %indvars.iv16
  %elem_j_ChildOf = getelementptr inbounds nuw %struct.ChildOf, ptr %sw_sh_raw_ChildOf, i64 %indvars.iv11
  %14 = load i32, ptr %elem_i_ChildOf, align 1
  %15 = load i32, ptr %elem_j_ChildOf, align 1
  store i32 %15, ptr %elem_i_ChildOf, align 1
  store i32 %14, ptr %elem_j_ChildOf, align 1
  br i1 %is_sw_co_Position.not, label %skip_sw_sh_Position, label %sw_sh_Position

skip_swap_row:                                    ; preds = %skip_sw_sh_PlayerTag, %sw_sh_Obstacle, %sort_inner_body
  %indvars.iv.next12 = add nuw nsw i64 %indvars.iv11, 1
  %exitcond15.not = icmp eq i64 %indvars.iv.next12, %count64
  br i1 %exitcond15.not, label %sort_outer_cond.loopexit, label %sort_inner_body

sw_sh_Position:                                   ; preds = %do_swap_row
  %sw_sh_raw_Position = load ptr, ptr %sw_sh_col_Position, align 8
  %elem_i_Position = getelementptr inbounds nuw %struct.Position, ptr %sw_sh_raw_Position, i64 %indvars.iv16
  %elem_j_Position = getelementptr inbounds nuw %struct.Position, ptr %sw_sh_raw_Position, i64 %indvars.iv11
  %16 = load i64, ptr %elem_i_Position, align 1
  %17 = load i64, ptr %elem_j_Position, align 1
  store i64 %17, ptr %elem_i_Position, align 1
  store i64 %16, ptr %elem_j_Position, align 1
  br label %skip_sw_sh_Position

skip_sw_sh_Position:                              ; preds = %sw_sh_Position, %do_swap_row
  br i1 %is_sw_co_Velocity.not, label %skip_sw_sh_Velocity, label %sw_sh_Velocity

sw_sh_Velocity:                                   ; preds = %skip_sw_sh_Position
  %sw_sh_raw_Velocity = load ptr, ptr %sw_sh_col_Velocity, align 8
  %elem_i_Velocity = getelementptr inbounds nuw %struct.Velocity, ptr %sw_sh_raw_Velocity, i64 %indvars.iv16
  %elem_j_Velocity = getelementptr inbounds nuw %struct.Velocity, ptr %sw_sh_raw_Velocity, i64 %indvars.iv11
  %18 = load i64, ptr %elem_i_Velocity, align 1
  %19 = load i64, ptr %elem_j_Velocity, align 1
  store i64 %19, ptr %elem_i_Velocity, align 1
  store i64 %18, ptr %elem_j_Velocity, align 1
  br label %skip_sw_sh_Velocity

skip_sw_sh_Velocity:                              ; preds = %sw_sh_Velocity, %skip_sw_sh_Position
  br i1 %is_sw_co_PlayerTag.not, label %skip_sw_sh_PlayerTag, label %sw_sh_PlayerTag

sw_sh_PlayerTag:                                  ; preds = %skip_sw_sh_Velocity
  %sw_sh_raw_PlayerTag = load ptr, ptr %sw_sh_col_PlayerTag, align 8
  %elem_i_PlayerTag = getelementptr inbounds nuw %struct.PlayerTag, ptr %sw_sh_raw_PlayerTag, i64 %indvars.iv16
  %elem_j_PlayerTag = getelementptr inbounds nuw %struct.PlayerTag, ptr %sw_sh_raw_PlayerTag, i64 %indvars.iv11
  %20 = load i32, ptr %elem_i_PlayerTag, align 1
  %21 = load i32, ptr %elem_j_PlayerTag, align 1
  store i32 %21, ptr %elem_i_PlayerTag, align 1
  store i32 %20, ptr %elem_j_PlayerTag, align 1
  br label %skip_sw_sh_PlayerTag

skip_sw_sh_PlayerTag:                             ; preds = %sw_sh_PlayerTag, %skip_sw_sh_Velocity
  br i1 %is_sw_co_Obstacle.not, label %skip_swap_row, label %sw_sh_Obstacle

sw_sh_Obstacle:                                   ; preds = %skip_sw_sh_PlayerTag
  %sw_sh_raw_Obstacle = load ptr, ptr %sw_sh_col_Obstacle, align 8
  %elem_i_Obstacle = getelementptr inbounds nuw %struct.Obstacle, ptr %sw_sh_raw_Obstacle, i64 %indvars.iv16
  %elem_j_Obstacle = getelementptr inbounds nuw %struct.Obstacle, ptr %sw_sh_raw_Obstacle, i64 %indvars.iv11
  %22 = load i8, ptr %elem_i_Obstacle, align 1
  %23 = load i8, ptr %elem_j_Obstacle, align 1
  store i8 %23, ptr %elem_i_Obstacle, align 1
  store i8 %22, ptr %elem_j_Obstacle, align 1
  br label %skip_swap_row
}

; Function Attrs: mustprogress nofree norecurse nosync nounwind willreturn memory(none)
define void @world_swap_events(ptr nocapture readnone %world) local_unnamed_addr #10 {
entry:
  ret void
}

; Function Attrs: nounwind
define noundef i32 @main() local_unnamed_addr #4 {
world_spawn.exit:
  %puts_call = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.28)
  %puts_call1 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.6)
  %puts_call2 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.28)
  %calloc.i = tail call dereferenceable_or_null(72) ptr @calloc(i64 1, i64 72)
  %arch_tables_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 8
  %arch_cap_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 4
  store i32 8, ptr %arch_cap_slot.i.i, align 4
  %malloc.i = tail call dereferenceable_or_null(512) ptr @malloc(i64 512)
  store ptr %malloc.i, ptr %arch_tables_slot.i.i, align 8
  store i32 1, ptr %calloc.i, align 4
  tail call void @llvm.memset.p0.i64(ptr noundef nonnull align 8 dereferenceable(64) %malloc.i, i8 0, i64 64, i1 false)
  %res_Time_slot.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 64
  store float 1.000000e+00, ptr %res_Time_slot.i, align 4
  %ent_count_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 16
  %ent_cap_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 20
  %ent_arch_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 24
  %ent_row_slot.i.i = getelementptr inbounds nuw i8, ptr %calloc.i, i64 32
  store i32 64, ptr %ent_cap_slot.i.i, align 4
  %malloc = tail call dereferenceable_or_null(256) ptr @malloc(i64 256)
  store ptr %malloc, ptr %ent_arch_slot.i.i, align 8
  %malloc352 = tail call dereferenceable_or_null(256) ptr @malloc(i64 256)
  store ptr %malloc352, ptr %ent_row_slot.i.i, align 8
  store i32 1, ptr %ent_count_slot.i.i, align 4
  store i32 -1, ptr %malloc, align 4
  store i32 -1, ptr %malloc352, align 4
  tail call void @world_assign_a0(ptr nonnull %calloc.i, i32 0)
  tail call void @world_add_Position(ptr nonnull %calloc.i, i32 0, float 1.000000e+01, float 2.000000e+01)
  tail call void @world_add_Velocity(ptr nonnull %calloc.i, i32 0, float 5.000000e+00, float 2.000000e+00)
  tail call void @world_add_PlayerTag(ptr nonnull %calloc.i, i32 0, i32 1)
  %cur_ent_count.i.i92 = load i32, ptr %ent_count_slot.i.i, align 4
  %cur_ent_cap.i.i93 = load i32, ptr %ent_cap_slot.i.i, align 4
  %need_grow_ent.not.i.i94 = icmp slt i32 %cur_ent_count.i.i92, %cur_ent_cap.i.i93
  %cur_arch_arr_alloc.i.i106.pre = load ptr, ptr %ent_arch_slot.i.i, align 8
  %cur_row_arr_alloc.i.i108.pre = load ptr, ptr %ent_row_slot.i.i, align 8
  br i1 %need_grow_ent.not.i.i94, label %world_spawn.exit110, label %grow_ent.i.i95

grow_ent.i.i95:                                   ; preds = %world_spawn.exit
  %ent_cap_zero.i.i96 = icmp eq i32 %cur_ent_cap.i.i93, 0
  %double_ent_cap.i.i97 = shl i32 %cur_ent_cap.i.i93, 1
  %new_ent_cap.i.i98 = select i1 %ent_cap_zero.i.i96, i32 64, i32 %double_ent_cap.i.i97
  store i32 %new_ent_cap.i.i98, ptr %ent_cap_slot.i.i, align 4
  %new_ent_cap64.i.i99 = zext i32 %new_ent_cap.i.i98 to i64
  %bytes_for_ent.i.i100 = shl nuw nsw i64 %new_ent_cap64.i.i99, 2
  %new_arch_i8.i.i102 = tail call ptr @realloc(ptr %cur_arch_arr_alloc.i.i106.pre, i64 %bytes_for_ent.i.i100)
  store ptr %new_arch_i8.i.i102, ptr %ent_arch_slot.i.i, align 8
  %new_row_i8.i.i104 = tail call ptr @realloc(ptr %cur_row_arr_alloc.i.i108.pre, i64 %bytes_for_ent.i.i100)
  store ptr %new_row_i8.i.i104, ptr %ent_row_slot.i.i, align 8
  br label %world_spawn.exit110

world_spawn.exit110:                              ; preds = %world_spawn.exit, %grow_ent.i.i95
  %cur_row_arr_alloc.i.i108 = phi ptr [ %cur_row_arr_alloc.i.i108.pre, %world_spawn.exit ], [ %new_row_i8.i.i104, %grow_ent.i.i95 ]
  %cur_arch_arr_alloc.i.i106 = phi ptr [ %cur_arch_arr_alloc.i.i106.pre, %world_spawn.exit ], [ %new_arch_i8.i.i102, %grow_ent.i.i95 ]
  %next_ent_cnt.i.i105 = add i32 %cur_ent_count.i.i92, 1
  store i32 %next_ent_cnt.i.i105, ptr %ent_count_slot.i.i, align 4
  %0 = sext i32 %cur_ent_count.i.i92 to i64
  %e_arch_slot_alloc.i.i107 = getelementptr inbounds i32, ptr %cur_arch_arr_alloc.i.i106, i64 %0
  store i32 -1, ptr %e_arch_slot_alloc.i.i107, align 4
  %e_row_slot_alloc.i.i109 = getelementptr inbounds i32, ptr %cur_row_arr_alloc.i.i108, i64 %0
  store i32 -1, ptr %e_row_slot_alloc.i.i109, align 4
  tail call void @world_assign_a0(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i92)
  tail call void @world_add_Position(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i92, float 1.000000e+02, float 1.000000e+02)
  tail call void @world_add_Obstacle(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i92, i1 true)
  %cur_ent_count.i.i115 = load i32, ptr %ent_count_slot.i.i, align 4
  %cur_ent_cap.i.i116 = load i32, ptr %ent_cap_slot.i.i, align 4
  %need_grow_ent.not.i.i117 = icmp slt i32 %cur_ent_count.i.i115, %cur_ent_cap.i.i116
  %cur_arch_arr_alloc.i.i129.pre = load ptr, ptr %ent_arch_slot.i.i, align 8
  %cur_row_arr_alloc.i.i131.pre = load ptr, ptr %ent_row_slot.i.i, align 8
  br i1 %need_grow_ent.not.i.i117, label %world_spawn.exit133, label %grow_ent.i.i118

grow_ent.i.i118:                                  ; preds = %world_spawn.exit110
  %ent_cap_zero.i.i119 = icmp eq i32 %cur_ent_cap.i.i116, 0
  %double_ent_cap.i.i120 = shl i32 %cur_ent_cap.i.i116, 1
  %new_ent_cap.i.i121 = select i1 %ent_cap_zero.i.i119, i32 64, i32 %double_ent_cap.i.i120
  store i32 %new_ent_cap.i.i121, ptr %ent_cap_slot.i.i, align 4
  %new_ent_cap64.i.i122 = zext i32 %new_ent_cap.i.i121 to i64
  %bytes_for_ent.i.i123 = shl nuw nsw i64 %new_ent_cap64.i.i122, 2
  %new_arch_i8.i.i125 = tail call ptr @realloc(ptr %cur_arch_arr_alloc.i.i129.pre, i64 %bytes_for_ent.i.i123)
  store ptr %new_arch_i8.i.i125, ptr %ent_arch_slot.i.i, align 8
  %new_row_i8.i.i127 = tail call ptr @realloc(ptr %cur_row_arr_alloc.i.i131.pre, i64 %bytes_for_ent.i.i123)
  store ptr %new_row_i8.i.i127, ptr %ent_row_slot.i.i, align 8
  br label %world_spawn.exit133

world_spawn.exit133:                              ; preds = %world_spawn.exit110, %grow_ent.i.i118
  %cur_row_arr_alloc.i.i131 = phi ptr [ %cur_row_arr_alloc.i.i131.pre, %world_spawn.exit110 ], [ %new_row_i8.i.i127, %grow_ent.i.i118 ]
  %cur_arch_arr_alloc.i.i129 = phi ptr [ %cur_arch_arr_alloc.i.i129.pre, %world_spawn.exit110 ], [ %new_arch_i8.i.i125, %grow_ent.i.i118 ]
  %next_ent_cnt.i.i128 = add i32 %cur_ent_count.i.i115, 1
  store i32 %next_ent_cnt.i.i128, ptr %ent_count_slot.i.i, align 4
  %1 = sext i32 %cur_ent_count.i.i115 to i64
  %e_arch_slot_alloc.i.i130 = getelementptr inbounds i32, ptr %cur_arch_arr_alloc.i.i129, i64 %1
  store i32 -1, ptr %e_arch_slot_alloc.i.i130, align 4
  %e_row_slot_alloc.i.i132 = getelementptr inbounds i32, ptr %cur_row_arr_alloc.i.i131, i64 %1
  store i32 -1, ptr %e_row_slot_alloc.i.i132, align 4
  tail call void @world_assign_a0(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i115)
  tail call void @world_add_Position(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i115, float 5.000000e+01, float 5.000000e+01)
  tail call void @world_add_Velocity(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i115, float 1.000000e+01, float 0.000000e+00)
  %puts_call23 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.8)
  %arch_arr_has.i = load ptr, ptr %ent_arch_slot.i.i, align 8
  %cur_arch_idx_has.i = load i32, ptr %arch_arr_has.i, align 4
  %is_alive_has.i = icmp sgt i32 %cur_arch_idx_has.i, -1
  br i1 %is_alive_has.i, label %check_mask.i, label %world_has_Velocity.exit

check_mask.i:                                     ; preds = %world_spawn.exit133
  %tables_has.i = load ptr, ptr %arch_tables_slot.i.i, align 8
  %2 = zext nneg i32 %cur_arch_idx_has.i to i64
  %arch_ptr_has.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has.i, i64 %2
  %arch_mask_has.i = load i64, ptr %arch_ptr_has.i, align 8
  %3 = trunc i64 %arch_mask_has.i to i32
  %4 = lshr i32 %3, 2
  %5 = and i32 %4, 1
  br label %world_has_Velocity.exit

world_has_Velocity.exit:                          ; preds = %world_spawn.exit133, %check_mask.i
  %common.ret.op.i = phi i32 [ %5, %check_mask.i ], [ 0, %world_spawn.exit133 ]
  %puts_call26 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.9)
  %printf_call = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_b.22, i32 %common.ret.op.i)
  %has_arch_slot.i136 = getelementptr inbounds i32, ptr %arch_arr_has.i, i64 %0
  %cur_arch_idx_has.i137 = load i32, ptr %has_arch_slot.i136, align 4
  %is_alive_has.i138 = icmp sgt i32 %cur_arch_idx_has.i137, -1
  br i1 %is_alive_has.i138, label %check_mask.i140, label %world_has_Velocity.exit147

check_mask.i140:                                  ; preds = %world_has_Velocity.exit
  %tables_has.i142 = load ptr, ptr %arch_tables_slot.i.i, align 8
  %6 = zext nneg i32 %cur_arch_idx_has.i137 to i64
  %arch_ptr_has.i143 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has.i142, i64 %6
  %arch_mask_has.i144 = load i64, ptr %arch_ptr_has.i143, align 8
  %7 = trunc i64 %arch_mask_has.i144 to i32
  %8 = lshr i32 %7, 2
  %9 = and i32 %8, 1
  br label %world_has_Velocity.exit147

world_has_Velocity.exit147:                       ; preds = %world_has_Velocity.exit, %check_mask.i140
  %common.ret.op.i139 = phi i32 [ %9, %check_mask.i140 ], [ 0, %world_has_Velocity.exit ]
  %puts_call31 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.10)
  %printf_call34 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_b.22, i32 %common.ret.op.i139)
  %cur_arch_idx_has.i151 = load i32, ptr %has_arch_slot.i136, align 4
  %is_alive_has.i152 = icmp sgt i32 %cur_arch_idx_has.i151, -1
  br i1 %is_alive_has.i152, label %check_mask.i154, label %world_has_Obstacle.exit

check_mask.i154:                                  ; preds = %world_has_Velocity.exit147
  %tables_has.i156 = load ptr, ptr %arch_tables_slot.i.i, align 8
  %10 = zext nneg i32 %cur_arch_idx_has.i151 to i64
  %arch_ptr_has.i157 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has.i156, i64 %10
  %arch_mask_has.i158 = load i64, ptr %arch_ptr_has.i157, align 8
  %11 = trunc i64 %arch_mask_has.i158 to i32
  %12 = lshr i32 %11, 4
  %13 = and i32 %12, 1
  br label %world_has_Obstacle.exit

world_has_Obstacle.exit:                          ; preds = %world_has_Velocity.exit147, %check_mask.i154
  %common.ret.op.i153 = phi i32 [ %13, %check_mask.i154 ], [ 0, %world_has_Velocity.exit147 ]
  %puts_call37 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.12)
  %printf_call40 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_b.22, i32 %common.ret.op.i153)
  %puts_call41 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.14)
  %num_archs.i.i = load i32, ptr %calloc.i, align 4
  %has_more_archs7.i.i = icmp sgt i32 %num_archs.i.i, 0
  br i1 %has_more_archs7.i.i, label %arch_body.lr.ph.i.i, label %pipeline_PhysicsLoop.exit

arch_body.lr.ph.i.i:                              ; preds = %world_has_Obstacle.exit
  %wide.trip.count13.i.i = zext nneg i32 %num_archs.i.i to i64
  %tables_base.i.i = load ptr, ptr %arch_tables_slot.i.i, align 8
  br label %arch_body.i.i

arch_body.i.i:                                    ; preds = %next_arch.i.i, %arch_body.lr.ph.i.i
  %indvars.iv10.i.i = phi i64 [ 0, %arch_body.lr.ph.i.i ], [ %indvars.iv.next11.i.i, %next_arch.i.i ]
  %cur_arch_ptr.i.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i.i, i64 %indvars.iv10.i.i
  %arch_mask.i.i = load i64, ptr %cur_arch_ptr.i.i, align 8
  %and_mask.i.i = and i64 %arch_mask.i.i, 6
  %is_match.i.i = icmp eq i64 %and_mask.i.i, 6
  br i1 %is_match.i.i, label %check_count.i.i, label %next_arch.i.i

next_arch.i.i:                                    ; preds = %ent_loop_body.i.i, %check_count.i.i, %arch_body.i.i
  %indvars.iv.next11.i.i = add nuw nsw i64 %indvars.iv10.i.i, 1
  %exitcond14.not.i.i = icmp eq i64 %indvars.iv.next11.i.i, %wide.trip.count13.i.i
  br i1 %exitcond14.not.i.i, label %pipeline_PhysicsLoop.exit, label %arch_body.i.i

check_count.i.i:                                  ; preds = %arch_body.i.i
  %cnt_slot.i.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i, i64 8
  %arch_count.i.i = load i32, ptr %cnt_slot.i.i, align 4
  %has_entities.i.i = icmp sgt i32 %arch_count.i.i, 0
  br i1 %has_entities.i.i, label %ent_loop_cond.preheader.i.i, label %next_arch.i.i

ent_loop_cond.preheader.i.i:                      ; preds = %check_count.i.i
  %pos_col_slot.i.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i, i64 32
  %vel_col_slot.i.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i, i64 40
  %wide.trip.count.i.i = zext nneg i32 %arch_count.i.i to i64
  %dt_val.i.i = load float, ptr %res_Time_slot.i, align 4
  br label %ent_loop_body.i.i

ent_loop_body.i.i:                                ; preds = %ent_loop_body.i.i, %ent_loop_cond.preheader.i.i
  %indvars.iv.i.i = phi i64 [ 0, %ent_loop_cond.preheader.i.i ], [ %indvars.iv.next.i.i, %ent_loop_body.i.i ]
  %pos_raw.i.i = load ptr, ptr %pos_col_slot.i.i, align 8
  %pos_elem.i.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i.i, i64 %indvars.iv.i.i
  %vel_raw.i.i = load ptr, ptr %vel_col_slot.i.i, align 8
  %vel_elem.i.i = getelementptr inbounds nuw %struct.Velocity, ptr %vel_raw.i.i, i64 %indvars.iv.i.i
  %vx_val.i.i = load float, ptr %vel_elem.i.i, align 4
  %fmul.i.i = fmul float %vx_val.i.i, %dt_val.i.i
  %cur_val.i.i = load float, ptr %pos_elem.i.i, align 4
  %fadd.i.i = fadd float %cur_val.i.i, %fmul.i.i
  store float %fadd.i.i, ptr %pos_elem.i.i, align 4
  %vel_vy.i.i = getelementptr inbounds nuw i8, ptr %vel_elem.i.i, i64 4
  %vy_val.i.i = load float, ptr %vel_vy.i.i, align 4
  %fmul3.i.i = fmul float %dt_val.i.i, %vy_val.i.i
  %pos_y_gep.i.i = getelementptr inbounds nuw i8, ptr %pos_elem.i.i, i64 4
  %cur_val4.i.i = load float, ptr %pos_y_gep.i.i, align 4
  %fadd5.i.i = fadd float %cur_val4.i.i, %fmul3.i.i
  store float %fadd5.i.i, ptr %pos_y_gep.i.i, align 4
  %indvars.iv.next.i.i = add nuw nsw i64 %indvars.iv.i.i, 1
  %exitcond.not.i.i = icmp eq i64 %indvars.iv.next.i.i, %wide.trip.count.i.i
  br i1 %exitcond.not.i.i, label %next_arch.i.i, label %ent_loop_body.i.i

pipeline_PhysicsLoop.exit:                        ; preds = %next_arch.i.i, %world_has_Obstacle.exit
  tail call void @world_apply_commands(ptr nonnull %calloc.i)
  %puts_call43 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.15)
  %num_archs.i = load i32, ptr %calloc.i, align 4
  %has_more_archs4.i = icmp sgt i32 %num_archs.i, 0
  br i1 %has_more_archs4.i, label %arch_body.lr.ph.i, label %system_PrintPlayerSystem.exit.thread

system_PrintPlayerSystem.exit.thread:             ; preds = %pipeline_PhysicsLoop.exit
  %puts_call45344 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.16)
  br label %system_PrintObstacleSystem.exit

arch_body.lr.ph.i:                                ; preds = %pipeline_PhysicsLoop.exit
  %wide.trip.count10.i = zext nneg i32 %num_archs.i to i64
  %tables_base.i = load ptr, ptr %arch_tables_slot.i.i, align 8
  br label %arch_body.i

arch_body.i:                                      ; preds = %next_arch.i, %arch_body.lr.ph.i
  %indvars.iv7.i = phi i64 [ 0, %arch_body.lr.ph.i ], [ %indvars.iv.next8.i, %next_arch.i ]
  %cur_arch_ptr.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv7.i
  %arch_mask.i = load i64, ptr %cur_arch_ptr.i, align 8
  %and_mask.i = and i64 %arch_mask.i, 10
  %is_match.i = icmp eq i64 %and_mask.i, 10
  br i1 %is_match.i, label %check_count.i, label %next_arch.i

next_arch.i:                                      ; preds = %ent_loop_body.i, %check_count.i, %arch_body.i
  %indvars.iv.next8.i = add nuw nsw i64 %indvars.iv7.i, 1
  %exitcond11.not.i = icmp eq i64 %indvars.iv.next8.i, %wide.trip.count10.i
  br i1 %exitcond11.not.i, label %system_PrintPlayerSystem.exit, label %arch_body.i

check_count.i:                                    ; preds = %arch_body.i
  %cnt_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 8
  %arch_count.i = load i32, ptr %cnt_slot.i, align 4
  %has_entities.i = icmp sgt i32 %arch_count.i, 0
  br i1 %has_entities.i, label %ent_loop_cond.preheader.i, label %next_arch.i

ent_loop_cond.preheader.i:                        ; preds = %check_count.i
  %pos_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 32
  %wide.trip.count.i = zext nneg i32 %arch_count.i to i64
  br label %ent_loop_body.i

ent_loop_body.i:                                  ; preds = %ent_loop_body.i, %ent_loop_cond.preheader.i
  %indvars.iv.i = phi i64 [ 0, %ent_loop_cond.preheader.i ], [ %indvars.iv.next.i, %ent_loop_body.i ]
  %pos_raw.i = load ptr, ptr %pos_col_slot.i, align 8
  %pos_elem.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i, i64 %indvars.iv.i
  %puts_call.i = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit)
  %x_val.i = load float, ptr %pos_elem.i, align 4
  %f_to_d.i = fpext float %x_val.i to double
  %printf_call.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i)
  %pos_y.i = getelementptr inbounds nuw i8, ptr %pos_elem.i, i64 4
  %y_val.i = load float, ptr %pos_y.i, align 4
  %f_to_d1.i = fpext float %y_val.i to double
  %printf_call2.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i)
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %next_arch.i, label %ent_loop_body.i

system_PrintPlayerSystem.exit:                    ; preds = %next_arch.i
  %puts_call45 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.16)
  br label %arch_body.i166

arch_body.i166:                                   ; preds = %next_arch.i173, %system_PrintPlayerSystem.exit
  %indvars.iv7.i167 = phi i64 [ 0, %system_PrintPlayerSystem.exit ], [ %indvars.iv.next8.i174, %next_arch.i173 ]
  %cur_arch_ptr.i169 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv7.i167
  %arch_mask.i170 = load i64, ptr %cur_arch_ptr.i169, align 8
  %and_mask.i171 = and i64 %arch_mask.i170, 18
  %is_match.i172 = icmp eq i64 %and_mask.i171, 18
  br i1 %is_match.i172, label %check_count.i176, label %next_arch.i173

next_arch.i173:                                   ; preds = %ent_loop_body.i183, %check_count.i176, %arch_body.i166
  %indvars.iv.next8.i174 = add nuw nsw i64 %indvars.iv7.i167, 1
  %exitcond11.not.i175 = icmp eq i64 %indvars.iv.next8.i174, %wide.trip.count10.i
  br i1 %exitcond11.not.i175, label %system_PrintObstacleSystem.exit, label %arch_body.i166

check_count.i176:                                 ; preds = %arch_body.i166
  %cnt_slot.i177 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i169, i64 8
  %arch_count.i178 = load i32, ptr %cnt_slot.i177, align 4
  %has_entities.i179 = icmp sgt i32 %arch_count.i178, 0
  br i1 %has_entities.i179, label %ent_loop_cond.preheader.i180, label %next_arch.i173

ent_loop_cond.preheader.i180:                     ; preds = %check_count.i176
  %pos_col_slot.i181 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i169, i64 32
  %wide.trip.count.i182 = zext nneg i32 %arch_count.i178 to i64
  br label %ent_loop_body.i183

ent_loop_body.i183:                               ; preds = %ent_loop_body.i183, %ent_loop_cond.preheader.i180
  %indvars.iv.i184 = phi i64 [ 0, %ent_loop_cond.preheader.i180 ], [ %indvars.iv.next.i195, %ent_loop_body.i183 ]
  %pos_raw.i185 = load ptr, ptr %pos_col_slot.i181, align 8
  %pos_elem.i186 = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i185, i64 %indvars.iv.i184
  %puts_call.i187 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.2)
  %x_val.i188 = load float, ptr %pos_elem.i186, align 4
  %f_to_d.i189 = fpext float %x_val.i188 to double
  %printf_call.i190 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i189)
  %pos_y.i191 = getelementptr inbounds nuw i8, ptr %pos_elem.i186, i64 4
  %y_val.i192 = load float, ptr %pos_y.i191, align 4
  %f_to_d1.i193 = fpext float %y_val.i192 to double
  %printf_call2.i194 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i193)
  %indvars.iv.next.i195 = add nuw nsw i64 %indvars.iv.i184, 1
  %exitcond.not.i196 = icmp eq i64 %indvars.iv.next.i195, %wide.trip.count.i182
  br i1 %exitcond.not.i196, label %next_arch.i173, label %ent_loop_body.i183

system_PrintObstacleSystem.exit:                  ; preds = %next_arch.i173, %system_PrintPlayerSystem.exit.thread
  %puts_call47 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.17)
  tail call void @world_remove_Velocity(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i115)
  %arch_arr_has.i198 = load ptr, ptr %ent_arch_slot.i.i, align 8
  %has_arch_slot.i199 = getelementptr inbounds i32, ptr %arch_arr_has.i198, i64 %1
  %cur_arch_idx_has.i200 = load i32, ptr %has_arch_slot.i199, align 4
  %is_alive_has.i201 = icmp sgt i32 %cur_arch_idx_has.i200, -1
  br i1 %is_alive_has.i201, label %check_mask.i203, label %world_has_Velocity.exit210

check_mask.i203:                                  ; preds = %system_PrintObstacleSystem.exit
  %tables_has.i205 = load ptr, ptr %arch_tables_slot.i.i, align 8
  %14 = zext nneg i32 %cur_arch_idx_has.i200 to i64
  %arch_ptr_has.i206 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has.i205, i64 %14
  %arch_mask_has.i207 = load i64, ptr %arch_ptr_has.i206, align 8
  %15 = trunc i64 %arch_mask_has.i207 to i32
  %16 = lshr i32 %15, 2
  %17 = and i32 %16, 1
  br label %world_has_Velocity.exit210

world_has_Velocity.exit210:                       ; preds = %system_PrintObstacleSystem.exit, %check_mask.i203
  %common.ret.op.i202 = phi i32 [ %17, %check_mask.i203 ], [ 0, %system_PrintObstacleSystem.exit ]
  %puts_call53 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.18)
  %printf_call56 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_b.22, i32 %common.ret.op.i202)
  %puts_call57 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.20)
  tail call void @world_add_Velocity(ptr nonnull %calloc.i, i32 %cur_ent_count.i.i92, float 1.000000e+00, float 2.000000e+00)
  %arch_arr_has.i212 = load ptr, ptr %ent_arch_slot.i.i, align 8
  %has_arch_slot.i213 = getelementptr inbounds i32, ptr %arch_arr_has.i212, i64 %0
  %cur_arch_idx_has.i214 = load i32, ptr %has_arch_slot.i213, align 4
  %is_alive_has.i215 = icmp sgt i32 %cur_arch_idx_has.i214, -1
  br i1 %is_alive_has.i215, label %check_mask.i217, label %world_has_Velocity.exit224

check_mask.i217:                                  ; preds = %world_has_Velocity.exit210
  %tables_has.i219 = load ptr, ptr %arch_tables_slot.i.i, align 8
  %18 = zext nneg i32 %cur_arch_idx_has.i214 to i64
  %arch_ptr_has.i220 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_has.i219, i64 %18
  %arch_mask_has.i221 = load i64, ptr %arch_ptr_has.i220, align 8
  %19 = trunc i64 %arch_mask_has.i221 to i32
  %20 = lshr i32 %19, 2
  %21 = and i32 %20, 1
  br label %world_has_Velocity.exit224

world_has_Velocity.exit224:                       ; preds = %world_has_Velocity.exit210, %check_mask.i217
  %common.ret.op.i216 = phi i32 [ %21, %check_mask.i217 ], [ 0, %world_has_Velocity.exit210 ]
  %puts_call63 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.21)
  %printf_call66 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_b.22, i32 %common.ret.op.i216)
  %puts_call67 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.23)
  %num_archs.i.i225 = load i32, ptr %calloc.i, align 4
  %has_more_archs7.i.i226 = icmp sgt i32 %num_archs.i.i225, 0
  br i1 %has_more_archs7.i.i226, label %arch_body.lr.ph.i.i227, label %pipeline_PhysicsLoop.exit269

arch_body.lr.ph.i.i227:                           ; preds = %world_has_Velocity.exit224
  %wide.trip.count13.i.i230 = zext nneg i32 %num_archs.i.i225 to i64
  %tables_base.i.i233 = load ptr, ptr %arch_tables_slot.i.i, align 8
  br label %arch_body.i.i231

arch_body.i.i231:                                 ; preds = %next_arch.i.i238, %arch_body.lr.ph.i.i227
  %indvars.iv10.i.i232 = phi i64 [ 0, %arch_body.lr.ph.i.i227 ], [ %indvars.iv.next11.i.i239, %next_arch.i.i238 ]
  %cur_arch_ptr.i.i234 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i.i233, i64 %indvars.iv10.i.i232
  %arch_mask.i.i235 = load i64, ptr %cur_arch_ptr.i.i234, align 8
  %and_mask.i.i236 = and i64 %arch_mask.i.i235, 6
  %is_match.i.i237 = icmp eq i64 %and_mask.i.i236, 6
  br i1 %is_match.i.i237, label %check_count.i.i241, label %next_arch.i.i238

next_arch.i.i238:                                 ; preds = %ent_loop_body.i.i249, %check_count.i.i241, %arch_body.i.i231
  %indvars.iv.next11.i.i239 = add nuw nsw i64 %indvars.iv10.i.i232, 1
  %exitcond14.not.i.i240 = icmp eq i64 %indvars.iv.next11.i.i239, %wide.trip.count13.i.i230
  br i1 %exitcond14.not.i.i240, label %pipeline_PhysicsLoop.exit269, label %arch_body.i.i231

check_count.i.i241:                               ; preds = %arch_body.i.i231
  %cnt_slot.i.i242 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i234, i64 8
  %arch_count.i.i243 = load i32, ptr %cnt_slot.i.i242, align 4
  %has_entities.i.i244 = icmp sgt i32 %arch_count.i.i243, 0
  br i1 %has_entities.i.i244, label %ent_loop_cond.preheader.i.i245, label %next_arch.i.i238

ent_loop_cond.preheader.i.i245:                   ; preds = %check_count.i.i241
  %pos_col_slot.i.i246 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i234, i64 32
  %vel_col_slot.i.i247 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i.i234, i64 40
  %wide.trip.count.i.i248 = zext nneg i32 %arch_count.i.i243 to i64
  %dt_val.i.i256 = load float, ptr %res_Time_slot.i, align 4
  br label %ent_loop_body.i.i249

ent_loop_body.i.i249:                             ; preds = %ent_loop_body.i.i249, %ent_loop_cond.preheader.i.i245
  %indvars.iv.i.i250 = phi i64 [ 0, %ent_loop_cond.preheader.i.i245 ], [ %indvars.iv.next.i.i267, %ent_loop_body.i.i249 ]
  %pos_raw.i.i251 = load ptr, ptr %pos_col_slot.i.i246, align 8
  %pos_elem.i.i252 = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i.i251, i64 %indvars.iv.i.i250
  %vel_raw.i.i253 = load ptr, ptr %vel_col_slot.i.i247, align 8
  %vel_elem.i.i254 = getelementptr inbounds nuw %struct.Velocity, ptr %vel_raw.i.i253, i64 %indvars.iv.i.i250
  %vx_val.i.i255 = load float, ptr %vel_elem.i.i254, align 4
  %fmul.i.i257 = fmul float %vx_val.i.i255, %dt_val.i.i256
  %cur_val.i.i258 = load float, ptr %pos_elem.i.i252, align 4
  %fadd.i.i259 = fadd float %cur_val.i.i258, %fmul.i.i257
  store float %fadd.i.i259, ptr %pos_elem.i.i252, align 4
  %vel_vy.i.i260 = getelementptr inbounds nuw i8, ptr %vel_elem.i.i254, i64 4
  %vy_val.i.i261 = load float, ptr %vel_vy.i.i260, align 4
  %fmul3.i.i263 = fmul float %dt_val.i.i256, %vy_val.i.i261
  %pos_y_gep.i.i264 = getelementptr inbounds nuw i8, ptr %pos_elem.i.i252, i64 4
  %cur_val4.i.i265 = load float, ptr %pos_y_gep.i.i264, align 4
  %fadd5.i.i266 = fadd float %cur_val4.i.i265, %fmul3.i.i263
  store float %fadd5.i.i266, ptr %pos_y_gep.i.i264, align 4
  %indvars.iv.next.i.i267 = add nuw nsw i64 %indvars.iv.i.i250, 1
  %exitcond.not.i.i268 = icmp eq i64 %indvars.iv.next.i.i267, %wide.trip.count.i.i248
  br i1 %exitcond.not.i.i268, label %next_arch.i.i238, label %ent_loop_body.i.i249

pipeline_PhysicsLoop.exit269:                     ; preds = %next_arch.i.i238, %world_has_Velocity.exit224
  tail call void @world_apply_commands(ptr nonnull %calloc.i)
  %puts_call69 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.24)
  %num_archs.i270 = load i32, ptr %calloc.i, align 4
  %has_more_archs4.i271 = icmp sgt i32 %num_archs.i270, 0
  br i1 %has_more_archs4.i271, label %arch_body.lr.ph.i272, label %system_PrintPlayerSystem.exit306.thread

system_PrintPlayerSystem.exit306.thread:          ; preds = %pipeline_PhysicsLoop.exit269
  %puts_call71345 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.25)
  br label %system_PrintObstacleSystem.exit343

arch_body.lr.ph.i272:                             ; preds = %pipeline_PhysicsLoop.exit269
  %wide.trip.count10.i274 = zext nneg i32 %num_archs.i270 to i64
  %tables_base.i277 = load ptr, ptr %arch_tables_slot.i.i, align 8
  br label %arch_body.i275

arch_body.i275:                                   ; preds = %next_arch.i282, %arch_body.lr.ph.i272
  %indvars.iv7.i276 = phi i64 [ 0, %arch_body.lr.ph.i272 ], [ %indvars.iv.next8.i283, %next_arch.i282 ]
  %cur_arch_ptr.i278 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i277, i64 %indvars.iv7.i276
  %arch_mask.i279 = load i64, ptr %cur_arch_ptr.i278, align 8
  %and_mask.i280 = and i64 %arch_mask.i279, 10
  %is_match.i281 = icmp eq i64 %and_mask.i280, 10
  br i1 %is_match.i281, label %check_count.i285, label %next_arch.i282

next_arch.i282:                                   ; preds = %ent_loop_body.i292, %check_count.i285, %arch_body.i275
  %indvars.iv.next8.i283 = add nuw nsw i64 %indvars.iv7.i276, 1
  %exitcond11.not.i284 = icmp eq i64 %indvars.iv.next8.i283, %wide.trip.count10.i274
  br i1 %exitcond11.not.i284, label %system_PrintPlayerSystem.exit306, label %arch_body.i275

check_count.i285:                                 ; preds = %arch_body.i275
  %cnt_slot.i286 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i278, i64 8
  %arch_count.i287 = load i32, ptr %cnt_slot.i286, align 4
  %has_entities.i288 = icmp sgt i32 %arch_count.i287, 0
  br i1 %has_entities.i288, label %ent_loop_cond.preheader.i289, label %next_arch.i282

ent_loop_cond.preheader.i289:                     ; preds = %check_count.i285
  %pos_col_slot.i290 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i278, i64 32
  %wide.trip.count.i291 = zext nneg i32 %arch_count.i287 to i64
  br label %ent_loop_body.i292

ent_loop_body.i292:                               ; preds = %ent_loop_body.i292, %ent_loop_cond.preheader.i289
  %indvars.iv.i293 = phi i64 [ 0, %ent_loop_cond.preheader.i289 ], [ %indvars.iv.next.i304, %ent_loop_body.i292 ]
  %pos_raw.i294 = load ptr, ptr %pos_col_slot.i290, align 8
  %pos_elem.i295 = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i294, i64 %indvars.iv.i293
  %puts_call.i296 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit)
  %x_val.i297 = load float, ptr %pos_elem.i295, align 4
  %f_to_d.i298 = fpext float %x_val.i297 to double
  %printf_call.i299 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i298)
  %pos_y.i300 = getelementptr inbounds nuw i8, ptr %pos_elem.i295, i64 4
  %y_val.i301 = load float, ptr %pos_y.i300, align 4
  %f_to_d1.i302 = fpext float %y_val.i301 to double
  %printf_call2.i303 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i302)
  %indvars.iv.next.i304 = add nuw nsw i64 %indvars.iv.i293, 1
  %exitcond.not.i305 = icmp eq i64 %indvars.iv.next.i304, %wide.trip.count.i291
  br i1 %exitcond.not.i305, label %next_arch.i282, label %ent_loop_body.i292

system_PrintPlayerSystem.exit306:                 ; preds = %next_arch.i282
  %puts_call71 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.25)
  br label %arch_body.i312

arch_body.i312:                                   ; preds = %next_arch.i319, %system_PrintPlayerSystem.exit306
  %indvars.iv7.i313 = phi i64 [ 0, %system_PrintPlayerSystem.exit306 ], [ %indvars.iv.next8.i320, %next_arch.i319 ]
  %cur_arch_ptr.i315 = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i277, i64 %indvars.iv7.i313
  %arch_mask.i316 = load i64, ptr %cur_arch_ptr.i315, align 8
  %and_mask.i317 = and i64 %arch_mask.i316, 18
  %is_match.i318 = icmp eq i64 %and_mask.i317, 18
  br i1 %is_match.i318, label %check_count.i322, label %next_arch.i319

next_arch.i319:                                   ; preds = %ent_loop_body.i329, %check_count.i322, %arch_body.i312
  %indvars.iv.next8.i320 = add nuw nsw i64 %indvars.iv7.i313, 1
  %exitcond11.not.i321 = icmp eq i64 %indvars.iv.next8.i320, %wide.trip.count10.i274
  br i1 %exitcond11.not.i321, label %system_PrintObstacleSystem.exit343, label %arch_body.i312

check_count.i322:                                 ; preds = %arch_body.i312
  %cnt_slot.i323 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i315, i64 8
  %arch_count.i324 = load i32, ptr %cnt_slot.i323, align 4
  %has_entities.i325 = icmp sgt i32 %arch_count.i324, 0
  br i1 %has_entities.i325, label %ent_loop_cond.preheader.i326, label %next_arch.i319

ent_loop_cond.preheader.i326:                     ; preds = %check_count.i322
  %pos_col_slot.i327 = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i315, i64 32
  %wide.trip.count.i328 = zext nneg i32 %arch_count.i324 to i64
  br label %ent_loop_body.i329

ent_loop_body.i329:                               ; preds = %ent_loop_body.i329, %ent_loop_cond.preheader.i326
  %indvars.iv.i330 = phi i64 [ 0, %ent_loop_cond.preheader.i326 ], [ %indvars.iv.next.i341, %ent_loop_body.i329 ]
  %pos_raw.i331 = load ptr, ptr %pos_col_slot.i327, align 8
  %pos_elem.i332 = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i331, i64 %indvars.iv.i330
  %puts_call.i333 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.2)
  %x_val.i334 = load float, ptr %pos_elem.i332, align 4
  %f_to_d.i335 = fpext float %x_val.i334 to double
  %printf_call.i336 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i335)
  %pos_y.i337 = getelementptr inbounds nuw i8, ptr %pos_elem.i332, i64 4
  %y_val.i338 = load float, ptr %pos_y.i337, align 4
  %f_to_d1.i339 = fpext float %y_val.i338 to double
  %printf_call2.i340 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i339)
  %indvars.iv.next.i341 = add nuw nsw i64 %indvars.iv.i330, 1
  %exitcond.not.i342 = icmp eq i64 %indvars.iv.next.i341, %wide.trip.count.i328
  br i1 %exitcond.not.i342, label %next_arch.i319, label %ent_loop_body.i329

system_PrintObstacleSystem.exit343:               ; preds = %next_arch.i319, %system_PrintPlayerSystem.exit306.thread
  %puts_call73 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.28)
  %puts_call74 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.27)
  %puts_call75 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.28)
  %puts_call76 = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.29)
  %key_input = tail call i32 @getchar()
  ret i32 0
}

; Function Attrs: nofree norecurse nosync nounwind memory(readwrite, inaccessiblemem: none)
define void @system_MovementSystem(ptr nocapture readonly %world) local_unnamed_addr #11 {
entry:
  %num_archs = load i32, ptr %world, align 4
  %has_more_archs7 = icmp sgt i32 %num_archs, 0
  br i1 %has_more_archs7, label %arch_body.lr.ph, label %sys_exit

arch_body.lr.ph:                                  ; preds = %entry
  %world_arch_tables_slot = getelementptr inbounds nuw i8, ptr %world, i64 8
  %time_res_slot = getelementptr inbounds nuw i8, ptr %world, i64 64
  %wide.trip.count13 = zext nneg i32 %num_archs to i64
  br label %arch_body

arch_body:                                        ; preds = %arch_body.lr.ph, %next_arch
  %indvars.iv10 = phi i64 [ 0, %arch_body.lr.ph ], [ %indvars.iv.next11, %next_arch ]
  %tables_base = load ptr, ptr %world_arch_tables_slot, align 8
  %cur_arch_ptr = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base, i64 %indvars.iv10
  %arch_mask = load i64, ptr %cur_arch_ptr, align 8
  %and_mask = and i64 %arch_mask, 6
  %is_match = icmp eq i64 %and_mask, 6
  br i1 %is_match, label %check_count, label %next_arch

next_arch:                                        ; preds = %ent_loop_body, %check_count, %arch_body
  %indvars.iv.next11 = add nuw nsw i64 %indvars.iv10, 1
  %exitcond14.not = icmp eq i64 %indvars.iv.next11, %wide.trip.count13
  br i1 %exitcond14.not, label %sys_exit, label %arch_body

sys_exit:                                         ; preds = %next_arch, %entry
  ret void

check_count:                                      ; preds = %arch_body
  %cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 8
  %arch_count = load i32, ptr %cnt_slot, align 4
  %has_entities = icmp sgt i32 %arch_count, 0
  br i1 %has_entities, label %ent_loop_cond.preheader, label %next_arch

ent_loop_cond.preheader:                          ; preds = %check_count
  %pos_col_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 32
  %vel_col_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 40
  %wide.trip.count = zext nneg i32 %arch_count to i64
  br label %ent_loop_body

ent_loop_body:                                    ; preds = %ent_loop_cond.preheader, %ent_loop_body
  %indvars.iv = phi i64 [ 0, %ent_loop_cond.preheader ], [ %indvars.iv.next, %ent_loop_body ]
  %pos_raw = load ptr, ptr %pos_col_slot, align 8
  %pos_elem = getelementptr inbounds nuw %struct.Position, ptr %pos_raw, i64 %indvars.iv
  %vel_raw = load ptr, ptr %vel_col_slot, align 8
  %vel_elem = getelementptr inbounds nuw %struct.Velocity, ptr %vel_raw, i64 %indvars.iv
  %vx_val = load float, ptr %vel_elem, align 4
  %dt_val = load float, ptr %time_res_slot, align 4
  %fmul = fmul float %vx_val, %dt_val
  %cur_val = load float, ptr %pos_elem, align 4
  %fadd = fadd float %cur_val, %fmul
  store float %fadd, ptr %pos_elem, align 4
  %vel_vy = getelementptr inbounds nuw i8, ptr %vel_elem, i64 4
  %vy_val = load float, ptr %vel_vy, align 4
  %dt_val2 = load float, ptr %time_res_slot, align 4
  %fmul3 = fmul float %vy_val, %dt_val2
  %pos_y_gep = getelementptr inbounds nuw i8, ptr %pos_elem, i64 4
  %cur_val4 = load float, ptr %pos_y_gep, align 4
  %fadd5 = fadd float %cur_val4, %fmul3
  store float %fadd5, ptr %pos_y_gep, align 4
  %indvars.iv.next = add nuw nsw i64 %indvars.iv, 1
  %exitcond.not = icmp eq i64 %indvars.iv.next, %wide.trip.count
  br i1 %exitcond.not, label %next_arch, label %ent_loop_body
}

; Function Attrs: nofree norecurse nosync nounwind memory(readwrite, inaccessiblemem: none)
define void @job_MovementSystem(ptr nocapture readnone %0, ptr nocapture readonly %1, ptr nocapture readnone %2) local_unnamed_addr #11 {
entry:
  %num_archs.i = load i32, ptr %1, align 4
  %has_more_archs7.i = icmp sgt i32 %num_archs.i, 0
  br i1 %has_more_archs7.i, label %arch_body.lr.ph.i, label %system_MovementSystem.exit

arch_body.lr.ph.i:                                ; preds = %entry
  %world_arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %1, i64 8
  %time_res_slot.i = getelementptr inbounds nuw i8, ptr %1, i64 64
  %wide.trip.count13.i = zext nneg i32 %num_archs.i to i64
  br label %arch_body.i

arch_body.i:                                      ; preds = %next_arch.i, %arch_body.lr.ph.i
  %indvars.iv10.i = phi i64 [ 0, %arch_body.lr.ph.i ], [ %indvars.iv.next11.i, %next_arch.i ]
  %tables_base.i = load ptr, ptr %world_arch_tables_slot.i, align 8
  %cur_arch_ptr.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv10.i
  %arch_mask.i = load i64, ptr %cur_arch_ptr.i, align 8
  %and_mask.i = and i64 %arch_mask.i, 6
  %is_match.i = icmp eq i64 %and_mask.i, 6
  br i1 %is_match.i, label %check_count.i, label %next_arch.i

next_arch.i:                                      ; preds = %ent_loop_body.i, %check_count.i, %arch_body.i
  %indvars.iv.next11.i = add nuw nsw i64 %indvars.iv10.i, 1
  %exitcond14.not.i = icmp eq i64 %indvars.iv.next11.i, %wide.trip.count13.i
  br i1 %exitcond14.not.i, label %system_MovementSystem.exit, label %arch_body.i

check_count.i:                                    ; preds = %arch_body.i
  %cnt_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 8
  %arch_count.i = load i32, ptr %cnt_slot.i, align 4
  %has_entities.i = icmp sgt i32 %arch_count.i, 0
  br i1 %has_entities.i, label %ent_loop_cond.preheader.i, label %next_arch.i

ent_loop_cond.preheader.i:                        ; preds = %check_count.i
  %pos_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 32
  %vel_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 40
  %wide.trip.count.i = zext nneg i32 %arch_count.i to i64
  br label %ent_loop_body.i

ent_loop_body.i:                                  ; preds = %ent_loop_body.i, %ent_loop_cond.preheader.i
  %indvars.iv.i = phi i64 [ 0, %ent_loop_cond.preheader.i ], [ %indvars.iv.next.i, %ent_loop_body.i ]
  %pos_raw.i = load ptr, ptr %pos_col_slot.i, align 8
  %pos_elem.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i, i64 %indvars.iv.i
  %vel_raw.i = load ptr, ptr %vel_col_slot.i, align 8
  %vel_elem.i = getelementptr inbounds nuw %struct.Velocity, ptr %vel_raw.i, i64 %indvars.iv.i
  %vx_val.i = load float, ptr %vel_elem.i, align 4
  %dt_val.i = load float, ptr %time_res_slot.i, align 4
  %fmul.i = fmul float %vx_val.i, %dt_val.i
  %cur_val.i = load float, ptr %pos_elem.i, align 4
  %fadd.i = fadd float %cur_val.i, %fmul.i
  store float %fadd.i, ptr %pos_elem.i, align 4
  %vel_vy.i = getelementptr inbounds nuw i8, ptr %vel_elem.i, i64 4
  %vy_val.i = load float, ptr %vel_vy.i, align 4
  %dt_val2.i = load float, ptr %time_res_slot.i, align 4
  %fmul3.i = fmul float %vy_val.i, %dt_val2.i
  %pos_y_gep.i = getelementptr inbounds nuw i8, ptr %pos_elem.i, i64 4
  %cur_val4.i = load float, ptr %pos_y_gep.i, align 4
  %fadd5.i = fadd float %cur_val4.i, %fmul3.i
  store float %fadd5.i, ptr %pos_y_gep.i, align 4
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %next_arch.i, label %ent_loop_body.i

system_MovementSystem.exit:                       ; preds = %next_arch.i, %entry
  ret void
}

; Function Attrs: nofree nounwind
define void @system_PrintPlayerSystem(ptr nocapture readonly %world) local_unnamed_addr #0 {
entry:
  %num_archs = load i32, ptr %world, align 4
  %has_more_archs4 = icmp sgt i32 %num_archs, 0
  br i1 %has_more_archs4, label %arch_body.lr.ph, label %sys_exit

arch_body.lr.ph:                                  ; preds = %entry
  %world_arch_tables_slot = getelementptr inbounds nuw i8, ptr %world, i64 8
  %wide.trip.count10 = zext nneg i32 %num_archs to i64
  br label %arch_body

arch_body:                                        ; preds = %arch_body.lr.ph, %next_arch
  %indvars.iv7 = phi i64 [ 0, %arch_body.lr.ph ], [ %indvars.iv.next8, %next_arch ]
  %tables_base = load ptr, ptr %world_arch_tables_slot, align 8
  %cur_arch_ptr = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base, i64 %indvars.iv7
  %arch_mask = load i64, ptr %cur_arch_ptr, align 8
  %and_mask = and i64 %arch_mask, 10
  %is_match = icmp eq i64 %and_mask, 10
  br i1 %is_match, label %check_count, label %next_arch

next_arch:                                        ; preds = %ent_loop_body, %check_count, %arch_body
  %indvars.iv.next8 = add nuw nsw i64 %indvars.iv7, 1
  %exitcond11.not = icmp eq i64 %indvars.iv.next8, %wide.trip.count10
  br i1 %exitcond11.not, label %sys_exit, label %arch_body

sys_exit:                                         ; preds = %next_arch, %entry
  ret void

check_count:                                      ; preds = %arch_body
  %cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 8
  %arch_count = load i32, ptr %cnt_slot, align 4
  %has_entities = icmp sgt i32 %arch_count, 0
  br i1 %has_entities, label %ent_loop_cond.preheader, label %next_arch

ent_loop_cond.preheader:                          ; preds = %check_count
  %pos_col_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 32
  %wide.trip.count = zext nneg i32 %arch_count to i64
  br label %ent_loop_body

ent_loop_body:                                    ; preds = %ent_loop_cond.preheader, %ent_loop_body
  %indvars.iv = phi i64 [ 0, %ent_loop_cond.preheader ], [ %indvars.iv.next, %ent_loop_body ]
  %pos_raw = load ptr, ptr %pos_col_slot, align 8
  %pos_elem = getelementptr inbounds nuw %struct.Position, ptr %pos_raw, i64 %indvars.iv
  %puts_call = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit)
  %x_val = load float, ptr %pos_elem, align 4
  %f_to_d = fpext float %x_val to double
  %printf_call = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d)
  %pos_y = getelementptr inbounds nuw i8, ptr %pos_elem, i64 4
  %y_val = load float, ptr %pos_y, align 4
  %f_to_d1 = fpext float %y_val to double
  %printf_call2 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1)
  %indvars.iv.next = add nuw nsw i64 %indvars.iv, 1
  %exitcond.not = icmp eq i64 %indvars.iv.next, %wide.trip.count
  br i1 %exitcond.not, label %next_arch, label %ent_loop_body
}

; Function Attrs: nofree nounwind
define void @job_PrintPlayerSystem(ptr nocapture readnone %0, ptr nocapture readonly %1, ptr nocapture readnone %2) local_unnamed_addr #0 {
entry:
  %num_archs.i = load i32, ptr %1, align 4
  %has_more_archs4.i = icmp sgt i32 %num_archs.i, 0
  br i1 %has_more_archs4.i, label %arch_body.lr.ph.i, label %system_PrintPlayerSystem.exit

arch_body.lr.ph.i:                                ; preds = %entry
  %world_arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %1, i64 8
  %wide.trip.count10.i = zext nneg i32 %num_archs.i to i64
  br label %arch_body.i

arch_body.i:                                      ; preds = %next_arch.i, %arch_body.lr.ph.i
  %indvars.iv7.i = phi i64 [ 0, %arch_body.lr.ph.i ], [ %indvars.iv.next8.i, %next_arch.i ]
  %tables_base.i = load ptr, ptr %world_arch_tables_slot.i, align 8
  %cur_arch_ptr.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv7.i
  %arch_mask.i = load i64, ptr %cur_arch_ptr.i, align 8
  %and_mask.i = and i64 %arch_mask.i, 10
  %is_match.i = icmp eq i64 %and_mask.i, 10
  br i1 %is_match.i, label %check_count.i, label %next_arch.i

next_arch.i:                                      ; preds = %ent_loop_body.i, %check_count.i, %arch_body.i
  %indvars.iv.next8.i = add nuw nsw i64 %indvars.iv7.i, 1
  %exitcond11.not.i = icmp eq i64 %indvars.iv.next8.i, %wide.trip.count10.i
  br i1 %exitcond11.not.i, label %system_PrintPlayerSystem.exit, label %arch_body.i

check_count.i:                                    ; preds = %arch_body.i
  %cnt_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 8
  %arch_count.i = load i32, ptr %cnt_slot.i, align 4
  %has_entities.i = icmp sgt i32 %arch_count.i, 0
  br i1 %has_entities.i, label %ent_loop_cond.preheader.i, label %next_arch.i

ent_loop_cond.preheader.i:                        ; preds = %check_count.i
  %pos_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 32
  %wide.trip.count.i = zext nneg i32 %arch_count.i to i64
  br label %ent_loop_body.i

ent_loop_body.i:                                  ; preds = %ent_loop_body.i, %ent_loop_cond.preheader.i
  %indvars.iv.i = phi i64 [ 0, %ent_loop_cond.preheader.i ], [ %indvars.iv.next.i, %ent_loop_body.i ]
  %pos_raw.i = load ptr, ptr %pos_col_slot.i, align 8
  %pos_elem.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i, i64 %indvars.iv.i
  %puts_call.i = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit)
  %x_val.i = load float, ptr %pos_elem.i, align 4
  %f_to_d.i = fpext float %x_val.i to double
  %printf_call.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i)
  %pos_y.i = getelementptr inbounds nuw i8, ptr %pos_elem.i, i64 4
  %y_val.i = load float, ptr %pos_y.i, align 4
  %f_to_d1.i = fpext float %y_val.i to double
  %printf_call2.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i)
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %next_arch.i, label %ent_loop_body.i

system_PrintPlayerSystem.exit:                    ; preds = %next_arch.i, %entry
  ret void
}

; Function Attrs: nofree nounwind
define void @system_PrintObstacleSystem(ptr nocapture readonly %world) local_unnamed_addr #0 {
entry:
  %num_archs = load i32, ptr %world, align 4
  %has_more_archs4 = icmp sgt i32 %num_archs, 0
  br i1 %has_more_archs4, label %arch_body.lr.ph, label %sys_exit

arch_body.lr.ph:                                  ; preds = %entry
  %world_arch_tables_slot = getelementptr inbounds nuw i8, ptr %world, i64 8
  %wide.trip.count10 = zext nneg i32 %num_archs to i64
  br label %arch_body

arch_body:                                        ; preds = %arch_body.lr.ph, %next_arch
  %indvars.iv7 = phi i64 [ 0, %arch_body.lr.ph ], [ %indvars.iv.next8, %next_arch ]
  %tables_base = load ptr, ptr %world_arch_tables_slot, align 8
  %cur_arch_ptr = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base, i64 %indvars.iv7
  %arch_mask = load i64, ptr %cur_arch_ptr, align 8
  %and_mask = and i64 %arch_mask, 18
  %is_match = icmp eq i64 %and_mask, 18
  br i1 %is_match, label %check_count, label %next_arch

next_arch:                                        ; preds = %ent_loop_body, %check_count, %arch_body
  %indvars.iv.next8 = add nuw nsw i64 %indvars.iv7, 1
  %exitcond11.not = icmp eq i64 %indvars.iv.next8, %wide.trip.count10
  br i1 %exitcond11.not, label %sys_exit, label %arch_body

sys_exit:                                         ; preds = %next_arch, %entry
  ret void

check_count:                                      ; preds = %arch_body
  %cnt_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 8
  %arch_count = load i32, ptr %cnt_slot, align 4
  %has_entities = icmp sgt i32 %arch_count, 0
  br i1 %has_entities, label %ent_loop_cond.preheader, label %next_arch

ent_loop_cond.preheader:                          ; preds = %check_count
  %pos_col_slot = getelementptr inbounds nuw i8, ptr %cur_arch_ptr, i64 32
  %wide.trip.count = zext nneg i32 %arch_count to i64
  br label %ent_loop_body

ent_loop_body:                                    ; preds = %ent_loop_cond.preheader, %ent_loop_body
  %indvars.iv = phi i64 [ 0, %ent_loop_cond.preheader ], [ %indvars.iv.next, %ent_loop_body ]
  %pos_raw = load ptr, ptr %pos_col_slot, align 8
  %pos_elem = getelementptr inbounds nuw %struct.Position, ptr %pos_raw, i64 %indvars.iv
  %puts_call = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.2)
  %x_val = load float, ptr %pos_elem, align 4
  %f_to_d = fpext float %x_val to double
  %printf_call = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d)
  %pos_y = getelementptr inbounds nuw i8, ptr %pos_elem, i64 4
  %y_val = load float, ptr %pos_y, align 4
  %f_to_d1 = fpext float %y_val to double
  %printf_call2 = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1)
  %indvars.iv.next = add nuw nsw i64 %indvars.iv, 1
  %exitcond.not = icmp eq i64 %indvars.iv.next, %wide.trip.count
  br i1 %exitcond.not, label %next_arch, label %ent_loop_body
}

; Function Attrs: nofree nounwind
define void @job_PrintObstacleSystem(ptr nocapture readnone %0, ptr nocapture readonly %1, ptr nocapture readnone %2) local_unnamed_addr #0 {
entry:
  %num_archs.i = load i32, ptr %1, align 4
  %has_more_archs4.i = icmp sgt i32 %num_archs.i, 0
  br i1 %has_more_archs4.i, label %arch_body.lr.ph.i, label %system_PrintObstacleSystem.exit

arch_body.lr.ph.i:                                ; preds = %entry
  %world_arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %1, i64 8
  %wide.trip.count10.i = zext nneg i32 %num_archs.i to i64
  br label %arch_body.i

arch_body.i:                                      ; preds = %next_arch.i, %arch_body.lr.ph.i
  %indvars.iv7.i = phi i64 [ 0, %arch_body.lr.ph.i ], [ %indvars.iv.next8.i, %next_arch.i ]
  %tables_base.i = load ptr, ptr %world_arch_tables_slot.i, align 8
  %cur_arch_ptr.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv7.i
  %arch_mask.i = load i64, ptr %cur_arch_ptr.i, align 8
  %and_mask.i = and i64 %arch_mask.i, 18
  %is_match.i = icmp eq i64 %and_mask.i, 18
  br i1 %is_match.i, label %check_count.i, label %next_arch.i

next_arch.i:                                      ; preds = %ent_loop_body.i, %check_count.i, %arch_body.i
  %indvars.iv.next8.i = add nuw nsw i64 %indvars.iv7.i, 1
  %exitcond11.not.i = icmp eq i64 %indvars.iv.next8.i, %wide.trip.count10.i
  br i1 %exitcond11.not.i, label %system_PrintObstacleSystem.exit, label %arch_body.i

check_count.i:                                    ; preds = %arch_body.i
  %cnt_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 8
  %arch_count.i = load i32, ptr %cnt_slot.i, align 4
  %has_entities.i = icmp sgt i32 %arch_count.i, 0
  br i1 %has_entities.i, label %ent_loop_cond.preheader.i, label %next_arch.i

ent_loop_cond.preheader.i:                        ; preds = %check_count.i
  %pos_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 32
  %wide.trip.count.i = zext nneg i32 %arch_count.i to i64
  br label %ent_loop_body.i

ent_loop_body.i:                                  ; preds = %ent_loop_body.i, %ent_loop_cond.preheader.i
  %indvars.iv.i = phi i64 [ 0, %ent_loop_cond.preheader.i ], [ %indvars.iv.next.i, %ent_loop_body.i ]
  %pos_raw.i = load ptr, ptr %pos_col_slot.i, align 8
  %pos_elem.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i, i64 %indvars.iv.i
  %puts_call.i = tail call i32 @puts(ptr nonnull dereferenceable(1) @str_lit.2)
  %x_val.i = load float, ptr %pos_elem.i, align 4
  %f_to_d.i = fpext float %x_val.i to double
  %printf_call.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d.i)
  %pos_y.i = getelementptr inbounds nuw i8, ptr %pos_elem.i, i64 4
  %y_val.i = load float, ptr %pos_y.i, align 4
  %f_to_d1.i = fpext float %y_val.i to double
  %printf_call2.i = tail call i32 (ptr, ...) @printf(ptr nonnull dereferenceable(1) @fmt_f.4, double %f_to_d1.i)
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %next_arch.i, label %ent_loop_body.i

system_PrintObstacleSystem.exit:                  ; preds = %next_arch.i, %entry
  ret void
}

; Function Attrs: nounwind
define void @pipeline_PhysicsLoop(ptr nocapture %world) local_unnamed_addr #4 {
entry:
  %num_archs.i = load i32, ptr %world, align 4
  %has_more_archs7.i = icmp sgt i32 %num_archs.i, 0
  br i1 %has_more_archs7.i, label %arch_body.lr.ph.i, label %system_MovementSystem.exit

arch_body.lr.ph.i:                                ; preds = %entry
  %world_arch_tables_slot.i = getelementptr inbounds nuw i8, ptr %world, i64 8
  %time_res_slot.i = getelementptr inbounds nuw i8, ptr %world, i64 64
  %wide.trip.count13.i = zext nneg i32 %num_archs.i to i64
  br label %arch_body.i

arch_body.i:                                      ; preds = %next_arch.i, %arch_body.lr.ph.i
  %indvars.iv10.i = phi i64 [ 0, %arch_body.lr.ph.i ], [ %indvars.iv.next11.i, %next_arch.i ]
  %tables_base.i = load ptr, ptr %world_arch_tables_slot.i, align 8
  %cur_arch_ptr.i = getelementptr inbounds nuw %struct.Archetype, ptr %tables_base.i, i64 %indvars.iv10.i
  %arch_mask.i = load i64, ptr %cur_arch_ptr.i, align 8
  %and_mask.i = and i64 %arch_mask.i, 6
  %is_match.i = icmp eq i64 %and_mask.i, 6
  br i1 %is_match.i, label %check_count.i, label %next_arch.i

next_arch.i:                                      ; preds = %ent_loop_body.i, %check_count.i, %arch_body.i
  %indvars.iv.next11.i = add nuw nsw i64 %indvars.iv10.i, 1
  %exitcond14.not.i = icmp eq i64 %indvars.iv.next11.i, %wide.trip.count13.i
  br i1 %exitcond14.not.i, label %system_MovementSystem.exit, label %arch_body.i

check_count.i:                                    ; preds = %arch_body.i
  %cnt_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 8
  %arch_count.i = load i32, ptr %cnt_slot.i, align 4
  %has_entities.i = icmp sgt i32 %arch_count.i, 0
  br i1 %has_entities.i, label %ent_loop_cond.preheader.i, label %next_arch.i

ent_loop_cond.preheader.i:                        ; preds = %check_count.i
  %pos_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 32
  %vel_col_slot.i = getelementptr inbounds nuw i8, ptr %cur_arch_ptr.i, i64 40
  %wide.trip.count.i = zext nneg i32 %arch_count.i to i64
  br label %ent_loop_body.i

ent_loop_body.i:                                  ; preds = %ent_loop_body.i, %ent_loop_cond.preheader.i
  %indvars.iv.i = phi i64 [ 0, %ent_loop_cond.preheader.i ], [ %indvars.iv.next.i, %ent_loop_body.i ]
  %pos_raw.i = load ptr, ptr %pos_col_slot.i, align 8
  %pos_elem.i = getelementptr inbounds nuw %struct.Position, ptr %pos_raw.i, i64 %indvars.iv.i
  %vel_raw.i = load ptr, ptr %vel_col_slot.i, align 8
  %vel_elem.i = getelementptr inbounds nuw %struct.Velocity, ptr %vel_raw.i, i64 %indvars.iv.i
  %vx_val.i = load float, ptr %vel_elem.i, align 4
  %dt_val.i = load float, ptr %time_res_slot.i, align 4
  %fmul.i = fmul float %vx_val.i, %dt_val.i
  %cur_val.i = load float, ptr %pos_elem.i, align 4
  %fadd.i = fadd float %cur_val.i, %fmul.i
  store float %fadd.i, ptr %pos_elem.i, align 4
  %vel_vy.i = getelementptr inbounds nuw i8, ptr %vel_elem.i, i64 4
  %vy_val.i = load float, ptr %vel_vy.i, align 4
  %dt_val2.i = load float, ptr %time_res_slot.i, align 4
  %fmul3.i = fmul float %vy_val.i, %dt_val2.i
  %pos_y_gep.i = getelementptr inbounds nuw i8, ptr %pos_elem.i, i64 4
  %cur_val4.i = load float, ptr %pos_y_gep.i, align 4
  %fadd5.i = fadd float %cur_val4.i, %fmul3.i
  store float %fadd5.i, ptr %pos_y_gep.i, align 4
  %indvars.iv.next.i = add nuw nsw i64 %indvars.iv.i, 1
  %exitcond.not.i = icmp eq i64 %indvars.iv.next.i, %wide.trip.count.i
  br i1 %exitcond.not.i, label %next_arch.i, label %ent_loop_body.i

system_MovementSystem.exit:                       ; preds = %next_arch.i, %entry
  tail call void @world_apply_commands(ptr nonnull %world)
  ret void
}

; Function Attrs: nocallback nofree nounwind willreturn memory(argmem: write)
declare void @llvm.memset.p0.i64(ptr nocapture writeonly, i8, i64, i1 immarg) #12

; Function Attrs: nocallback nofree nosync nounwind speculatable willreturn memory(none)
declare i32 @llvm.smax.i32(i32, i32) #13

; Function Attrs: nofree nounwind willreturn allockind("alloc,zeroed") allocsize(0,1) memory(inaccessiblemem: readwrite)
declare noalias noundef ptr @calloc(i64 noundef, i64 noundef) local_unnamed_addr #14

define void @world_cmd_set_Position(ptr %0, i32 %1, float %2, float %3) local_unnamed_addr {
  tail call void @world_cmd_add_Position(ptr %0, i32 %1, float %2, float %3)
  ret void
}

define void @world_cmd_set_Velocity(ptr %0, i32 %1, float %2, float %3) local_unnamed_addr {
  tail call void @world_cmd_add_Velocity(ptr %0, i32 %1, float %2, float %3)
  ret void
}

; Function Attrs: nounwind
define void @world_set_PlayerTag(ptr nocapture %0, i32 %1, i32 %2) local_unnamed_addr #4 {
  tail call void @world_add_PlayerTag(ptr nocapture %0, i32 %1, i32 %2) #4
  ret void
}

; Function Attrs: nounwind
define void @world_set_Obstacle(ptr nocapture %0, i32 %1, i1 %2) local_unnamed_addr #4 {
  tail call void @world_add_Obstacle(ptr nocapture %0, i32 %1, i1 %2) #4
  ret void
}

; Function Attrs: nounwind
define void @world_set_ChildOf(ptr nocapture %0, i32 %1, i32 %2) local_unnamed_addr #4 {
  tail call void @world_add_ChildOf(ptr nocapture %0, i32 %1, i32 %2) #4
  ret void
}

; Function Attrs: nounwind
define void @world_set_Position(ptr nocapture %0, i32 %1, float %2, float %3) local_unnamed_addr #4 {
  tail call void @world_add_Position(ptr nocapture %0, i32 %1, float %2, float %3) #4
  ret void
}

; Function Attrs: nounwind
define void @world_set_Velocity(ptr nocapture %0, i32 %1, float %2, float %3) local_unnamed_addr #4 {
  tail call void @world_add_Velocity(ptr nocapture %0, i32 %1, float %2, float %3) #4
  ret void
}

define void @world_cmd_set_ChildOf(ptr %0, i32 %1, i32 %2) local_unnamed_addr {
  tail call void @world_cmd_add_ChildOf(ptr %0, i32 %1, i32 %2)
  ret void
}

define void @world_cmd_set_PlayerTag(ptr %0, i32 %1, i32 %2) local_unnamed_addr {
  tail call void @world_cmd_add_PlayerTag(ptr %0, i32 %1, i32 %2)
  ret void
}

define void @world_cmd_set_Obstacle(ptr %0, i32 %1, i1 %2) local_unnamed_addr {
  tail call void @world_cmd_add_Obstacle(ptr %0, i32 %1, i1 %2)
  ret void
}

attributes #0 = { nofree nounwind }
attributes #1 = { mustprogress nounwind willreturn allockind("realloc") allocsize(1) memory(argmem: readwrite, inaccessiblemem: readwrite) "alloc-family"="malloc" }
attributes #2 = { mustprogress nofree nounwind willreturn allockind("alloc,uninitialized") allocsize(0) memory(inaccessiblemem: readwrite) "alloc-family"="malloc" }
attributes #3 = { mustprogress nounwind willreturn allockind("free") memory(argmem: readwrite, inaccessiblemem: readwrite) "alloc-family"="malloc" }
attributes #4 = { nounwind }
attributes #5 = { mustprogress nounwind willreturn }
attributes #6 = { mustprogress nofree nounwind willreturn memory(write, argmem: none, inaccessiblemem: readwrite) }
attributes #7 = { mustprogress nofree norecurse nosync nounwind willreturn memory(readwrite, inaccessiblemem: none) }
attributes #8 = { mustprogress nofree norecurse nosync nounwind willreturn memory(read, inaccessiblemem: none) }
attributes #9 = { mustprogress nofree norecurse nosync nounwind willreturn memory(argmem: write) }
attributes #10 = { mustprogress nofree norecurse nosync nounwind willreturn memory(none) }
attributes #11 = { nofree norecurse nosync nounwind memory(readwrite, inaccessiblemem: none) }
attributes #12 = { nocallback nofree nounwind willreturn memory(argmem: write) }
attributes #13 = { nocallback nofree nosync nounwind speculatable willreturn memory(none) }
attributes #14 = { nofree nounwind willreturn allockind("alloc,zeroed") allocsize(0,1) memory(inaccessiblemem: readwrite) "alloc-family"="malloc" }

!0 = distinct !{!0, !1, !2}
!1 = !{!"llvm.loop.isvectorized", i32 1}
!2 = !{!"llvm.loop.unroll.runtime.disable"}
!3 = distinct !{!3, !2, !1}
