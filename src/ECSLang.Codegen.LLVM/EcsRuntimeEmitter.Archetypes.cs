using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class EcsRuntimeEmitter
{
    public unsafe void EmitMultiArchetypeRuntime(
        LLVMTargetDataRef dataLayout,
        LLVMTypeRef reallocType,
        LLVMValueRef reallocFunc,
        LLVMTypeRef memcpyType,
        LLVMValueRef memcpyFunc,
        LLVMTypeRef memsetType,
        LLVMValueRef memsetFunc,
        LLVMTypeRef mallocType,
        LLVMValueRef mallocFunc,
        LLVMTypeRef freeType,
        LLVMValueRef freeFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var i32PtrType = LLVMTypeRef.CreatePointer(_context.Int32Type, 0);
        var archPtrType = LLVMTypeRef.CreatePointer(_archStructType, 0);
        var worldPtrType = LLVMTypeRef.CreatePointer(_worldStructType, 0);

        ulong archStructSize = Math.Max(8, LlvmApi.ABISizeOfType(dataLayout, _archStructType));
        var archStructSizeVal = LLVMValueRef.CreateConstInt(_context.Int64Type, archStructSize);

        ulong worldStructSize = Math.Max(64, LlvmApi.ABISizeOfType(dataLayout, _worldStructType));
        var worldStructSizeVal = LLVMValueRef.CreateConstInt(_context.Int64Type, worldStructSize);

        var compNames = _typeChecker.Components.Keys.ToList();
        int totalComps = compNames.Count;

        // =========================================================================
        // Helper: world_get_or_create_archetype(ptr world, i64 mask) -> i32 arch_idx
        // =========================================================================
        var getArchType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType, _context.Int64Type }, false);
        var getArchFunc = _module.AddFunction("world_get_or_create_archetype", getArchType);
        var gEntryBB = getArchFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(gEntryBB);

        var worldParamG = getArchFunc.GetParam(0);
        var targetMask = getArchFunc.GetParam(1);

        var archCountSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 0, "arch_count_slot");
        var archCapSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 1, "arch_cap_slot");
        var archTablesSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 2, "arch_tables_slot");

        var curCount = _builder.BuildLoad2(_context.Int32Type, archCountSlot, "cur_count");
        var iAlloca = _builder.BuildAlloca(_context.Int32Type, "i");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), iAlloca);

        var sCondBB = getArchFunc.AppendBasicBlock("search_cond");
        var sBodyBB = getArchFunc.AppendBasicBlock("search_body");
        var sNotFoundBB = getArchFunc.AppendBasicBlock("not_found");
        var sRetBB = getArchFunc.AppendBasicBlock("return_found");

        _builder.BuildBr(sCondBB);

        // Search loop
        _builder.PositionAtEnd(sCondBB);
        var curI = _builder.BuildLoad2(_context.Int32Type, iAlloca, "cur_i");
        var hasMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curI, curCount, "has_more");
        _builder.BuildCondBr(hasMore, sBodyBB, sNotFoundBB);

        _builder.PositionAtEnd(sBodyBB);
        var tablesBase = _builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var existingArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBase, new[] { curI }, "arch_elem");
        var maskGEP = _builder.BuildStructGEP2(_archStructType, existingArchPtr, 0, "mask_gep");
        var existingMask = _builder.BuildLoad2(_context.Int64Type, maskGEP, "existing_mask");
        var isMatch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, existingMask, targetMask, "is_match");

        var sNextBB = getArchFunc.AppendBasicBlock("search_next");
        _builder.BuildCondBr(isMatch, sRetBB, sNextBB);

        _builder.PositionAtEnd(sNextBB);
        var nextI = _builder.BuildAdd(curI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_i");
        _builder.BuildStore(nextI, iAlloca);
        _builder.BuildBr(sCondBB);

        _builder.PositionAtEnd(sRetBB);
        _builder.BuildRet(curI);

        // Not found: allocate new archetype
        _builder.PositionAtEnd(sNotFoundBB);
        var curCap = _builder.BuildLoad2(_context.Int32Type, archCapSlot, "cur_cap");
        var needGrow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount, curCap, "need_grow");
        var growBB = getArchFunc.AppendBasicBlock("grow_tables");
        var initArchBB = getArchFunc.AppendBasicBlock("init_arch");

        _builder.BuildCondBr(needGrow, growBB, initArchBB);

        _builder.PositionAtEnd(growBB);
        var capIsZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "cap_zero");
        var doubleCap = _builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cap");
        var newCap = _builder.BuildSelect(capIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), doubleCap, "new_cap");
        _builder.BuildStore(newCap, archCapSlot);

        var newCap64 = _builder.BuildZExt(newCap, _context.Int64Type, "new_cap64");
        var allocBytes = _builder.BuildMul(newCap64, archStructSizeVal, "alloc_bytes");
        var curTablesRaw = _builder.BuildLoad2(archPtrType, archTablesSlot, "cur_tables_raw");
        var curTablesI8 = _builder.BuildBitCast(curTablesRaw, i8PtrType, "cur_tables_i8");
        var reallocCall = _builder.BuildCall2(reallocType, reallocFunc, new[] { curTablesI8, allocBytes }, "new_tables_i8");
        var newTablesTyped = _builder.BuildBitCast(reallocCall, archPtrType, "new_tables_typed");
        _builder.BuildStore(newTablesTyped, archTablesSlot);
        _builder.BuildBr(initArchBB);

        _builder.PositionAtEnd(initArchBB);
        var newIdx = curCount;
        var nextCount = _builder.BuildAdd(curCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_count");
        _builder.BuildStore(nextCount, archCountSlot);

        var latestTables = _builder.BuildLoad2(archPtrType, archTablesSlot, "latest_tables");
        var newArchElem = _builder.BuildInBoundsGEP2(_archStructType, latestTables, new[] { newIdx }, "new_arch_elem");

        // Set mask
        var newMaskGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 0, "m_gep");
        _builder.BuildStore(targetMask, newMaskGEP);

        // Set count = 0
        var newCountGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 1, "cnt_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), newCountGEP);

        // Set cap = 0
        var newCapGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 2, "cap_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), newCapGEP);

        // Set entities = null
        var newEntGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 3, "ent_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), newEntGEP);

        // Set columns = null
        var colsArrGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 4, "cols_gep");
        for (int k = 0; k < totalComps; k++)
        {
            var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsArrGEP, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
            }, $"col_slot_{k}");
            _builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), colSlot);
        }

        _builder.BuildRet(newIdx);

        // =========================================================================
        // Helper: world_grow_archetype(ptr world, i32 arch_idx) -> void
        // =========================================================================
        var growArchType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var growArchFunc = _module.AddFunction("world_grow_archetype", growArchType);
        var grEntryBB = growArchFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(grEntryBB);

        var worldParamGr = growArchFunc.GetParam(0);
        var archIdxParam = growArchFunc.GetParam(1);

        var tablesSlotGr = _builder.BuildStructGEP2(_worldStructType, worldParamGr, 2, "tables_slot_gr");
        var tablesBaseGr = _builder.BuildLoad2(archPtrType, tablesSlotGr, "tables_base");
        var archElemGr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseGr, new[] { archIdxParam }, "arch_elem");

        var capSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 2, "cap_slot");
        var curArchCap = _builder.BuildLoad2(_context.Int32Type, capSlotGr, "cur_cap");
        var archCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_zero");
        var doubleArchCap = _builder.BuildMul(curArchCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cap");
        var newArchCap = _builder.BuildSelect(archCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 32), doubleArchCap, "new_arch_cap");
        _builder.BuildStore(newArchCap, capSlotGr);

        var newArchCap64 = _builder.BuildZExt(newArchCap, _context.Int64Type, "new_cap64");

        // Reallocate entities array (i32*): newArchCap * 4 bytes
        var entSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 3, "ent_slot");
        var curEntRaw = _builder.BuildLoad2(i8PtrType, entSlotGr, "cur_ent_raw");
        var entBytes = _builder.BuildMul(newArchCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "ent_bytes");
        var newEntRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curEntRaw, entBytes }, "new_ent_raw");
        _builder.BuildStore(newEntRaw, entSlotGr);

        // Reallocate active component columns
        var maskSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 0, "mask_slot");
        var archMaskGr = _builder.BuildLoad2(_context.Int64Type, maskSlotGr, "arch_mask");
        var colsArrGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 4, "cols_arr");

        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            ulong compSize = _compSizes[compName];
            ulong compBit = 1UL << k;

            var bitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, compBit);
            var andRes = _builder.BuildAnd(archMaskGr, bitVal, $"has_{compName}");
            var hasComp = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, andRes, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_{compName}");

            var growColBB = growArchFunc.AppendBasicBlock($"grow_col_{compName}");
            var skipColBB = growArchFunc.AppendBasicBlock($"skip_col_{compName}");

            _builder.BuildCondBr(hasComp, growColBB, skipColBB);

            _builder.PositionAtEnd(growColBB);
            var colSlotK = _builder.BuildInBoundsGEP2(_colArrayType, colsArrGr, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
            }, $"col_slot_{compName}");
            var curColRaw = _builder.BuildLoad2(i8PtrType, colSlotK, $"cur_col_{compName}");
            var colBytes = _builder.BuildMul(newArchCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, compSize), $"col_bytes_{compName}");
            var newColRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curColRaw, colBytes }, $"new_col_{compName}");
            _builder.BuildStore(newColRaw, colSlotK);
            _builder.BuildBr(skipColBB);

            _builder.PositionAtEnd(skipColBB);
        }

        _builder.BuildRetVoid();

        // =========================================================================
        // Factory: ecs_create_world() -> ptr
        // =========================================================================
        var createWorldType = LLVMTypeRef.CreateFunction(worldPtrType, Array.Empty<LLVMTypeRef>(), false);
        var createWorldFunc = _module.AddFunction("ecs_create_world", createWorldType);
        var cwEntryBB = createWorldFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(cwEntryBB);

        var rawWorld = _builder.BuildCall2(mallocType, mallocFunc, new[] { worldStructSizeVal }, "raw_world");
        _builder.BuildCall2(memsetType, memsetFunc, new[] { rawWorld, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), worldStructSizeVal }, "");
        var typedWorld = _builder.BuildBitCast(rawWorld, worldPtrType, "typed_world");

        // Initialize empty Archetype 0 in this world
        _builder.BuildCall2(getArchType, getArchFunc, new[] { typedWorld, LLVMValueRef.CreateConstInt(_context.Int64Type, 0) }, "a0_init");
        _builder.BuildRet(typedWorld);

        // =========================================================================
        // Win32 SRWLock (kernel32.lib)
        // =========================================================================
        var srwFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        var srwAcquireFunc = _module.GetNamedFunction("AcquireSRWLockExclusive");
        if (srwAcquireFunc.Handle == IntPtr.Zero)
            srwAcquireFunc = _module.AddFunction("AcquireSRWLockExclusive", srwFuncType);
        var srwReleaseFunc = _module.GetNamedFunction("ReleaseSRWLockExclusive");
        if (srwReleaseFunc.Handle == IntPtr.Zero)
            srwReleaseFunc = _module.AddFunction("ReleaseSRWLockExclusive", srwFuncType);

        // =========================================================================
        // Helper: world_alloc_entity(ptr world) -> i32 entity_id
        // =========================================================================
        var allocEntType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType }, false);
        var allocEntFunc = _module.AddFunction("world_alloc_entity", allocEntType);
        var alEntryBB = allocEntFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(alEntryBB);

        var worldParamAl = allocEntFunc.GetParam(0);
        var entCountSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 3, "ent_count_slot");
        var entCapSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 4, "ent_cap_slot");
        var entArchSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 5, "ent_arch_slot");
        var entRowSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 6, "ent_row_slot");

        var curEntCount = _builder.BuildLoad2(_context.Int32Type, entCountSlotAl, "cur_ent_count");
        var curEntCap = _builder.BuildLoad2(_context.Int32Type, entCapSlotAl, "cur_ent_cap");
        var needGrowEnt = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curEntCount, curEntCap, "need_grow_ent");

        var growEntBB = allocEntFunc.AppendBasicBlock("grow_ent");
        var assignEntBB = allocEntFunc.AppendBasicBlock("assign_ent");

        _builder.BuildCondBr(needGrowEnt, growEntBB, assignEntBB);

        _builder.PositionAtEnd(growEntBB);
        var entCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curEntCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "ent_cap_zero");
        var doubleEntCap = _builder.BuildMul(curEntCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_ent_cap");
        var newEntCap = _builder.BuildSelect(entCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 64), doubleEntCap, "new_ent_cap");
        _builder.BuildStore(newEntCap, entCapSlotAl);

        var newEntCap64 = _builder.BuildZExt(newEntCap, _context.Int64Type, "new_ent_cap64");
        var bytesForEnt = _builder.BuildMul(newEntCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "bytes_for_ent");

        // Realloc entity_arch
        var curArchArrRaw = _builder.BuildLoad2(i32PtrType, entArchSlotAl, "cur_arch_arr");
        var curArchArrI8 = _builder.BuildBitCast(curArchArrRaw, i8PtrType, "cur_arch_i8");
        var newArchArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curArchArrI8, bytesForEnt }, "new_arch_i8");
        var newArchArrTyped = _builder.BuildBitCast(newArchArrI8, i32PtrType, "new_arch_typed");
        _builder.BuildStore(newArchArrTyped, entArchSlotAl);

        // Realloc entity_row
        var curRowArrRaw = _builder.BuildLoad2(i32PtrType, entRowSlotAl, "cur_row_arr");
        var curRowArrI8 = _builder.BuildBitCast(curRowArrRaw, i8PtrType, "cur_row_i8");
        var newRowArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curRowArrI8, bytesForEnt }, "new_row_i8");
        var newRowArrTyped = _builder.BuildBitCast(newRowArrI8, i32PtrType, "new_row_typed");
        _builder.BuildStore(newRowArrTyped, entRowSlotAl);

        _builder.BuildBr(assignEntBB);

        _builder.PositionAtEnd(assignEntBB);
        var eAlloc = curEntCount;
        var nextEntCountVal = _builder.BuildAdd(curEntCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_ent_cnt");
        _builder.BuildStore(nextEntCountVal, entCountSlotAl);

        // Initialize newly allocated entity slot to unassigned (-1)
        var curArchArrAlloc = _builder.BuildLoad2(i32PtrType, entArchSlotAl, "cur_arch_arr_alloc");
        var eArchSlotAlloc = _builder.BuildInBoundsGEP2(_context.Int32Type, curArchArrAlloc, new[] { eAlloc }, "e_arch_slot_alloc");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eArchSlotAlloc);

        var curRowArrAlloc = _builder.BuildLoad2(i32PtrType, entRowSlotAl, "cur_row_arr_alloc");
        var eRowSlotAlloc = _builder.BuildInBoundsGEP2(_context.Int32Type, curRowArrAlloc, new[] { eAlloc }, "e_row_slot_alloc");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eRowSlotAlloc);

        _builder.BuildRet(eAlloc);

        // =========================================================================
        // Helper: world_assign_a0(ptr world, i32 entity_id) -> void
        // =========================================================================
        var assignA0Type = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var assignA0Func = _module.AddFunction("world_assign_a0", assignA0Type);
        var a0EntryBB = assignA0Func.AppendBasicBlock("entry");
        _builder.PositionAtEnd(a0EntryBB);

        var wArgA0 = assignA0Func.GetParam(0);
        var eArgA0 = assignA0Func.GetParam(1);

        var archArrA0Slot = _builder.BuildStructGEP2(_worldStructType, wArgA0, 5, "arch_arr_a0_slot");
        var archArrA0 = _builder.BuildLoad2(i32PtrType, archArrA0Slot, "arch_arr_a0");
        var eArchSlotA0 = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrA0, new[] { eArgA0 }, "e_arch_slot_a0");
        var curArchValA0 = _builder.BuildLoad2(_context.Int32Type, eArchSlotA0, "cur_arch_val_a0");
        var isUnassigned = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchValA0, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_unassigned");

        var doAssignBB = assignA0Func.AppendBasicBlock("do_assign");
        var exitA0BB = assignA0Func.AppendBasicBlock("exit_a0");
        _builder.BuildCondBr(isUnassigned, doAssignBB, exitA0BB);

        _builder.PositionAtEnd(doAssignBB);
        var a0 = _builder.BuildCall2(getArchType, getArchFunc, new[] { wArgA0, LLVMValueRef.CreateConstInt(_context.Int64Type, 0) }, "a0");

        var tablesSlotA0 = _builder.BuildStructGEP2(_worldStructType, wArgA0, 2, "tables_slot_a0");
        var tablesBaseA0 = _builder.BuildLoad2(archPtrType, tablesSlotA0, "tables_a0");
        var a0Ptr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseA0, new[] { a0 }, "a0_ptr");
        var cntSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr, 1, "cnt_slot0");
        var curCount0 = _builder.BuildLoad2(_context.Int32Type, cntSlot0, "cur_cnt0");
        var capSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr, 2, "cap_slot0");
        var curCap0 = _builder.BuildLoad2(_context.Int32Type, capSlot0, "cur_cap0");
        var needGrow0 = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount0, curCap0, "need_grow0");

        var grow0BB = assignA0Func.AppendBasicBlock("grow_a0");
        var afterGrow0BB = assignA0Func.AppendBasicBlock("after_grow_a0");
        _builder.BuildCondBr(needGrow0, grow0BB, afterGrow0BB);

        _builder.PositionAtEnd(grow0BB);
        _builder.BuildCall2(growArchType, growArchFunc, new[] { wArgA0, a0 }, "");
        _builder.BuildBr(afterGrow0BB);

        _builder.PositionAtEnd(afterGrow0BB);
        var tablesBaseA0_2 = _builder.BuildLoad2(archPtrType, tablesSlotA0, "tables_a0_2");
        var a0Ptr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseA0_2, new[] { a0 }, "a0_ptr2");
        var cntSlot0_2 = _builder.BuildStructGEP2(_archStructType, a0Ptr2, 1, "cnt_slot0_2");
        var row = _builder.BuildLoad2(_context.Int32Type, cntSlot0_2, "row");
        var nextCount0 = _builder.BuildAdd(row, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_cnt0");
        _builder.BuildStore(nextCount0, cntSlot0_2);

        // a0.entities[row] = e
        var entSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr2, 3, "ent_slot0");
        var entRaw0 = _builder.BuildLoad2(i8PtrType, entSlot0, "ent_raw0");
        var entTyped0 = _builder.BuildBitCast(entRaw0, i32PtrType, "ent_typed0");
        var entElem0 = _builder.BuildInBoundsGEP2(_context.Int32Type, entTyped0, new[] { row }, "ent_elem0");
        _builder.BuildStore(eArgA0, entElem0);

        // world.entity_arch[e] = a0
        _builder.BuildStore(a0, eArchSlotA0);

        // world.entity_row[e] = row
        var rowArrA0Slot = _builder.BuildStructGEP2(_worldStructType, wArgA0, 6, "row_arr_a0_slot");
        var rowArrA0 = _builder.BuildLoad2(i32PtrType, rowArrA0Slot, "row_arr_a0");
        var eRowSlotA0 = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrA0, new[] { eArgA0 }, "e_row_slot_a0");
        _builder.BuildStore(row, eRowSlotA0);
        _builder.BuildBr(exitA0BB);

        _builder.PositionAtEnd(exitA0BB);
        _builder.BuildRetVoid();

        // =========================================================================
        // Helper: world_spawn(ptr world) -> i32 entity_id
        // =========================================================================
        var spawnType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType }, false);
        var spawnFunc = _module.AddFunction("world_spawn", spawnType);
        var spEntryBB = spawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(spEntryBB);

        var worldParamSp = spawnFunc.GetParam(0);
        var eSpawn = _builder.BuildCall2(allocEntType, allocEntFunc, new[] { worldParamSp }, "e_spawn");
        _builder.BuildCall2(assignA0Type, assignA0Func, new[] { worldParamSp, eSpawn }, "");
        _builder.BuildRet(eSpawn);

        // =========================================================================
        // Helper: world_despawn(ptr world, i32 entity_id) -> void
        // =========================================================================
        var despawnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var despawnFunc = _module.AddFunction("world_despawn", despawnType);
        var dsEntryBB = despawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(dsEntryBB);

        var wArgDs = despawnFunc.GetParam(0);
        var eArgDs = despawnFunc.GetParam(1);

        var entCountSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 3, "ent_count_slot_ds");
        var totalEntsDs = _builder.BuildLoad2(_context.Int32Type, entCountSlotDs, "total_ents_ds");

        var eNonNegDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, eArgDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "e_non_neg_ds");
        var eInBoundsDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, eArgDs, totalEntsDs, "e_in_bounds_ds");
        var isValidIdDs = _builder.BuildAnd(eNonNegDs, eInBoundsDs, "is_valid_id_ds");

        var checkArchBB = despawnFunc.AppendBasicBlock("check_arch");
        var dsExitBB = despawnFunc.AppendBasicBlock("ds_exit");
        _builder.BuildCondBr(isValidIdDs, checkArchBB, dsExitBB);

        _builder.PositionAtEnd(checkArchBB);
        var entArchSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 5, "ent_arch_slot_ds");
        var entRowSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 6, "ent_row_slot_ds");
        var tablesSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 2, "tables_slot_ds");

        var archArrDs = _builder.BuildLoad2(i32PtrType, entArchSlotDs, "arch_arr_ds");
        var eArchSlotDsInst = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrDs, new[] { eArgDs }, "e_arch_slot_ds_inst");
        var curArchIdxDs = _builder.BuildLoad2(_context.Int32Type, eArchSlotDsInst, "cur_arch_idx_ds");

        var isAliveDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curArchIdxDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_alive_ds");
        var doDespawnBB = despawnFunc.AppendBasicBlock("do_despawn");
        _builder.BuildCondBr(isAliveDs, doDespawnBB, dsExitBB);

        _builder.PositionAtEnd(doDespawnBB);
        var rowArrDs = _builder.BuildLoad2(i32PtrType, entRowSlotDs, "row_arr_ds");
        var eRowSlotDsInst = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrDs, new[] { eArgDs }, "e_row_slot_ds_inst");
        var curRowDs = _builder.BuildLoad2(_context.Int32Type, eRowSlotDsInst, "cur_row_ds");

        var tablesBaseDs = _builder.BuildLoad2(archPtrType, tablesSlotDs, "tables_base_ds");
        var curArchPtrDs = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseDs, new[] { curArchIdxDs }, "cur_arch_ptr_ds");

        var curCntSlotDs = _builder.BuildStructGEP2(_archStructType, curArchPtrDs, 1, "cur_cnt_slot_ds");
        var curArchCountDs = _builder.BuildLoad2(_context.Int32Type, curCntSlotDs, "cur_arch_cnt_ds");
        var lastRowDs = _builder.BuildSub(curArchCountDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "last_row_ds");
        _builder.BuildStore(lastRowDs, curCntSlotDs);

        var isLastRowDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curRowDs, lastRowDs, "is_last_row_ds");
        var doSwapDsBB = despawnFunc.AppendBasicBlock("do_swap_ds");
        var afterSwapDsBB = despawnFunc.AppendBasicBlock("after_swap_ds");
        _builder.BuildCondBr(isLastRowDs, afterSwapDsBB, doSwapDsBB);

        _builder.PositionAtEnd(doSwapDsBB);
        // Move entity ID from lastRow to curRow
        var curEntSlotDs = _builder.BuildStructGEP2(_archStructType, curArchPtrDs, 3, "cur_ent_slot_ds");
        var curEntRawDs = _builder.BuildLoad2(i8PtrType, curEntSlotDs, "cur_ent_raw_ds");
        var curEntTypedDs = _builder.BuildBitCast(curEntRawDs, i32PtrType, "cur_ent_typed_ds");
        var lastEntElemDs = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedDs, new[] { lastRowDs }, "last_ent_elem_ds");
        var movedEDs = _builder.BuildLoad2(_context.Int32Type, lastEntElemDs, "moved_e_ds");
        var curEntElemDs = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedDs, new[] { curRowDs }, "cur_ent_elem_ds");
        _builder.BuildStore(movedEDs, curEntElemDs);

        // Copy all active components from lastRow to curRow
        var curMaskSlotDs = _builder.BuildStructGEP2(_archStructType, curArchPtrDs, 0, "cur_mask_slot_ds");
        var curMaskDs = _builder.BuildLoad2(_context.Int64Type, curMaskSlotDs, "cur_mask_ds");
        var curColsArrDs = _builder.BuildStructGEP2(_archStructType, curArchPtrDs, 4, "cur_cols_arr_ds");

        for (int c = 0; c < totalComps; c++)
        {
            var cName = compNames[c];
            ulong cBit = 1UL << c;
            ulong cSize = _compSizes[cName];
            var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

            var cAnd = _builder.BuildAnd(curMaskDs, cBitVal, $"has_sw_ds_{cName}");
            var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_sw_ds_{cName}");

            var swapCBB = despawnFunc.AppendBasicBlock($"swap_ds_{cName}");
            var skipSwCBB = despawnFunc.AppendBasicBlock($"skip_sw_ds_{cName}");
            _builder.BuildCondBr(hasC, swapCBB, skipSwCBB);

            _builder.PositionAtEnd(swapCBB);
            var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArrDs, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
            }, $"sw_col_ds_{cName}");
            var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_raw_ds_{cName}");
            var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_typed_ds_{cName}");
            var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { lastRowDs }, $"sw_src_ds_{cName}");
            var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curRowDs }, $"sw_dst_ds_{cName}");
            var srcI8 = _builder.BuildBitCast(srcElem, i8PtrType, "sw_src_ds_i8");
            var dstI8 = _builder.BuildBitCast(dstElem, i8PtrType, "sw_dst_ds_i8");
            _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstI8, srcI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
            _builder.BuildBr(skipSwCBB);

            _builder.PositionAtEnd(skipSwCBB);
        }

        // Update world.entity_row[movedEDs] = curRowDs
        var movedERowSlotDs = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrDs, new[] { movedEDs }, "moved_e_row_slot_ds");
        _builder.BuildStore(curRowDs, movedERowSlotDs);
        _builder.BuildBr(afterSwapDsBB);

        _builder.PositionAtEnd(afterSwapDsBB);
        // Mark despawned: world.entity_arch[e] = -1, world.entity_row[e] = -1
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eArchSlotDsInst);
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eRowSlotDsInst);
        _builder.BuildBr(dsExitBB);

        _builder.PositionAtEnd(dsExitBB);
        _builder.BuildRetVoid();

        // =========================================================================
        // Helper: world_cmd_ensure_cap(ptr world, i32 needed) -> void
        // =========================================================================
        var ensureCapType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var ensureCapFunc = _module.AddFunction("world_cmd_ensure_cap", ensureCapType);
        var ecEntryBB = ensureCapFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(ecEntryBB);

        var wArgEc = ensureCapFunc.GetParam(0);
        var neededArg = ensureCapFunc.GetParam(1);

        var cmdCountSlotEc = _builder.BuildStructGEP2(_worldStructType, wArgEc, 7, "cmd_cnt_slot_ec");
        var cmdCapSlotEc = _builder.BuildStructGEP2(_worldStructType, wArgEc, 8, "cmd_cap_slot_ec");
        var cmdDataSlotEc = _builder.BuildStructGEP2(_worldStructType, wArgEc, 9, "cmd_data_slot_ec");

        var curCmdCount = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotEc, "cur_cmd_cnt");
        var curCmdCap = _builder.BuildLoad2(_context.Int32Type, cmdCapSlotEc, "cur_cmd_cap");
        var neededTotal = _builder.BuildAdd(curCmdCount, neededArg, "needed_total");
        var needGrowCmd = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, neededTotal, curCmdCap, "need_grow_cmd");

        var growCmdBB = ensureCapFunc.AppendBasicBlock("grow_cmd");
        var ecExitBB = ensureCapFunc.AppendBasicBlock("ec_exit");
        _builder.BuildCondBr(needGrowCmd, growCmdBB, ecExitBB);

        _builder.PositionAtEnd(growCmdBB);
        var doubleCmdCap = _builder.BuildMul(curCmdCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cmd_cap");
        var atLeast1K = _builder.BuildSelect(
            _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, doubleCmdCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 1024), "lt_1k"),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 1024),
            doubleCmdCap,
            "at_least_1k"
        );
        var finalCap = _builder.BuildSelect(
            _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, atLeast1K, neededTotal, "lt_needed"),
            neededTotal,
            atLeast1K,
            "final_cap"
        );
        _builder.BuildStore(finalCap, cmdCapSlotEc);

        var finalCap64 = _builder.BuildZExt(finalCap, _context.Int64Type, "final_cap64");
        var curCmdDataRaw = _builder.BuildLoad2(i8PtrType, cmdDataSlotEc, "cur_cmd_data");
        var newCmdDataRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curCmdDataRaw, finalCap64 }, "new_cmd_data");
        _builder.BuildStore(newCmdDataRaw, cmdDataSlotEc);
        _builder.BuildBr(ecExitBB);

        _builder.PositionAtEnd(ecExitBB);
        _builder.BuildRetVoid();

        // =========================================================================
        // Helper: world_cmd_spawn(ptr world) -> i32 entity_id
        // =========================================================================
        var cmdSpawnType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType }, false);
        var cmdSpawnFunc = _module.AddFunction("world_cmd_spawn", cmdSpawnType);
        var csEntryBB = cmdSpawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(csEntryBB);

        var wArgCs = cmdSpawnFunc.GetParam(0);
        var lockSlotCs = _builder.BuildStructGEP2(_worldStructType, wArgCs, 10, "cmd_lock_slot_cs");
        var lockPtrCs = _builder.BuildBitCast(lockSlotCs, i8PtrType, "lock_ptr_cs");
        _builder.BuildCall2(srwFuncType, srwAcquireFunc, new[] { lockPtrCs }, "");

        var eAllocCs = _builder.BuildCall2(allocEntType, allocEntFunc, new[] { wArgCs }, "e_alloc_cs");
        _builder.BuildCall2(ensureCapType, ensureCapFunc, new[] { wArgCs, LLVMValueRef.CreateConstInt(_context.Int32Type, 8) }, "");

        var cmdCountSlotCs = _builder.BuildStructGEP2(_worldStructType, wArgCs, 7, "cmd_cnt_slot_cs");
        var cmdDataSlotCs = _builder.BuildStructGEP2(_worldStructType, wArgCs, 9, "cmd_data_slot_cs");
        var curCntCs = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotCs, "cur_cnt_cs");
        var dataPtrCs = _builder.BuildLoad2(i8PtrType, cmdDataSlotCs, "data_ptr_cs");

        var curCntCs64 = _builder.BuildZExt(curCntCs, _context.Int64Type, "cur_cnt_cs64");
        var writePtrCs = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrCs, new[] { curCntCs64 }, "write_ptr_cs");
        var writePtrCsI32 = _builder.BuildBitCast(writePtrCs, i32PtrType, "write_ptr_cs_i32");

        var opSlotCs = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCsI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot_cs");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), opSlotCs); // OP 1: SPAWN

        var eSlotCs = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCsI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot_cs");
        _builder.BuildStore(eAllocCs, eSlotCs);

        var newCntCs = _builder.BuildAdd(curCntCs, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), "new_cnt_cs");
        _builder.BuildStore(newCntCs, cmdCountSlotCs);

        _builder.BuildCall2(srwFuncType, srwReleaseFunc, new[] { lockPtrCs }, "");
        _builder.BuildRet(eAllocCs);

        // =========================================================================
        // Helper: world_cmd_despawn(ptr world, i32 entity_id) -> void
        // =========================================================================
        var cmdDespawnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var cmdDespawnFunc = _module.AddFunction("world_cmd_despawn", cmdDespawnType);
        var cdEntryBB = cmdDespawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(cdEntryBB);

        var wArgCd = cmdDespawnFunc.GetParam(0);
        var eArgCd = cmdDespawnFunc.GetParam(1);

        var lockSlotCd = _builder.BuildStructGEP2(_worldStructType, wArgCd, 10, "cmd_lock_slot_cd");
        var lockPtrCd = _builder.BuildBitCast(lockSlotCd, i8PtrType, "lock_ptr_cd");
        _builder.BuildCall2(srwFuncType, srwAcquireFunc, new[] { lockPtrCd }, "");

        _builder.BuildCall2(ensureCapType, ensureCapFunc, new[] { wArgCd, LLVMValueRef.CreateConstInt(_context.Int32Type, 8) }, "");

        var cmdCountSlotCd = _builder.BuildStructGEP2(_worldStructType, wArgCd, 7, "cmd_cnt_slot_cd");
        var cmdDataSlotCd = _builder.BuildStructGEP2(_worldStructType, wArgCd, 9, "cmd_data_slot_cd");
        var curCntCd = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotCd, "cur_cnt_cd");
        var dataPtrCd = _builder.BuildLoad2(i8PtrType, cmdDataSlotCd, "data_ptr_cd");

        var curCntCd64 = _builder.BuildZExt(curCntCd, _context.Int64Type, "cur_cnt_cd64");
        var writePtrCd = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrCd, new[] { curCntCd64 }, "write_ptr_cd");
        var writePtrCdI32 = _builder.BuildBitCast(writePtrCd, i32PtrType, "write_ptr_cd_i32");

        var opSlotCd = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCdI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot_cd");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 2), opSlotCd); // OP 2: DESPAWN

        var eSlotCd = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCdI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot_cd");
        _builder.BuildStore(eArgCd, eSlotCd);

        var newCntCd = _builder.BuildAdd(curCntCd, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), "new_cnt_cd");
        _builder.BuildStore(newCntCd, cmdCountSlotCd);

        _builder.BuildCall2(srwFuncType, srwReleaseFunc, new[] { lockPtrCd }, "");
        _builder.BuildRetVoid();

        // =========================================================================
        // Helpers for each component: world_set_Comp / world_add_Comp / world_remove_Comp / world_has_Comp
        // =========================================================================
        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            var compSym = _typeChecker.Components[compName];
            var compStructType = _compStructTypes[compName];
            ulong compSize = _compSizes[compName];
            ulong compBit = 1UL << k;
            var compBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, compBit);

            // 1. world_set_Comp and world_add_Comp
            foreach (var prefix in new[] { "world_set_", "world_add_" })
            {
                var paramTypes = new List<LLVMTypeRef> { worldPtrType, _context.Int32Type };
                foreach (var f in compSym.Fields)
                {
                    paramTypes.Add(MapType(f.Type.Name));
                }

                var setFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
                var setFunc = _module.AddFunction($"{prefix}{compName}", setFuncType);
                var setEntryBB = setFunc.AppendBasicBlock("entry");
                _builder.PositionAtEnd(setEntryBB);

                var targetArchAlloca = _builder.BuildAlloca(_context.Int32Type, "target_arch");
                var targetRowAlloca = _builder.BuildAlloca(_context.Int32Type, "target_row");

                var worldParamSet = setFunc.GetParam(0);
                var eParam = setFunc.GetParam(1);

                var entArchSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 5, "ent_arch_slot_set");
                var entRowSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 6, "ent_row_slot_set");
                var tablesSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 2, "tables_slot_set");

                // Load entity current archetype & row
                var archArr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr");
                var entArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArr, new[] { eParam }, "ent_arch_slot");
                var curArchIdxRaw = _builder.BuildLoad2(_context.Int32Type, entArchSlot, "cur_arch_idx_raw");

                var isNegArch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdxRaw, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_neg_arch");
                var assignA0BB = setFunc.AppendBasicBlock("set_assign_a0");
                var contSetBB = setFunc.AppendBasicBlock("set_cont");
                _builder.BuildCondBr(isNegArch, assignA0BB, contSetBB);

                _builder.PositionAtEnd(assignA0BB);
                _builder.BuildCall2(assignA0Type, assignA0Func, new[] { worldParamSet, eParam }, "");
                _builder.BuildBr(contSetBB);

                _builder.PositionAtEnd(contSetBB);
                var curArchIdx = _builder.BuildLoad2(_context.Int32Type, entArchSlot, "cur_arch_idx");

                var rowArr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr");
                var entRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArr, new[] { eParam }, "ent_row_slot");
                var curRow = _builder.BuildLoad2(_context.Int32Type, entRowSlot, "cur_row");

                var tablesBaseSet = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_set");
                var curArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseSet, new[] { curArchIdx }, "cur_arch_ptr");

                var curMaskSlot = _builder.BuildStructGEP2(_archStructType, curArchPtr, 0, "cur_mask_slot");
                var curMask = _builder.BuildLoad2(_context.Int64Type, curMaskSlot, "cur_mask");

                var hasBit = _builder.BuildAnd(curMask, compBitVal, "has_bit");
                var alreadyHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, hasBit, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "already_has");

                var inPlaceBB = setFunc.AppendBasicBlock("in_place_update");
                var transBB = setFunc.AppendBasicBlock("transition");
                var storeFieldsBB = setFunc.AppendBasicBlock("store_fields");

                _builder.BuildCondBr(alreadyHas, inPlaceBB, transBB);

                // --- In place update ---
                _builder.PositionAtEnd(inPlaceBB);
                _builder.BuildStore(curArchIdx, targetArchAlloca);
                _builder.BuildStore(curRow, targetRowAlloca);
                _builder.BuildBr(storeFieldsBB);

                // --- Transition to new archetype ---
                _builder.PositionAtEnd(transBB);
                var newMask = _builder.BuildOr(curMask, compBitVal, "new_mask");
                var newArchIdx = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamSet, newMask }, "new_arch_idx");

                var tablesBaseTr1 = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_tr1");
                var newArchPtr1 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr1, new[] { newArchIdx }, "new_arch_ptr1");
                var newCntSlot1 = _builder.BuildStructGEP2(_archStructType, newArchPtr1, 1, "new_cnt_slot1");
                var newCnt1 = _builder.BuildLoad2(_context.Int32Type, newCntSlot1, "new_cnt1");
                var newCapSlot1 = _builder.BuildStructGEP2(_archStructType, newArchPtr1, 2, "new_cap_slot1");
                var newCap1 = _builder.BuildLoad2(_context.Int32Type, newCapSlot1, "new_cap1");
                var needGrowNew = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, newCnt1, newCap1, "need_grow_new");

                var growNewBB = setFunc.AppendBasicBlock("grow_new_arch");
                var afterGrowNewBB = setFunc.AppendBasicBlock("after_grow_new_arch");
                _builder.BuildCondBr(needGrowNew, growNewBB, afterGrowNewBB);

                _builder.PositionAtEnd(growNewBB);
                _builder.BuildCall2(growArchType, growArchFunc, new[] { worldParamSet, newArchIdx }, "");
                _builder.BuildBr(afterGrowNewBB);

                _builder.PositionAtEnd(afterGrowNewBB);
                // Reload pointers
                var tablesBaseTr2 = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_tr2");
                var curArchPtr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr2, new[] { curArchIdx }, "cur_arch_ptr2");
                var newArchPtr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr2, new[] { newArchIdx }, "new_arch_ptr2");

                var newCntSlot2 = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 1, "new_cnt_slot2");
                var newRow = _builder.BuildLoad2(_context.Int32Type, newCntSlot2, "new_row");
                var nextNewCnt = _builder.BuildAdd(newRow, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_new_cnt");
                _builder.BuildStore(nextNewCnt, newCntSlot2);

                // Store e into newArch.entities[newRow]
                var newEntSlot2 = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 3, "new_ent_slot2");
                var newEntRaw2 = _builder.BuildLoad2(i8PtrType, newEntSlot2, "new_ent_raw2");
                var newEntTyped2 = _builder.BuildBitCast(newEntRaw2, i32PtrType, "new_ent_typed2");
                var newEntElem2 = _builder.BuildInBoundsGEP2(_context.Int32Type, newEntTyped2, new[] { newRow }, "new_ent_elem2");
                _builder.BuildStore(eParam, newEntElem2);

                // Copy all existing components from curArch[curRow] to newArch[newRow]
                var curColsArr = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 4, "cur_cols_arr");
                var newColsArr = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 4, "new_cols_arr");

                for (int c = 0; c < totalComps; c++)
                {
                    var cName = compNames[c];
                    ulong cBit = 1UL << c;
                    ulong cSize = _compSizes[cName];
                    var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                    var cAnd = _builder.BuildAnd(curMask, cBitVal, $"has_{cName}");
                    var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_{cName}");

                    var copyCBB = setFunc.AppendBasicBlock($"copy_{cName}");
                    var skipCBB = setFunc.AppendBasicBlock($"skip_{cName}");
                    _builder.BuildCondBr(hasC, copyCBB, skipCBB);

                    _builder.PositionAtEnd(copyCBB);
                    var srcColSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"src_col_{cName}");
                    var srcColRaw = _builder.BuildLoad2(i8PtrType, srcColSlot, $"src_raw_{cName}");
                    var srcCompTyped = _builder.BuildBitCast(srcColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"src_typed_{cName}");
                    var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], srcCompTyped, new[] { curRow }, $"src_elem_{cName}");
                    var srcElemI8 = _builder.BuildBitCast(srcElem, i8PtrType, $"src_i8_{cName}");

                    var dstColSlot = _builder.BuildInBoundsGEP2(_colArrayType, newColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"dst_col_{cName}");
                    var dstColRaw = _builder.BuildLoad2(i8PtrType, dstColSlot, $"dst_raw_{cName}");
                    var dstCompTyped = _builder.BuildBitCast(dstColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"dst_typed_{cName}");
                    var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], dstCompTyped, new[] { newRow }, $"dst_elem_{cName}");
                    var dstElemI8 = _builder.BuildBitCast(dstElem, i8PtrType, $"dst_i8_{cName}");

                    _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstElemI8, srcElemI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                    _builder.BuildBr(skipCBB);

                    _builder.PositionAtEnd(skipCBB);
                }

                // Swap-remove from curArch
                var curCntSlot = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 1, "cur_cnt_slot");
                var curArchCount = _builder.BuildLoad2(_context.Int32Type, curCntSlot, "cur_arch_count");
                var lastRow = _builder.BuildSub(curArchCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "last_row");
                _builder.BuildStore(lastRow, curCntSlot);

                var isLastRow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curRow, lastRow, "is_last_row");
                var doSwapBB = setFunc.AppendBasicBlock("do_swap_remove");
                var afterSwapBB = setFunc.AppendBasicBlock("after_swap_remove");
                _builder.BuildCondBr(isLastRow, afterSwapBB, doSwapBB);

                _builder.PositionAtEnd(doSwapBB);
                // Move entity ID from lastRow to curRow
                var curEntSlotSr = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 3, "cur_ent_sr");
                var curEntRawSr = _builder.BuildLoad2(i8PtrType, curEntSlotSr, "cur_ent_raw_sr");
                var curEntTypedSr = _builder.BuildBitCast(curEntRawSr, i32PtrType, "cur_ent_typed_sr");
                var lastEntElem = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedSr, new[] { lastRow }, "last_ent_elem");
                var movedE = _builder.BuildLoad2(_context.Int32Type, lastEntElem, "moved_e");
                var curEntElemSr = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedSr, new[] { curRow }, "cur_ent_elem_sr");
                _builder.BuildStore(movedE, curEntElemSr);

                // Copy all components from lastRow to curRow
                for (int c = 0; c < totalComps; c++)
                {
                    var cName = compNames[c];
                    ulong cBit = 1UL << c;
                    ulong cSize = _compSizes[cName];
                    var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                    var cAnd = _builder.BuildAnd(curMask, cBitVal, $"has_sw_{cName}");
                    var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_sw_{cName}");

                    var swapCBB = setFunc.AppendBasicBlock($"swap_{cName}");
                    var skipSwCBB = setFunc.AppendBasicBlock($"skip_sw_{cName}");
                    _builder.BuildCondBr(hasC, swapCBB, skipSwCBB);

                    _builder.PositionAtEnd(swapCBB);
                    var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"sw_col_{cName}");
                    var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_raw_{cName}");
                    var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_typed_{cName}");
                    var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { lastRow }, $"sw_src_{cName}");
                    var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curRow }, $"sw_dst_{cName}");
                    var srcI8 = _builder.BuildBitCast(srcElem, i8PtrType, "sw_src_i8");
                    var dstI8 = _builder.BuildBitCast(dstElem, i8PtrType, "sw_dst_i8");
                    _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstI8, srcI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                    _builder.BuildBr(skipSwCBB);

                    _builder.PositionAtEnd(skipSwCBB);
                }

                // Update world.entity_row[movedE] = curRow
                var rowArrSr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_sr");
                var movedERowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSr, new[] { movedE }, "moved_e_row_slot");
                _builder.BuildStore(curRow, movedERowSlot);
                _builder.BuildBr(afterSwapBB);

                _builder.PositionAtEnd(afterSwapBB);
                // Update e's location
                var archArrTr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr_tr");
                var eArchSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrTr, new[] { eParam }, "e_arch_slot_tr");
                _builder.BuildStore(newArchIdx, eArchSlotTr);

                var rowArrTr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_tr");
                var eRowSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrTr, new[] { eParam }, "e_row_slot_tr");
                _builder.BuildStore(newRow, eRowSlotTr);

                _builder.BuildStore(newArchIdx, targetArchAlloca);
                _builder.BuildStore(newRow, targetRowAlloca);
                _builder.BuildBr(storeFieldsBB);

                // --- Store fields into targetArch.columns[k][targetRow] ---
                _builder.PositionAtEnd(storeFieldsBB);
                var finalArchIdx = _builder.BuildLoad2(_context.Int32Type, targetArchAlloca, "final_arch");
                var finalRow = _builder.BuildLoad2(_context.Int32Type, targetRowAlloca, "final_row");

                var latestTablesSf = _builder.BuildLoad2(archPtrType, tablesSlotSet, "latest_tables_sf");
                var finalArchPtr = _builder.BuildInBoundsGEP2(_archStructType, latestTablesSf, new[] { finalArchIdx }, "final_arch_ptr");
                var finalColsArr = _builder.BuildStructGEP2(_archStructType, finalArchPtr, 4, "final_cols");
                var finalColSlot = _builder.BuildInBoundsGEP2(_colArrayType, finalColsArr, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
                }, "final_col_slot");
                var finalColRaw = _builder.BuildLoad2(i8PtrType, finalColSlot, "final_col_raw");
                var finalColTyped = _builder.BuildBitCast(finalColRaw, LLVMTypeRef.CreatePointer(compStructType, 0), "final_col_typed");
                var finalElem = _builder.BuildInBoundsGEP2(compStructType, finalColTyped, new[] { finalRow }, "final_elem");

                for (int f = 0; f < compSym.Fields.Count; f++)
                {
                    var fVal = setFunc.GetParam((uint)(f + 2)); // Param 0 is world, Param 1 is e
                    var fGEP = _builder.BuildStructGEP2(compStructType, finalElem, (uint)f, $"{compSym.Fields[f].Name}_gep");
                    _builder.BuildStore(fVal, fGEP);
                }

                _builder.BuildRetVoid();
            }

            // 2. world_remove_Comp(ptr world, i32 e) -> void
            var remFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
            var remFunc = _module.AddFunction($"world_remove_{compName}", remFuncType);
            var remEntryBB = remFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(remEntryBB);

            var worldParamRem = remFunc.GetParam(0);
            var remE = remFunc.GetParam(1);

            var entArchSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 5, "ent_arch_slot_rem");
            var entRowSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 6, "ent_row_slot_rem");
            var tablesSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 2, "tables_slot_rem");

            var archArrRem = _builder.BuildLoad2(i32PtrType, entArchSlotRem, "arch_arr_rem");
            var remArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrRem, new[] { remE }, "rem_arch_slot");
            var curArchIdxRem = _builder.BuildLoad2(_context.Int32Type, remArchSlot, "cur_arch_rem");

            var rowArrRem = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem");
            var remRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRem, new[] { remE }, "rem_row_slot");
            var curRowRem = _builder.BuildLoad2(_context.Int32Type, remRowSlot, "cur_row_rem");

            var tablesBaseRem = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem");
            var curArchPtrRem = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRem, new[] { curArchIdxRem }, "cur_arch_ptr_rem");
            var curMaskSlotRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem, 0, "cur_mask_rem");
            var curMaskRem = _builder.BuildLoad2(_context.Int64Type, curMaskSlotRem, "cur_mask_val_rem");

            var hasCompRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(curMaskRem, compBitVal, "rem_has_bit"),
                LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "has_comp_rem");

            var doRemBB = remFunc.AppendBasicBlock("do_remove");
            var exitRemBB = remFunc.AppendBasicBlock("exit_remove");
            _builder.BuildCondBr(hasCompRem, doRemBB, exitRemBB);

            _builder.PositionAtEnd(doRemBB);
            var notBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, ~compBit);
            var newMaskRem = _builder.BuildAnd(curMaskRem, notBitVal, "new_mask_rem");
            var newArchIdxRem = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamRem, newMaskRem }, "new_arch_rem");

            var tablesBaseRemTr1 = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem_tr1");
            var newArchPtrRem1 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr1, new[] { newArchIdxRem }, "new_arch_ptr_rem1");
            var newCntSlotRem1 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem1, 1, "cnt_slot_rem1");
            var newCntRem1 = _builder.BuildLoad2(_context.Int32Type, newCntSlotRem1, "cnt_rem1");
            var newCapSlotRem1 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem1, 2, "cap_slot_rem1");
            var newCapRem1 = _builder.BuildLoad2(_context.Int32Type, newCapSlotRem1, "cap_rem1");
            var needGrowRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, newCntRem1, newCapRem1, "need_grow_rem");

            var growRemBB = remFunc.AppendBasicBlock("grow_rem_arch");
            var afterGrowRemBB = remFunc.AppendBasicBlock("after_grow_rem_arch");
            _builder.BuildCondBr(needGrowRem, growRemBB, afterGrowRemBB);

            _builder.PositionAtEnd(growRemBB);
            _builder.BuildCall2(growArchType, growArchFunc, new[] { worldParamRem, newArchIdxRem }, "");
            _builder.BuildBr(afterGrowRemBB);

            _builder.PositionAtEnd(afterGrowRemBB);
            var tablesBaseRemTr2 = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem_tr2");
            var curArchPtrRem2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr2, new[] { curArchIdxRem }, "cur_arch_ptr_rem2");
            var newArchPtrRem2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr2, new[] { newArchIdxRem }, "new_arch_ptr_rem2");

            var newCntSlotRem2 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 1, "cnt_slot_rem2");
            var newRowRem = _builder.BuildLoad2(_context.Int32Type, newCntSlotRem2, "new_row_rem");
            var nextNewCntRem = _builder.BuildAdd(newRowRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_cnt_rem");
            _builder.BuildStore(nextNewCntRem, newCntSlotRem2);

            // Store entity in newArch
            var newEntSlotRem2 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 3, "new_ent_rem");
            var newEntRawRem2 = _builder.BuildLoad2(i8PtrType, newEntSlotRem2, "new_ent_raw_rem");
            var newEntTypedRem2 = _builder.BuildBitCast(newEntRawRem2, i32PtrType, "new_ent_typed_rem");
            var newEntElemRem2 = _builder.BuildInBoundsGEP2(_context.Int32Type, newEntTypedRem2, new[] { newRowRem }, "new_ent_elem_rem");
            _builder.BuildStore(remE, newEntElemRem2);

            // Copy all components EXCEPT component k
            var curColsArrRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 4, "cur_cols_rem");
            var newColsArrRem = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 4, "new_cols_rem");

            for (int c = 0; c < totalComps; c++)
            {
                if (c == k) continue; // skip removed component

                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(curMaskRem, cBitVal, $"rem_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_rem_{cName}");

                var copyRemCBB = remFunc.AppendBasicBlock($"copy_rem_{cName}");
                var skipRemCBB = remFunc.AppendBasicBlock($"skip_rem_{cName}");
                _builder.BuildCondBr(hasC, copyRemCBB, skipRemCBB);

                _builder.PositionAtEnd(copyRemCBB);
                var srcColSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"rem_src_col_{cName}");
                var srcColRaw = _builder.BuildLoad2(i8PtrType, srcColSlot, $"rem_src_raw_{cName}");
                var srcCompTyped = _builder.BuildBitCast(srcColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"rem_src_typed_{cName}");
                var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], srcCompTyped, new[] { curRowRem }, $"rem_src_elem_{cName}");
                var srcElemI8 = _builder.BuildBitCast(srcElem, i8PtrType, "rem_src_i8");

                var dstColSlot = _builder.BuildInBoundsGEP2(_colArrayType, newColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"rem_dst_col_{cName}");
                var dstColRaw = _builder.BuildLoad2(i8PtrType, dstColSlot, $"rem_dst_raw_{cName}");
                var dstCompTyped = _builder.BuildBitCast(dstColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"rem_dst_typed_{cName}");
                var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], dstCompTyped, new[] { newRowRem }, $"rem_dst_elem_{cName}");
                var dstElemI8 = _builder.BuildBitCast(dstElem, i8PtrType, "rem_dst_i8");

                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstElemI8, srcElemI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                _builder.BuildBr(skipRemCBB);

                _builder.PositionAtEnd(skipRemCBB);
            }

            // Swap-remove from curArch
            var curCntSlotRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 1, "cnt_slot_rem");
            var curArchCountRem = _builder.BuildLoad2(_context.Int32Type, curCntSlotRem, "cnt_rem");
            var lastRowRem = _builder.BuildSub(curArchCountRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "last_row_rem");
            _builder.BuildStore(lastRowRem, curCntSlotRem);

            var isLastRowRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curRowRem, lastRowRem, "is_last_rem");
            var doSwapRemBB = remFunc.AppendBasicBlock("do_swap_rem");
            var afterSwapRemBB = remFunc.AppendBasicBlock("after_swap_rem");
            _builder.BuildCondBr(isLastRowRem, afterSwapRemBB, doSwapRemBB);

            _builder.PositionAtEnd(doSwapRemBB);
            var curEntSlotRemSr = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 3, "ent_sr_rem");
            var curEntRawRemSr = _builder.BuildLoad2(i8PtrType, curEntSlotRemSr, "ent_raw_sr_rem");
            var curEntTypedRemSr = _builder.BuildBitCast(curEntRawRemSr, i32PtrType, "ent_typed_sr_rem");
            var lastEntElemRem = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedRemSr, new[] { lastRowRem }, "last_ent_rem");
            var movedERem = _builder.BuildLoad2(_context.Int32Type, lastEntElemRem, "moved_e_rem");
            var curEntElemRemSr = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedRemSr, new[] { curRowRem }, "cur_ent_rem");
            _builder.BuildStore(movedERem, curEntElemRemSr);

            // Copy components of curArch from lastRow to curRow
            for (int c = 0; c < totalComps; c++)
            {
                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(curMaskRem, cBitVal, $"sw_rem_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_sw_rem_{cName}");

                var swapRemCBB = remFunc.AppendBasicBlock($"swap_rem_{cName}");
                var skipSwRemCBB = remFunc.AppendBasicBlock($"skip_sw_rem_{cName}");
                _builder.BuildCondBr(hasC, swapRemCBB, skipSwRemCBB);

                _builder.PositionAtEnd(swapRemCBB);
                var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"sw_col_rem_{cName}");
                var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_raw_rem_{cName}");
                var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_typed_rem_{cName}");
                var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { lastRowRem }, $"sw_src_rem_{cName}");
                var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curRowRem }, $"sw_dst_rem_{cName}");
                var srcI8 = _builder.BuildBitCast(srcElem, i8PtrType, "sw_src_rem_i8");
                var dstI8 = _builder.BuildBitCast(dstElem, i8PtrType, "sw_dst_rem_i8");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstI8, srcI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                _builder.BuildBr(skipSwRemCBB);

                _builder.PositionAtEnd(skipSwRemCBB);
            }

            var rowArrRemSr = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem_sr");
            var movedERowSlotRem = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRemSr, new[] { movedERem }, "moved_e_row_slot_rem");
            _builder.BuildStore(curRowRem, movedERowSlotRem);
            _builder.BuildBr(afterSwapRemBB);

            _builder.PositionAtEnd(afterSwapRemBB);
            // Update entity records
            var archArrRemTr = _builder.BuildLoad2(i32PtrType, entArchSlotRem, "arch_arr_rem_tr");
            var remArchSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrRemTr, new[] { remE }, "rem_arch_slot_tr");
            _builder.BuildStore(newArchIdxRem, remArchSlotTr);

            var rowArrRemTr = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem_tr");
            var remRowSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRemTr, new[] { remE }, "rem_row_slot_tr");
            _builder.BuildStore(newRowRem, remRowSlotTr);

            _builder.BuildBr(exitRemBB);

            _builder.PositionAtEnd(exitRemBB);
            _builder.BuildRetVoid();

            // 3. world_has_Comp(ptr world, i32 e) -> bool
            var hasFuncType = LLVMTypeRef.CreateFunction(_context.Int1Type, new[] { worldPtrType, _context.Int32Type }, false);
            var hasFunc = _module.AddFunction($"world_has_{compName}", hasFuncType);
            var hasEntryBB = hasFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(hasEntryBB);

            var worldParamHas = hasFunc.GetParam(0);
            var hasEParam = hasFunc.GetParam(1);

            var entArchSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 5, "ent_arch_slot_has");
            var tablesSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 2, "tables_slot_has");

            var archArrHas = _builder.BuildLoad2(i32PtrType, entArchSlotHas, "arch_arr_has");
            var hasArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrHas, new[] { hasEParam }, "has_arch_slot");
            var curArchIdxHas = _builder.BuildLoad2(_context.Int32Type, hasArchSlot, "cur_arch_idx_has");

            var isAliveHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curArchIdxHas, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_alive_has");
            var checkMaskBB = hasFunc.AppendBasicBlock("check_mask");
            var retFalseBB = hasFunc.AppendBasicBlock("ret_false");
            _builder.BuildCondBr(isAliveHas, checkMaskBB, retFalseBB);

            _builder.PositionAtEnd(checkMaskBB);
            var tablesBaseHas = _builder.BuildLoad2(archPtrType, tablesSlotHas, "tables_has");
            var archPtrHas = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseHas, new[] { curArchIdxHas }, "arch_ptr_has");
            var maskSlotHas = _builder.BuildStructGEP2(_archStructType, archPtrHas, 0, "mask_slot_has");
            var archMaskHas = _builder.BuildLoad2(_context.Int64Type, maskSlotHas, "arch_mask_has");

            var bitAndHas = _builder.BuildAnd(archMaskHas, compBitVal, "bit_and_has");
            var resultHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, bitAndHas, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "res_has");
            _builder.BuildRet(resultHas);

            _builder.PositionAtEnd(retFalseBB);
            _builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 0));

            // 4. world_cmd_set_Comp and world_cmd_add_Comp
            foreach (var prefix in new[] { "world_cmd_set_", "world_cmd_add_" })
            {
                var paramTypes = new List<LLVMTypeRef> { worldPtrType, _context.Int32Type };
                foreach (var f in compSym.Fields)
                {
                    paramTypes.Add(MapType(f.Type.Name));
                }

                var cmdSetFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
                var cmdSetFunc = _module.AddFunction($"{prefix}{compName}", cmdSetFuncType);
                var cmdSetBB = cmdSetFunc.AppendBasicBlock("entry");
                _builder.PositionAtEnd(cmdSetBB);

                var wArgCmd = cmdSetFunc.GetParam(0);
                var eArgCmd = cmdSetFunc.GetParam(1);

                var lockSlotCmd = _builder.BuildStructGEP2(_worldStructType, wArgCmd, 10, "cmd_lock_slot_cset");
                var lockPtrCmd = _builder.BuildBitCast(lockSlotCmd, i8PtrType, "lock_ptr_cset");
                _builder.BuildCall2(srwFuncType, srwAcquireFunc, new[] { lockPtrCmd }, "");

                ulong neededBytes = 12 + compSize;
                _builder.BuildCall2(ensureCapType, ensureCapFunc, new[] { wArgCmd, LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)neededBytes) }, "");

                var cmdCountSlotCSet = _builder.BuildStructGEP2(_worldStructType, wArgCmd, 7, "cmd_cnt_slot_cset");
                var cmdDataSlotCSet = _builder.BuildStructGEP2(_worldStructType, wArgCmd, 9, "cmd_data_slot_cset");
                var curCntCSet = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotCSet, "cur_cnt_cset");
                var dataPtrCSet = _builder.BuildLoad2(i8PtrType, cmdDataSlotCSet, "data_ptr_cset");

                var curCntCSet64 = _builder.BuildZExt(curCntCSet, _context.Int64Type, "cur_cnt_cset64");
                var writePtrCSet = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrCSet, new[] { curCntCSet64 }, "write_ptr_cset");
                var writePtrCSetI32 = _builder.BuildBitCast(writePtrCSet, i32PtrType, "write_ptr_cset_i32");

                var opSlotCSet = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCSetI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot_cset");
                _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 3), opSlotCSet); // OP 3: SET

                var eSlotCSet = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCSetI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot_cset");
                _builder.BuildStore(eArgCmd, eSlotCSet);

                var compIdSlotCSet = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCSetI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 2) }, "comp_id_slot_cset");
                _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k), compIdSlotCSet);

                var payloadRaw = _builder.BuildInBoundsGEP2(_context.Int8Type, writePtrCSet, new[] { LLVMValueRef.CreateConstInt(_context.Int64Type, 12) }, "payload_raw");
                var payloadTyped = _builder.BuildBitCast(payloadRaw, LLVMTypeRef.CreatePointer(compStructType, 0), "payload_typed");

                for (int f = 0; f < compSym.Fields.Count; f++)
                {
                    var fArg = cmdSetFunc.GetParam((uint)(2 + f));
                    var fSlot = _builder.BuildStructGEP2(compStructType, payloadTyped, (uint)f, $"f_slot_{f}");
                    _builder.BuildStore(fArg, fSlot);
                }

                var newCntCSet = _builder.BuildAdd(curCntCSet, LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)neededBytes), "new_cnt_cset");
                _builder.BuildStore(newCntCSet, cmdCountSlotCSet);

                _builder.BuildCall2(srwFuncType, srwReleaseFunc, new[] { lockPtrCmd }, "");
                _builder.BuildRetVoid();
            }

            // 5. world_cmd_remove_Comp
            {
                var cmdRemFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
                var cmdRemFunc = _module.AddFunction($"world_cmd_remove_{compName}", cmdRemFuncType);
                var cmdRemBB = cmdRemFunc.AppendBasicBlock("entry");
                _builder.PositionAtEnd(cmdRemBB);

                var wArgRem = cmdRemFunc.GetParam(0);
                var eArgRem = cmdRemFunc.GetParam(1);

                var lockSlotRem = _builder.BuildStructGEP2(_worldStructType, wArgRem, 10, "cmd_lock_slot_crem");
                var lockPtrRem = _builder.BuildBitCast(lockSlotRem, i8PtrType, "lock_ptr_crem");
                _builder.BuildCall2(srwFuncType, srwAcquireFunc, new[] { lockPtrRem }, "");

                _builder.BuildCall2(ensureCapType, ensureCapFunc, new[] { wArgRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 12) }, "");

                var cmdCountSlotCRem = _builder.BuildStructGEP2(_worldStructType, wArgRem, 7, "cmd_cnt_slot_crem");
                var cmdDataSlotCRem = _builder.BuildStructGEP2(_worldStructType, wArgRem, 9, "cmd_data_slot_crem");
                var curCntCRem = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotCRem, "cur_cnt_crem");
                var dataPtrCRem = _builder.BuildLoad2(i8PtrType, cmdDataSlotCRem, "data_ptr_crem");

                var curCntCRem64 = _builder.BuildZExt(curCntCRem, _context.Int64Type, "cur_cnt_crem64");
                var writePtrCRem = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrCRem, new[] { curCntCRem64 }, "write_ptr_crem");
                var writePtrCRemI32 = _builder.BuildBitCast(writePtrCRem, i32PtrType, "write_ptr_crem_i32");

                var opSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot_crem");
                _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 4), opSlotCRem); // OP 4: REMOVE

                var eSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot_crem");
                _builder.BuildStore(eArgRem, eSlotCRem);

                var compIdSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 2) }, "comp_id_slot_crem");
                _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k), compIdSlotCRem);

                var newCntCRem = _builder.BuildAdd(curCntCRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 12), "new_cnt_crem");
                _builder.BuildStore(newCntCRem, cmdCountSlotCRem);

                _builder.BuildCall2(srwFuncType, srwReleaseFunc, new[] { lockPtrRem }, "");
                _builder.BuildRetVoid();
            }
        }

        // =========================================================================
        // Helper: world_apply_commands(ptr world) -> void
        // =========================================================================
        var applyCmdType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
        var applyCmdFunc = _module.AddFunction("world_apply_commands", applyCmdType);
        var acEntryBB = applyCmdFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(acEntryBB);

        var wArgAc = applyCmdFunc.GetParam(0);
        var cmdCountSlotAc = _builder.BuildStructGEP2(_worldStructType, wArgAc, 7, "cmd_cnt_slot_ac");
        var cmdDataSlotAc = _builder.BuildStructGEP2(_worldStructType, wArgAc, 9, "cmd_data_slot_ac");

        var totalCmdBytes = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotAc, "total_cmd_bytes");
        var hasCmds = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, totalCmdBytes, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "has_cmds");

        var loopHeadBB = applyCmdFunc.AppendBasicBlock("ac_loop_head");
        var acExitBB = applyCmdFunc.AppendBasicBlock("ac_exit");
        _builder.BuildCondBr(hasCmds, loopHeadBB, acExitBB);

        _builder.PositionAtEnd(loopHeadBB);
        var offsetAlloca = _builder.BuildAlloca(_context.Int32Type, "ac_offset");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), offsetAlloca);

        var loopCondBB = applyCmdFunc.AppendBasicBlock("ac_loop_cond");
        var loopBodyBB = applyCmdFunc.AppendBasicBlock("ac_loop_body");
        _builder.BuildBr(loopCondBB);

        _builder.PositionAtEnd(loopCondBB);
        var curOffset = _builder.BuildLoad2(_context.Int32Type, offsetAlloca, "cur_offset");
        var hasMoreCmds = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curOffset, totalCmdBytes, "has_more_cmds");
        _builder.BuildCondBr(hasMoreCmds, loopBodyBB, acExitBB);

        _builder.PositionAtEnd(loopBodyBB);
        var dataPtrAc = _builder.BuildLoad2(i8PtrType, cmdDataSlotAc, "data_ptr_ac");
        var curOffset64 = _builder.BuildZExt(curOffset, _context.Int64Type, "cur_offset64");
        var curCmdPtr = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrAc, new[] { curOffset64 }, "cur_cmd_ptr");
        var curCmdPtrI32 = _builder.BuildBitCast(curCmdPtr, i32PtrType, "cur_cmd_ptr_i32");

        var opSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, curCmdPtrI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot");
        var opVal = _builder.BuildLoad2(_context.Int32Type, opSlot, "op_val");

        var eSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, curCmdPtrI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot");
        var eVal = _builder.BuildLoad2(_context.Int32Type, eSlot, "e_val");

        var opSpawnBB = applyCmdFunc.AppendBasicBlock("op_spawn");
        var opDespawnBB = applyCmdFunc.AppendBasicBlock("op_despawn");
        var opSetBB = applyCmdFunc.AppendBasicBlock("op_set");
        var opRemoveBB = applyCmdFunc.AppendBasicBlock("op_remove");
        var opDefaultBB = applyCmdFunc.AppendBasicBlock("op_default");

        var switchInst = _builder.BuildSwitch(opVal, opDefaultBB, 4);
        switchInst.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), opSpawnBB);
        switchInst.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, 2), opDespawnBB);
        switchInst.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, 3), opSetBB);
        switchInst.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, 4), opRemoveBB);

        // OP 1: SPAWN
        _builder.PositionAtEnd(opSpawnBB);
        _builder.BuildCall2(assignA0Type, assignA0Func, new[] { wArgAc, eVal }, "");
        var nextOff1 = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), "next_off_1");
        _builder.BuildStore(nextOff1, offsetAlloca);
        _builder.BuildBr(loopCondBB);

        // OP 2: DESPAWN
        _builder.PositionAtEnd(opDespawnBB);
        _builder.BuildCall2(despawnType, despawnFunc, new[] { wArgAc, eVal }, "");
        var nextOff2 = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), "next_off_2");
        _builder.BuildStore(nextOff2, offsetAlloca);
        _builder.BuildBr(loopCondBB);

        // OP 3: SET
        _builder.PositionAtEnd(opSetBB);
        var compIdSlotSet = _builder.BuildInBoundsGEP2(_context.Int32Type, curCmdPtrI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 2) }, "comp_id_slot_set");
        var compIdValSet = _builder.BuildLoad2(_context.Int32Type, compIdSlotSet, "comp_id_set");
        var payloadPtrSet = _builder.BuildInBoundsGEP2(_context.Int8Type, curCmdPtr, new[] { LLVMValueRef.CreateConstInt(_context.Int64Type, 12) }, "payload_ptr_set");

        var setDefBB = applyCmdFunc.AppendBasicBlock("set_def");
        var setSwitch = _builder.BuildSwitch(compIdValSet, setDefBB, (uint)Math.Max(1, totalComps));

        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            var compSym = _typeChecker.Components[compName];
            ulong compSize = _compSizes[compName];
            var compCaseBB = applyCmdFunc.AppendBasicBlock($"set_case_{compName}");
            setSwitch.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k), compCaseBB);

            _builder.PositionAtEnd(compCaseBB);
            var setFuncK = _module.GetNamedFunction($"world_set_{compName}");
            var setFuncKType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(setFuncK);
            var typedPayloadK = _builder.BuildBitCast(payloadPtrSet, LLVMTypeRef.CreatePointer(_compStructTypes[compName], 0), $"typed_payload_{compName}");

            var setArgs = new List<LLVMValueRef> { wArgAc, eVal };
            for (int f = 0; f < compSym.Fields.Count; f++)
            {
                var fSlot = _builder.BuildStructGEP2(_compStructTypes[compName], typedPayloadK, (uint)f, $"p_f_{f}");
                var fVal = _builder.BuildLoad2(MapType(compSym.Fields[f].Type.Name), fSlot, $"f_val_{f}");
                setArgs.Add(fVal);
            }
            _builder.BuildCall2(setFuncKType, setFuncK, setArgs.ToArray(), "");
            var nextOffSetK = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)(12 + compSize)), $"next_off_set_{compName}");
            _builder.BuildStore(nextOffSetK, offsetAlloca);
            _builder.BuildBr(loopCondBB);
        }

        _builder.PositionAtEnd(setDefBB);
        var nextOffSetDef = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 12), "next_off_set_def");
        _builder.BuildStore(nextOffSetDef, offsetAlloca);
        _builder.BuildBr(loopCondBB);

        // OP 4: REMOVE
        _builder.PositionAtEnd(opRemoveBB);
        var compIdSlotRem = _builder.BuildInBoundsGEP2(_context.Int32Type, curCmdPtrI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 2) }, "comp_id_slot_rem");
        var compIdValRem = _builder.BuildLoad2(_context.Int32Type, compIdSlotRem, "comp_id_rem");

        var remDefBB = applyCmdFunc.AppendBasicBlock("rem_def");
        var remSwitch = _builder.BuildSwitch(compIdValRem, remDefBB, (uint)Math.Max(1, totalComps));

        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            var remCaseBB = applyCmdFunc.AppendBasicBlock($"rem_case_{compName}");
            remSwitch.AddCase(LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k), remCaseBB);

            _builder.PositionAtEnd(remCaseBB);
            var remFuncK = _module.GetNamedFunction($"world_remove_{compName}");
            var remFuncKType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(remFuncK);
            _builder.BuildCall2(remFuncKType, remFuncK, new[] { wArgAc, eVal }, "");
            var nextOffRemK = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 12), $"next_off_rem_{compName}");
            _builder.BuildStore(nextOffRemK, offsetAlloca);
            _builder.BuildBr(loopCondBB);
        }

        _builder.PositionAtEnd(remDefBB);
        var nextOffRemDef = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 12), "next_off_rem_def");
        _builder.BuildStore(nextOffRemDef, offsetAlloca);
        _builder.BuildBr(loopCondBB);

        // Default Opcode Fallback
        _builder.PositionAtEnd(opDefaultBB);
        var nextOffDef = _builder.BuildAdd(curOffset, LLVMValueRef.CreateConstInt(_context.Int32Type, 4), "next_off_def");
        _builder.BuildStore(nextOffDef, offsetAlloca);
        _builder.BuildBr(loopCondBB);

        // acExitBB: reset cmd_count = 0 and return
        _builder.PositionAtEnd(acExitBB);
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), cmdCountSlotAc);
        _builder.BuildRetVoid();

        // =========================================================================
        // Helpers for resources: world_set_Time(ptr world, dt)
        // =========================================================================
        foreach (var (resName, resSym) in _typeChecker.Resources)
        {
            var structType = GetComponentStructType(resName);
            int resOff = _resourceWorldOffsets[resName];

            var paramTypes = new List<LLVMTypeRef> { worldPtrType };
            foreach (var f in resSym.Fields)
            {
                paramTypes.Add(MapType(f.Type.Name));
            }

            var setterType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
            var setterFunc = _module.AddFunction($"world_set_{resName}", setterType);
            var bb = setterFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(bb);

            var worldParamRes = setterFunc.GetParam(0);
            var resFieldSlot = _builder.BuildStructGEP2(_worldStructType, worldParamRes, (uint)resOff, $"res_{resName}_slot");

            for (int i = 0; i < resSym.Fields.Count; i++)
            {
                var fieldVal = setterFunc.GetParam((uint)(i + 1));
                var fieldGEP = _builder.BuildStructGEP2(structType, resFieldSlot, (uint)i, $"{resSym.Fields[i].Name}_gep");
                _builder.BuildStore(fieldVal, fieldGEP);
            }
            _builder.BuildRetVoid();
        }

        // =========================================================================
        // world_sort_hierarchy(ptr world): sort entities inside archetypes that have ChildOf
        // =========================================================================
        if (_compIds.TryGetValue("ChildOf", out int childOfId))
        {
            var sortType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
            var sortFunc = _module.AddFunction("world_sort_hierarchy", sortType);
            var sEntryBB = sortFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(sEntryBB);

            var worldParamSh = sortFunc.GetParam(0);
            var archCountSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 0, "arch_count_sh");
            var tablesSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 2, "tables_slot_sh");
            var entRowSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 6, "ent_row_slot_sh");

            ulong childOfBit = 1UL << childOfId;
            var childOfBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, childOfBit);

            var numArchs = _builder.BuildLoad2(_context.Int32Type, archCountSlotSh, "num_archs");
            var archIdxAlloca = _builder.BuildAlloca(_context.Int32Type, "arch_i");
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), archIdxAlloca);

            var initIAlloca = _builder.BuildAlloca(_context.Int32Type, "init_i");
            var sortIAlloca = _builder.BuildAlloca(_context.Int32Type, "sort_i");
            var sortJAlloca = _builder.BuildAlloca(_context.Int32Type, "sort_j");

            var tempAllocas = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
            foreach (var cName in compNames)
            {
                tempAllocas[cName] = _builder.BuildAlloca(_compStructTypes[cName], $"temp_{cName}");
            }

            var archLoopCondBB = sortFunc.AppendBasicBlock("arch_loop_cond");
            var archLoopBodyBB = sortFunc.AppendBasicBlock("arch_loop_body");
            var archLoopExitBB = sortFunc.AppendBasicBlock("arch_loop_exit");

            _builder.BuildBr(archLoopCondBB);

            _builder.PositionAtEnd(archLoopCondBB);
            var curAIdx = _builder.BuildLoad2(_context.Int32Type, archIdxAlloca, "cur_a_idx");
            var hasMoreArchs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curAIdx, numArchs, "has_more_archs");
            _builder.BuildCondBr(hasMoreArchs, archLoopBodyBB, archLoopExitBB);

            _builder.PositionAtEnd(archLoopBodyBB);
            var tBase = _builder.BuildLoad2(archPtrType, tablesSlotSh, "t_base");
            var curArch = _builder.BuildInBoundsGEP2(_archStructType, tBase, new[] { curAIdx }, "cur_a");
            var mSlot = _builder.BuildStructGEP2(_archStructType, curArch, 0, "m_slot");
            var mVal = _builder.BuildLoad2(_context.Int64Type, mSlot, "m_val");

            var hasChildOf = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(mVal, childOfBitVal, "and_co"),
                LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "has_co");

            var checkSortBB = sortFunc.AppendBasicBlock("check_sort");
            var nextArchBB = sortFunc.AppendBasicBlock("next_arch");

            _builder.BuildCondBr(hasChildOf, checkSortBB, nextArchBB);

            _builder.PositionAtEnd(checkSortBB);
            var cntSlotSh = _builder.BuildStructGEP2(_archStructType, curArch, 1, "cnt_slot_sh");
            var totalCountSh = _builder.BuildLoad2(_context.Int32Type, cntSlotSh, "cnt_sh");
            var canSort = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, totalCountSh, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "can_sort");

            var doSortBB = sortFunc.AppendBasicBlock("do_sort");
            _builder.BuildCondBr(canSort, doSortBB, nextArchBB);

            _builder.PositionAtEnd(doSortBB);
            var count64 = _builder.BuildZExt(totalCountSh, _context.Int64Type, "count64");
            var bytesNeeded = _builder.BuildMul(count64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "bytes_needed");
            var depthsRaw = _builder.BuildCall2(mallocType, mallocFunc, new[] { bytesNeeded }, "depths_raw");

            var colsSh = _builder.BuildStructGEP2(_archStructType, curArch, 4, "cols_sh");
            var coSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsSh, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)childOfId)
            }, "co_slot");
            var coColRaw = _builder.BuildLoad2(i8PtrType, coSlot, "co_col_raw");
            var coColTyped = _builder.BuildBitCast(coColRaw, LLVMTypeRef.CreatePointer(_compStructTypes["ChildOf"], 0), "co_col_typed");

            // Calculate depths (0 = root, 1 = child)
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), initIAlloca);

            var dInitCondBB = sortFunc.AppendBasicBlock("d_init_cond");
            var dInitBodyBB = sortFunc.AppendBasicBlock("d_init_body");
            var dInitExitBB = sortFunc.AppendBasicBlock("d_init_exit");

            _builder.BuildBr(dInitCondBB);

            _builder.PositionAtEnd(dInitCondBB);
            var curInitI = _builder.BuildLoad2(_context.Int32Type, initIAlloca, "cur_init_i");
            var hasInitMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curInitI, totalCountSh, "has_init_more");
            _builder.BuildCondBr(hasInitMore, dInitBodyBB, dInitExitBB);

            _builder.PositionAtEnd(dInitBodyBB);
            var childElem = _builder.BuildInBoundsGEP2(_compStructTypes["ChildOf"], coColTyped, new[] { curInitI }, "child_elem");
            var parentGEP = _builder.BuildStructGEP2(_compStructTypes["ChildOf"], childElem, 0, "parent_gep");
            var parentId = _builder.BuildLoad2(_context.Int32Type, parentGEP, "parent_id");
            var isRoot = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, parentId, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_root");
            var depthVal = _builder.BuildSelect(isRoot, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "depth_val");
            var depthsTyped = _builder.BuildBitCast(depthsRaw, i32PtrType, "depths_typed");
            var depthSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curInitI }, "depth_slot");
            _builder.BuildStore(depthVal, depthSlot);

            var nextInitI = _builder.BuildAdd(curInitI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_init_i");
            _builder.BuildStore(nextInitI, initIAlloca);
            _builder.BuildBr(dInitCondBB);

            _builder.PositionAtEnd(dInitExitBB);
            // Bubble sort rows by depth
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), sortIAlloca);

            var outerCondBB = sortFunc.AppendBasicBlock("sort_outer_cond");
            var outerBodyBB = sortFunc.AppendBasicBlock("sort_outer_body");
            var outerExitBB = sortFunc.AppendBasicBlock("sort_outer_exit");

            _builder.BuildBr(outerCondBB);

            _builder.PositionAtEnd(outerCondBB);
            var curSortI = _builder.BuildLoad2(_context.Int32Type, sortIAlloca, "cur_sort_i");
            var outerLimit = _builder.BuildSub(totalCountSh, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "outer_limit");
            var outerMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortI, outerLimit, "outer_more");
            _builder.BuildCondBr(outerMore, outerBodyBB, outerExitBB);

            _builder.PositionAtEnd(outerBodyBB);
            var innerStart = _builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "inner_start");
            _builder.BuildStore(innerStart, sortJAlloca);

            var innerCondBB = sortFunc.AppendBasicBlock("sort_inner_cond");
            var innerBodyBB = sortFunc.AppendBasicBlock("sort_inner_body");
            var innerStepBB = sortFunc.AppendBasicBlock("sort_inner_step");

            _builder.BuildBr(innerCondBB);

            _builder.PositionAtEnd(innerCondBB);
            var curSortJ = _builder.BuildLoad2(_context.Int32Type, sortJAlloca, "cur_sort_j");
            var innerMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortJ, totalCountSh, "inner_more");
            _builder.BuildCondBr(innerMore, innerBodyBB, innerStepBB);

            _builder.PositionAtEnd(innerBodyBB);
            var diSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curSortI }, "di_slot");
            var djSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curSortJ }, "dj_slot");
            var di = _builder.BuildLoad2(_context.Int32Type, diSlot, "di");
            var dj = _builder.BuildLoad2(_context.Int32Type, djSlot, "dj");

            var needSwap = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, di, dj, "need_swap");
            var doSwapRowBB = sortFunc.AppendBasicBlock("do_swap_row");
            var skipSwapRowBB = sortFunc.AppendBasicBlock("skip_swap_row");
            _builder.BuildCondBr(needSwap, doSwapRowBB, skipSwapRowBB);

            _builder.PositionAtEnd(doSwapRowBB);
            // Swap depths
            _builder.BuildStore(dj, diSlot);
            _builder.BuildStore(di, djSlot);

            // Swap entities
            var entSlotSh = _builder.BuildStructGEP2(_archStructType, curArch, 3, "ent_slot_sh");
            var entRawSh = _builder.BuildLoad2(i8PtrType, entSlotSh, "ent_raw_sh");
            var entTypedSh = _builder.BuildBitCast(entRawSh, i32PtrType, "ent_typed_sh");
            var eiSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, entTypedSh, new[] { curSortI }, "ei_slot");
            var ejSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, entTypedSh, new[] { curSortJ }, "ej_slot");
            var ei = _builder.BuildLoad2(_context.Int32Type, eiSlot, "ei");
            var ej = _builder.BuildLoad2(_context.Int32Type, ejSlot, "ej");
            _builder.BuildStore(ej, eiSlot);
            _builder.BuildStore(ei, ejSlot);

            // Update world.entity_row
            var rowArrSh = _builder.BuildLoad2(i32PtrType, entRowSlotSh, "row_arr_sh");
            var eiRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { ei }, "ei_row_slot");
            var ejRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { ej }, "ej_row_slot");
            _builder.BuildStore(curSortJ, eiRowSlot);
            _builder.BuildStore(curSortI, ejRowSlot);

            // Swap all component columns in this archetype
            for (int c = 0; c < totalComps; c++)
            {
                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(mVal, cBitVal, $"sw_co_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_sw_co_{cName}");

                var swShBB = sortFunc.AppendBasicBlock($"sw_sh_{cName}");
                var skipSwShBB = sortFunc.AppendBasicBlock($"skip_sw_sh_{cName}");
                _builder.BuildCondBr(hasC, swShBB, skipSwShBB);

                _builder.PositionAtEnd(swShBB);
                var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsSh, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"sw_sh_col_{cName}");
                var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_sh_raw_{cName}");
                var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_sh_typed_{cName}");
                var elemI = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curSortI }, $"elem_i_{cName}");
                var elemJ = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curSortJ }, $"elem_j_{cName}");

                // Temp alloca for swap
                var tempAlloca = tempAllocas[cName];
                var tempI8 = _builder.BuildBitCast(tempAlloca, i8PtrType, "temp_i8");
                var elemII8 = _builder.BuildBitCast(elemI, i8PtrType, "elem_i_i8");
                var elemJI8 = _builder.BuildBitCast(elemJ, i8PtrType, "elem_j_i8");
                var szVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cSize);

                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { tempI8, elemII8, szVal }, "");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { elemII8, elemJI8, szVal }, "");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { elemJI8, tempI8, szVal }, "");
                _builder.BuildBr(skipSwShBB);

                _builder.PositionAtEnd(skipSwShBB);
            }

            _builder.BuildBr(skipSwapRowBB);

            _builder.PositionAtEnd(skipSwapRowBB);
            var nextJ = _builder.BuildAdd(curSortJ, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_j");
            _builder.BuildStore(nextJ, sortJAlloca);
            _builder.BuildBr(innerCondBB);

            _builder.PositionAtEnd(innerStepBB);
            var nextSortI = _builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_sort_i");
            _builder.BuildStore(nextSortI, sortIAlloca);
            _builder.BuildBr(outerCondBB);

            _builder.PositionAtEnd(outerExitBB);
            _builder.BuildCall2(freeType, freeFunc, new[] { depthsRaw }, "");
            _builder.BuildBr(nextArchBB);

            _builder.PositionAtEnd(nextArchBB);
            var nextAIdx = _builder.BuildAdd(curAIdx, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_a_idx");
            _builder.BuildStore(nextAIdx, archIdxAlloca);
            _builder.BuildBr(archLoopCondBB);

            _builder.PositionAtEnd(archLoopExitBB);
            _builder.BuildRetVoid();
        }

        // =========================================================================
        // Event Runtime: world_emit_{Name}(ptr world, ...) and world_swap_events(ptr world)
        // =========================================================================
        foreach (var (evName, evSym) in _typeChecker.Events)
        {
            int evBaseOffset = GetEventWorldOffset(evName);
            ulong evSize = _eventSizes[evName];
            var evStructType = _compStructTypes[evName];
            var evPtrType = LLVMTypeRef.CreatePointer(evStructType, 0);

            var paramTypes = new List<LLVMTypeRef> { worldPtrType };
            foreach (var f in evSym.Fields)
            {
                paramTypes.Add(MapType(f.Type.Name));
            }

            var emitFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
            var emitFunc = _module.AddFunction($"world_emit_{evName}", emitFuncType);
            var wArg = emitFunc.GetParam(0);
            wArg.Name = "world";

            var evEntryBB = emitFunc.AppendBasicBlock("entry");
            var evGrowBB = emitFunc.AppendBasicBlock("grow");
            var evStoreBB = emitFunc.AppendBasicBlock("store_event");

            _builder.PositionAtEnd(evEntryBB);

            var wCountSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 3), "wcount_slot");
            var wCapSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 4), "wcap_slot");
            var wDataSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 5), "wdata_slot");

            var curWCount = _builder.BuildLoad2(_context.Int32Type, wCountSlot, "cur_wcount");
            var curWCap = _builder.BuildLoad2(_context.Int32Type, wCapSlot, "cur_wcap");

            var evNeedGrow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curWCount, curWCap, "need_grow");
            _builder.BuildCondBr(evNeedGrow, evGrowBB, evStoreBB);

            // grow block
            _builder.PositionAtEnd(evGrowBB);
            var evCapIsZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curWCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "cap_is_zero");
            var evDoubledCap = _builder.BuildMul(curWCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "doubled_cap");
            var evNewCap = _builder.BuildSelect(evCapIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 16), evDoubledCap, "new_cap");
            var evNewCap64 = _builder.BuildZExt(evNewCap, _context.Int64Type, "new_cap64");
            var evNewBytes = _builder.BuildMul(evNewCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, evSize), "new_bytes");

            var evOldDataRaw = _builder.BuildLoad2(i8PtrType, wDataSlot, "old_data");
            var evReallocCall = _builder.BuildCall2(reallocType, reallocFunc, new[] { evOldDataRaw, evNewBytes }, "new_data");
            _builder.BuildStore(evNewCap, wCapSlot);
            _builder.BuildStore(evReallocCall, wDataSlot);
            _builder.BuildBr(evStoreBB);

            // store_event block
            _builder.PositionAtEnd(evStoreBB);
            var finalDataRaw = _builder.BuildLoad2(i8PtrType, wDataSlot, "final_data_raw");
            var finalDataTyped = _builder.BuildBitCast(finalDataRaw, evPtrType, "final_data_typed");
            var elemSlot = _builder.BuildInBoundsGEP2(evStructType, finalDataTyped, new[] { curWCount }, "ev_elem_slot");

            for (int i = 0; i < evSym.Fields.Count; i++)
            {
                var fieldArg = emitFunc.GetParam((uint)(i + 1));
                var fieldSlot = _builder.BuildStructGEP2(evStructType, elemSlot, (uint)i, $"ev_f_{evSym.Fields[i].Name}");
                _builder.BuildStore(fieldArg, fieldSlot);
            }

            var nextWCount = _builder.BuildAdd(curWCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_wcount");
            _builder.BuildStore(nextWCount, wCountSlot);
            _builder.BuildRetVoid();
        }

        // world_swap_events(ptr world)
        var swapFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
        var swapFunc = _module.AddFunction("world_swap_events", swapFuncType);
        var swapWorldArg = swapFunc.GetParam(0);
        swapWorldArg.Name = "world";

        var swapEntryBB = swapFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(swapEntryBB);

        foreach (var (evName, _) in _typeChecker.Events)
        {
            int evBaseOffset = GetEventWorldOffset(evName);

            var rCountSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 0), $"{evName}_rcount_slot");
            var rCapSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 1), $"{evName}_rcap_slot");
            var rDataSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 2), $"{evName}_rdata_slot");

            var wCountSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 3), $"{evName}_wcount_slot");
            var wCapSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 4), $"{evName}_wcap_slot");
            var wDataSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 5), $"{evName}_wdata_slot");

            var wCount = _builder.BuildLoad2(_context.Int32Type, wCountSlot, $"{evName}_wcount");
            _builder.BuildStore(wCount, rCountSlot);
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), wCountSlot);

            // Swap pointers and caps
            var rCap = _builder.BuildLoad2(_context.Int32Type, rCapSlot, $"{evName}_rcap");
            var wCap = _builder.BuildLoad2(_context.Int32Type, wCapSlot, $"{evName}_wcap");
            _builder.BuildStore(wCap, rCapSlot);
            _builder.BuildStore(rCap, wCapSlot);

            var rData = _builder.BuildLoad2(i8PtrType, rDataSlot, $"{evName}_rdata");
            var wData = _builder.BuildLoad2(i8PtrType, wDataSlot, $"{evName}_wdata");
            _builder.BuildStore(wData, rDataSlot);
            _builder.BuildStore(rData, wDataSlot);
        }

        _builder.BuildRetVoid();
    }

}
