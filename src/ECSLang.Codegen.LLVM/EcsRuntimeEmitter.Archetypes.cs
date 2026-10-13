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

        var compNames = _orderedCompNames;
        int totalComps = compNames.Count;

        // =========================================================================
        // =========================================================================
        // Helper: world_grow_arch_index(ptr world) -> void (Step B4)
        // =========================================================================
        var growIdxType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
        var growIdxFunc = _module.AddFunction("world_grow_arch_index", growIdxType);
        var giEntryBB = growIdxFunc.AppendBasicBlock("entry");
        var giBuilder = _context.CreateBuilder();
        giBuilder.PositionAtEnd(giEntryBB);

        var giWorld = growIdxFunc.GetParam(0);
        var giCountSlot = giBuilder.BuildStructGEP2(_worldStructType, giWorld, 0, "gi_count_slot");
        var giTablesSlot = giBuilder.BuildStructGEP2(_worldStructType, giWorld, 2, "gi_tables_slot");
        var giEntriesSlot = giBuilder.BuildStructGEP2(_worldStructType, giWorld, (uint)_archIndexEntriesWorldOffset, "gi_entries_slot");
        var giCapSlot = giBuilder.BuildStructGEP2(_worldStructType, giWorld, (uint)_archIndexCapWorldOffset, "gi_cap_slot");

        var giCount = giBuilder.BuildLoad2(_context.Int32Type, giCountSlot, "gi_count");
        var giCap = giBuilder.BuildLoad2(_context.Int32Type, giCapSlot, "gi_cap");
        var giOldEntries = giBuilder.BuildLoad2(i32PtrType, giEntriesSlot, "gi_old_entries");
        var giTables = giBuilder.BuildLoad2(archPtrType, giTablesSlot, "gi_tables");

        var giIsZero = giBuilder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, giCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "gi_is_zero");
        var giDoubleCap = giBuilder.BuildMul(giCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "gi_double_cap");
        var giNewCap = giBuilder.BuildSelect(giIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 32), giDoubleCap, "gi_new_cap");

        var giNewCap64 = giBuilder.BuildZExt(giNewCap, _context.Int64Type, "gi_new_cap64");
        var giAllocBytes = giBuilder.BuildMul(giNewCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "gi_alloc_bytes");
        var giNewEntriesRaw = giBuilder.BuildCall2(mallocType, mallocFunc, new[] { giAllocBytes }, "gi_new_raw");
        giBuilder.BuildCall2(memsetType, memsetFunc, new[] { giNewEntriesRaw, LLVMValueRef.CreateConstInt(_context.Int32Type, 255), giAllocBytes }, "");
        var giNewEntries = giBuilder.BuildBitCast(giNewEntriesRaw, i32PtrType, "gi_new_entries");

        // Rehash existing archetypes if giCount > 0
        var giHasExisting = giBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, giCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "gi_has_existing");
        var giRehashCondBB = growIdxFunc.AppendBasicBlock("gi_rehash_cond");
        var giRehashBodyBB = growIdxFunc.AppendBasicBlock("gi_rehash_body");
        var giFinishBB = growIdxFunc.AppendBasicBlock("gi_finish");

        var giIAlloca = giBuilder.BuildAlloca(_context.Int32Type, "gi_i");
        giBuilder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), giIAlloca);

        giBuilder.BuildCondBr(giHasExisting, giRehashCondBB, giFinishBB);

        // Rehash loop: for (i = 0; i < giCount; i++)
        giBuilder.PositionAtEnd(giRehashCondBB);
        var giCurI = giBuilder.BuildLoad2(_context.Int32Type, giIAlloca, "gi_cur_i");
        var giMore = giBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, giCurI, giCount, "gi_more");
        giBuilder.BuildCondBr(giMore, giRehashBodyBB, giFinishBB);

        giBuilder.PositionAtEnd(giRehashBodyBB);
        var giArchElem = giBuilder.BuildInBoundsGEP2(_archStructType, giTables, new[] { giCurI }, "gi_arch_elem");
        var giMaskGEP = giBuilder.BuildStructGEP2(_archStructType, giArchElem, 0, "gi_mask_gep");

        // FNV hash of giMaskGEP
        var fnvPrime64 = LLVMValueRef.CreateConstInt(_context.Int64Type, 1099511628211UL);
        var hVal = LLVMValueRef.CreateConstInt(_context.Int64Type, 14695981039346656037UL);
        for (int w = 0; w < _maskWords; w++)
        {
            var wPtr = giBuilder.BuildInBoundsGEP2(_maskArrayType, giMaskGEP, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"gi_wptr_{w}");
            var wVal = giBuilder.BuildLoad2(_context.Int64Type, wPtr, $"gi_wval_{w}");
            hVal = giBuilder.BuildXor(hVal, wVal, $"gi_hxor_{w}");
            hVal = giBuilder.BuildMul(hVal, fnvPrime64, $"gi_hmul_{w}");
            var hShift = giBuilder.BuildLShr(hVal, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), $"gi_hshift_{w}");
            hVal = giBuilder.BuildXor(hVal, hShift, $"gi_hmix_{w}");
        }
        var giH32 = giBuilder.BuildTrunc(hVal, _context.Int32Type, "gi_h32");
        var giNewMask = giBuilder.BuildSub(giNewCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "gi_new_mask");
        var giInitSlot = giBuilder.BuildAnd(giH32, giNewMask, "gi_init_slot");

        var giSlotAlloca = giBuilder.BuildAlloca(_context.Int32Type, "gi_slot");
        giBuilder.BuildStore(giInitSlot, giSlotAlloca);

        var giProbeBB = growIdxFunc.AppendBasicBlock("gi_probe");
        giBuilder.BuildBr(giProbeBB);

        giBuilder.PositionAtEnd(giProbeBB);
        var giCurSlot = giBuilder.BuildLoad2(_context.Int32Type, giSlotAlloca, "gi_cur_slot");
        var giSlotPtr = giBuilder.BuildInBoundsGEP2(_context.Int32Type, giNewEntries, new[] { giCurSlot }, "gi_slot_ptr");
        var giSlotVal = giBuilder.BuildLoad2(_context.Int32Type, giSlotPtr, "gi_slot_val");
        var giSlotEmpty = giBuilder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, giSlotVal, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1), true), "gi_slot_empty");

        var giSlotFoundBB = growIdxFunc.AppendBasicBlock("gi_slot_found");
        var giSlotNextBB = growIdxFunc.AppendBasicBlock("gi_slot_next");
        giBuilder.BuildCondBr(giSlotEmpty, giSlotFoundBB, giSlotNextBB);

        giBuilder.PositionAtEnd(giSlotNextBB);
        var giNextSlot = giBuilder.BuildAdd(giCurSlot, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "gi_next_slot");
        var giWrappedSlot = giBuilder.BuildAnd(giNextSlot, giNewMask, "gi_wrapped_slot");
        giBuilder.BuildStore(giWrappedSlot, giSlotAlloca);
        giBuilder.BuildBr(giProbeBB);

        giBuilder.PositionAtEnd(giSlotFoundBB);
        giBuilder.BuildStore(giCurI, giSlotPtr);
        var giNextI = giBuilder.BuildAdd(giCurI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "gi_next_i");
        giBuilder.BuildStore(giNextI, giIAlloca);
        giBuilder.BuildBr(giRehashCondBB);

        // Finish: free old buffer if not null, store new buffer and cap
        giBuilder.PositionAtEnd(giFinishBB);
        var giFreeOldBB = growIdxFunc.AppendBasicBlock("gi_free_old");
        var giDoneBB = growIdxFunc.AppendBasicBlock("gi_done");
        giBuilder.BuildCondBr(giIsZero, giDoneBB, giFreeOldBB);

        giBuilder.PositionAtEnd(giFreeOldBB);
        var giOldEntriesRaw = giBuilder.BuildBitCast(giOldEntries, i8PtrType, "gi_old_raw");
        giBuilder.BuildCall2(freeType, freeFunc, new[] { giOldEntriesRaw }, "");
        giBuilder.BuildBr(giDoneBB);

        giBuilder.PositionAtEnd(giDoneBB);
        giBuilder.BuildStore(giNewEntries, giEntriesSlot);
        giBuilder.BuildStore(giNewCap, giCapSlot);
        giBuilder.BuildRetVoid();
        giBuilder.Dispose();

        // =========================================================================
        // Helper: world_get_or_create_archetype(ptr world, ptr mask) -> i32 arch_idx (Step B4)
        // =========================================================================
        var maskPtrType = LLVMTypeRef.CreatePointer(_maskArrayType, 0);
        var getArchType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType, maskPtrType }, false);
        var getArchFunc = _module.AddFunction("world_get_or_create_archetype", getArchType);
        var gEntryBB = getArchFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(gEntryBB);

        var worldParamG = getArchFunc.GetParam(0);
        var targetMask = getArchFunc.GetParam(1);

        var archCountSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 0, "arch_count_slot");
        var archCapSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 1, "arch_cap_slot");
        var archTablesSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 2, "arch_tables_slot");
        var entriesSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, (uint)_archIndexEntriesWorldOffset, "arch_entries_slot");
        var capSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, (uint)_archIndexCapWorldOffset, "arch_index_cap_slot");

        var curCap = _builder.BuildLoad2(_context.Int32Type, capSlot, "cur_cap");
        var isCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_cap_zero");
        var initCapBB = getArchFunc.AppendBasicBlock("init_cap");
        var hashBB = getArchFunc.AppendBasicBlock("hash_bb");

        _builder.BuildCondBr(isCapZero, initCapBB, hashBB);

        _builder.PositionAtEnd(initCapBB);
        _builder.BuildCall2(growIdxType, growIdxFunc, new[] { worldParamG }, "");
        _builder.BuildBr(hashBB);

        _builder.PositionAtEnd(hashBB);
        var curCapNow = _builder.BuildLoad2(_context.Int32Type, capSlot, "cur_cap_now");
        var curEntries = _builder.BuildLoad2(i32PtrType, entriesSlot, "cur_entries");

        // FNV hash of targetMask
        var fnvPrime64G = LLVMValueRef.CreateConstInt(_context.Int64Type, 1099511628211UL);
        var hValG = LLVMValueRef.CreateConstInt(_context.Int64Type, 14695981039346656037UL);
        for (int w = 0; w < _maskWords; w++)
        {
            var wPtr = _builder.BuildInBoundsGEP2(_maskArrayType, targetMask, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"tgt_wptr_{w}");
            var wVal = _builder.BuildLoad2(_context.Int64Type, wPtr, $"tgt_wval_{w}");
            hValG = _builder.BuildXor(hValG, wVal, $"tgt_hxor_{w}");
            hValG = _builder.BuildMul(hValG, fnvPrime64G, $"tgt_hmul_{w}");
            var hShift = _builder.BuildLShr(hValG, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), $"tgt_hshift_{w}");
            hValG = _builder.BuildXor(hValG, hShift, $"tgt_hmix_{w}");
        }
        var h32G = _builder.BuildTrunc(hValG, _context.Int32Type, "h32");
        var maskVal = _builder.BuildSub(curCapNow, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "mask_val");
        var initSlot = _builder.BuildAnd(h32G, maskVal, "init_slot");

        var slotAlloca = _builder.BuildAlloca(_context.Int32Type, "slot_alloca");
        _builder.BuildStore(initSlot, slotAlloca);

        var probeLoopBB = getArchFunc.AppendBasicBlock("probe_loop");
        var retFoundBB = getArchFunc.AppendBasicBlock("ret_found");
        var notFoundBB = getArchFunc.AppendBasicBlock("not_found");
        _builder.BuildBr(probeLoopBB);

        _builder.PositionAtEnd(probeLoopBB);
        var curSlot = _builder.BuildLoad2(_context.Int32Type, slotAlloca, "cur_slot");
        var slotPtr = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntries, new[] { curSlot }, "slot_ptr");
        var slotVal = _builder.BuildLoad2(_context.Int32Type, slotPtr, "slot_val");
        var isSlotEmpty = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotVal, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1), true), "is_slot_empty");

        var checkMatchBB = getArchFunc.AppendBasicBlock("check_match");
        var probeNextBB = getArchFunc.AppendBasicBlock("probe_next");
        _builder.BuildCondBr(isSlotEmpty, notFoundBB, checkMatchBB);

        _builder.PositionAtEnd(checkMatchBB);
        var tablesBase = _builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var candArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBase, new[] { slotVal }, "cand_arch");
        var candMaskGEP = _builder.BuildStructGEP2(_archStructType, candArchPtr, 0, "cand_mask");

        var exW0Ptr = _builder.BuildInBoundsGEP2(_maskArrayType, candMaskGEP, new[]
        {
            LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 0)
        }, "ex_w0");
        var exW0 = _builder.BuildLoad2(_context.Int64Type, exW0Ptr, "existing_mask");
        var tgtW0Ptr = _builder.BuildInBoundsGEP2(_maskArrayType, targetMask, new[]
        {
            LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
            LLVMValueRef.CreateConstInt(_context.Int32Type, 0)
        }, "tgt_w0");
        var tgtW0 = _builder.BuildLoad2(_context.Int64Type, tgtW0Ptr, "target_mask");
        var isMatch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, exW0, tgtW0, "is_match");

        for (int w = 1; w < _maskWords; w++)
        {
            var exWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, candMaskGEP, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"ex_w{w}");
            var exW = _builder.BuildLoad2(_context.Int64Type, exWPtr, $"existing_mask_{w}");
            var tgtWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, targetMask, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"tgt_w{w}");
            var tgtW = _builder.BuildLoad2(_context.Int64Type, tgtWPtr, $"target_mask_{w}");
            var wMatch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, exW, tgtW, $"is_match_{w}");
            isMatch = _builder.BuildAnd(isMatch, wMatch, $"is_match_comb_{w}");
        }
        _builder.BuildCondBr(isMatch, retFoundBB, probeNextBB);

        _builder.PositionAtEnd(probeNextBB);
        var nextSlot = _builder.BuildAdd(curSlot, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_slot");
        var wrappedSlot = _builder.BuildAnd(nextSlot, maskVal, "wrapped_slot");
        _builder.BuildStore(wrappedSlot, slotAlloca);
        _builder.BuildBr(probeLoopBB);

        _builder.PositionAtEnd(retFoundBB);
        _builder.BuildRet(slotVal);

        // Not found: allocate new archetype and insert into index
        _builder.PositionAtEnd(notFoundBB);
        var curCount = _builder.BuildLoad2(_context.Int32Type, archCountSlot, "cur_count");
        var curTablesCap = _builder.BuildLoad2(_context.Int32Type, archCapSlot, "cur_tables_cap");
        var needGrowTables = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount, curTablesCap, "need_grow_tables");
        var growTablesBB = getArchFunc.AppendBasicBlock("grow_tables");
        var checkIndexGrowBB = getArchFunc.AppendBasicBlock("check_index_grow");

        _builder.BuildCondBr(needGrowTables, growTablesBB, checkIndexGrowBB);

        _builder.PositionAtEnd(growTablesBB);
        var capIsZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curTablesCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "cap_zero");
        var doubleTablesCap = _builder.BuildMul(curTablesCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_tables_cap");
        var newTablesCap = _builder.BuildSelect(capIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), doubleTablesCap, "new_tables_cap");
        _builder.BuildStore(newTablesCap, archCapSlot);

        var newTablesCap64 = _builder.BuildZExt(newTablesCap, _context.Int64Type, "new_tables_cap64");
        var allocBytesTables = _builder.BuildMul(newTablesCap64, archStructSizeVal, "alloc_bytes_tables");
        var curTablesRaw = _builder.BuildLoad2(archPtrType, archTablesSlot, "cur_tables_raw");
        var curTablesI8 = _builder.BuildBitCast(curTablesRaw, i8PtrType, "cur_tables_i8");
        var reallocCall = _builder.BuildCall2(reallocType, reallocFunc, new[] { curTablesI8, allocBytesTables }, "new_tables_i8");
        var newTablesTyped = _builder.BuildBitCast(reallocCall, archPtrType, "new_tables_typed");
        _builder.BuildStore(newTablesTyped, archTablesSlot);
        _builder.BuildBr(checkIndexGrowBB);

        _builder.PositionAtEnd(checkIndexGrowBB);
        // Check load factor of hash index: if (curCount + 1) * 2 >= curCapNow -> grow index
        var nextCountVal = _builder.BuildAdd(curCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_cnt");
        var doubleNextCount = _builder.BuildMul(nextCountVal, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "dbl_next_cnt");
        var needGrowIndex = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, doubleNextCount, curCapNow, "need_grow_index");
        var growIndexBB = getArchFunc.AppendBasicBlock("grow_index");
        var insertBB = getArchFunc.AppendBasicBlock("insert_bb");

        _builder.BuildCondBr(needGrowIndex, growIndexBB, insertBB);

        _builder.PositionAtEnd(growIndexBB);
        _builder.BuildCall2(growIdxType, growIdxFunc, new[] { worldParamG }, "");
        _builder.BuildBr(insertBB);

        _builder.PositionAtEnd(insertBB);
        var latestCap = _builder.BuildLoad2(_context.Int32Type, capSlot, "latest_cap");
        var latestEntries = _builder.BuildLoad2(i32PtrType, entriesSlot, "latest_entries");
        var latestMask = _builder.BuildSub(latestCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "latest_mask");
        var insSlotInit = _builder.BuildAnd(h32G, latestMask, "ins_slot_init");

        var insSlotAlloca = _builder.BuildAlloca(_context.Int32Type, "ins_slot_alloca");
        _builder.BuildStore(insSlotInit, insSlotAlloca);

        var findEmptyBB = getArchFunc.AppendBasicBlock("find_empty");
        var doInsertBB = getArchFunc.AppendBasicBlock("do_insert");
        _builder.BuildBr(findEmptyBB);

        _builder.PositionAtEnd(findEmptyBB);
        var sIns = _builder.BuildLoad2(_context.Int32Type, insSlotAlloca, "s_ins");
        var sInsPtr = _builder.BuildInBoundsGEP2(_context.Int32Type, latestEntries, new[] { sIns }, "s_ins_ptr");
        var sInsVal = _builder.BuildLoad2(_context.Int32Type, sInsPtr, "s_ins_val");
        var sIsEmpty = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, sInsVal, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1), true), "s_is_empty");

        var advInsBB = getArchFunc.AppendBasicBlock("adv_ins");
        _builder.BuildCondBr(sIsEmpty, doInsertBB, advInsBB);

        _builder.PositionAtEnd(advInsBB);
        var nextInsSlot = _builder.BuildAdd(sIns, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_ins_slot");
        var wrappedInsSlot = _builder.BuildAnd(nextInsSlot, latestMask, "wrapped_ins_slot");
        _builder.BuildStore(wrappedInsSlot, insSlotAlloca);
        _builder.BuildBr(findEmptyBB);

        _builder.PositionAtEnd(doInsertBB);
        var newIdx = curCount;
        _builder.BuildStore(newIdx, sInsPtr);

        var nextCountFinal = _builder.BuildAdd(curCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_count_final");
        _builder.BuildStore(nextCountFinal, archCountSlot);

        var latestTables = _builder.BuildLoad2(archPtrType, archTablesSlot, "latest_tables");
        var newArchElem = _builder.BuildInBoundsGEP2(_archStructType, latestTables, new[] { newIdx }, "new_arch_elem");

        // Set mask
        var newMaskGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 0, "m_gep");
        for (int w = 0; w < _maskWords; w++)
        {
            var srcWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, targetMask, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"src_w{w}");
            var srcVal = _builder.BuildLoad2(_context.Int64Type, srcWPtr, $"src_val{w}");
            var dstWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, newMaskGEP, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"dst_w{w}");
            _builder.BuildStore(srcVal, dstWPtr);
        }

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

        // Reallocate entities array (i64*): newArchCap * 8 bytes
        var entSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 3, "ent_slot");
        var curEntRaw = _builder.BuildLoad2(i8PtrType, entSlotGr, "cur_ent_raw");
        var entBytes = _builder.BuildMul(newArchCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 8), "ent_bytes");
        var newEntRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curEntRaw, entBytes }, "new_ent_raw");
        _builder.BuildStore(newEntRaw, entSlotGr);

        // Intrinsic llvm.cttz.i64 for bit scanning
        var cttzType = LLVMTypeRef.CreateFunction(_context.Int64Type, new[] { _context.Int64Type, _context.Int1Type }, false);
        var cttzFunc = _module.GetNamedFunction("llvm.cttz.i64");
        if (cttzFunc.Handle == IntPtr.Zero)
        {
            cttzFunc = _module.AddFunction("llvm.cttz.i64", cttzType);
        }

        // Reallocate active component columns via compact mask bit loop
        var maskSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 0, "mask_slot");
        var colsArrGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 4, "cols_arr");

        for (int w = 0; w < _maskWords; w++)
        {
            var maskWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, maskSlotGr, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"gr_mask_w{w}_ptr");
            var maskWVal = _builder.BuildLoad2(_context.Int64Type, maskWPtr, $"gr_mask_w{w}_val");

            var hasBits = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, maskWVal, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"gr_has_bits_w{w}");
            var wordLoopBB = growArchFunc.AppendBasicBlock($"gr_word_{w}_loop");
            var nextWordBB = growArchFunc.AppendBasicBlock($"gr_word_{w}_next");
            _builder.BuildCondBr(hasBits, wordLoopBB, nextWordBB);

            _builder.PositionAtEnd(wordLoopBB);
            var curMaskAlloca = _builder.BuildAlloca(_context.Int64Type, $"gr_cur_mask_alloca_w{w}");
            _builder.BuildStore(maskWVal, curMaskAlloca);

            var bitLoopBB = growArchFunc.AppendBasicBlock($"gr_w{w}_bit_loop");
            _builder.BuildBr(bitLoopBB);

            _builder.PositionAtEnd(bitLoopBB);
            var curMaskVal = _builder.BuildLoad2(_context.Int64Type, curMaskAlloca, "gr_cur_mask_val");

            // ctz to find lowest set bit index (0..63)
            var ctzVal = _builder.BuildCall2(cttzType, cttzFunc, new[]
            {
                curMaskVal,
                LLVMValueRef.CreateConstInt(_context.Int1Type, 1) // is_zero_undef = true
            }, "gr_ctz");
            var ctz32 = _builder.BuildTrunc(ctzVal, _context.Int32Type, "gr_tz32");
            var wOffset = LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)(w * 64));
            var compId = _builder.BuildAdd(wOffset, ctz32, "gr_comp_id");

            // Descriptor lookup: &_ecs_component_descriptors[compId].size
            var descSlot = _builder.BuildInBoundsGEP2(_compDescArrayType, _compDescriptorsGlobal, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                compId
            }, "gr_desc_slot");
            var sizeSlot = _builder.BuildStructGEP2(_compDescStructType, descSlot, 1, "gr_size_slot");
            var compSize = _builder.BuildLoad2(_context.Int64Type, sizeSlot, "gr_comp_size");

            // Reallocate column: cols[compId]
            var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsArrGr, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                compId
            }, "gr_col_slot");
            var curColRaw = _builder.BuildLoad2(i8PtrType, colSlot, "gr_cur_col");
            var colBytes = _builder.BuildMul(newArchCap64, compSize, "gr_col_bytes");
            var newColRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curColRaw, colBytes }, "gr_new_col");
            _builder.BuildStore(newColRaw, colSlot);

            // Clear lowest bit: curMask = curMask & (curMask - 1)
            var maskSub1 = _builder.BuildSub(curMaskVal, LLVMValueRef.CreateConstInt(_context.Int64Type, 1), "gr_mask_sub1");
            var nextMask = _builder.BuildAnd(curMaskVal, maskSub1, "gr_next_mask");
            _builder.BuildStore(nextMask, curMaskAlloca);

            var moreBits = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, nextMask, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "gr_more_bits");
            _builder.BuildCondBr(moreBits, bitLoopBB, nextWordBB);

            _builder.PositionAtEnd(nextWordBB);
        }

        _builder.BuildRetVoid();

        // =========================================================================
        // Helper: world_migrate_entity(ptr world, i32 oldArch, i32 newArch, i32 oldRow, i32 newRow) -> void
        // Copies all active components common to oldArch and newArch using descriptor table
        // =========================================================================
        var migrateFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[]
        {
            worldPtrType,        // 0: world
            _context.Int32Type,  // 1: oldArchIdx
            _context.Int32Type,  // 2: newArchIdx
            _context.Int32Type,  // 3: oldRow
            _context.Int32Type   // 4: newRow
        }, false);
        var migrateFunc = _module.AddFunction("world_migrate_entity", migrateFuncType);
        var mEntryBB = migrateFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(mEntryBB);

        var mWorld = migrateFunc.GetParam(0);
        var mOldArchIdx = migrateFunc.GetParam(1);
        var mNewArchIdx = migrateFunc.GetParam(2);
        var mOldRow = migrateFunc.GetParam(3);
        var mNewRow = migrateFunc.GetParam(4);

        var mOldRow64 = _builder.BuildZExt(mOldRow, _context.Int64Type, "m_old_row64");
        var mNewRow64 = _builder.BuildZExt(mNewRow, _context.Int64Type, "m_new_row64");

        var mTablesSlot = _builder.BuildStructGEP2(_worldStructType, mWorld, 2, "m_tables_slot");
        var mTablesBase = _builder.BuildLoad2(archPtrType, mTablesSlot, "m_tables_base");

        var mOldArchPtr = _builder.BuildInBoundsGEP2(_archStructType, mTablesBase, new[] { mOldArchIdx }, "m_old_arch");
        var mNewArchPtr = _builder.BuildInBoundsGEP2(_archStructType, mTablesBase, new[] { mNewArchIdx }, "m_new_arch");

        var mOldMaskSlot = _builder.BuildStructGEP2(_archStructType, mOldArchPtr, 0, "m_old_mask");
        var mNewMaskSlot = _builder.BuildStructGEP2(_archStructType, mNewArchPtr, 0, "m_new_mask");

        var mOldColsSlot = _builder.BuildStructGEP2(_archStructType, mOldArchPtr, 4, "m_old_cols");
        var mNewColsSlot = _builder.BuildStructGEP2(_archStructType, mNewArchPtr, 4, "m_new_cols");

        // Intrinsic llvm.cttz.i64 (already declared earlier in EmitWorldArchetypeFunctions)


        // Loop through each mask word
        for (int w = 0; w < _maskWords; w++)
        {
            var oldWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, mOldMaskSlot, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"m_old_w{w}");
            var oldWVal = _builder.BuildLoad2(_context.Int64Type, oldWPtr, $"m_old_wval{w}");

            var newWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, mNewMaskSlot, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
            }, $"m_new_w{w}");
            var newWVal = _builder.BuildLoad2(_context.Int64Type, newWPtr, $"m_new_wval{w}");

            var commonMask = _builder.BuildAnd(oldWVal, newWVal, $"m_common_w{w}");
            var hasBits = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, commonMask, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"m_has_bits_w{w}");

            var wordLoopBB = migrateFunc.AppendBasicBlock($"m_word_{w}_loop");
            var nextWordBB = migrateFunc.AppendBasicBlock($"m_word_{w}_next");
            _builder.BuildCondBr(hasBits, wordLoopBB, nextWordBB);

            _builder.PositionAtEnd(wordLoopBB);
            var curMaskAlloca = _builder.BuildAlloca(_context.Int64Type, $"cur_mask_alloca_w{w}");
            _builder.BuildStore(commonMask, curMaskAlloca);

            var bitLoopBB = migrateFunc.AppendBasicBlock($"m_w{w}_bit_loop");
            _builder.BuildBr(bitLoopBB);

            _builder.PositionAtEnd(bitLoopBB);
            var curMaskVal = _builder.BuildLoad2(_context.Int64Type, curMaskAlloca, "cur_mask_val");

            // ctz to find lowest set bit index (0..63)
            var ctzVal = _builder.BuildCall2(cttzType, cttzFunc, new[]
            {
                curMaskVal,
                LLVMValueRef.CreateConstInt(_context.Int1Type, 1) // is_zero_undef = true
            }, "ctz");
            var ctz32 = _builder.BuildTrunc(ctzVal, _context.Int32Type, "tz32");
            var wOffset = LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)(w * 64));
            var compId = _builder.BuildAdd(wOffset, ctz32, "comp_id");

            // Descriptor lookup: &_ecs_component_descriptors[compId]
            var descSlot = _builder.BuildInBoundsGEP2(_compDescArrayType, _compDescriptorsGlobal, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                compId
            }, "desc_slot");
            var sizeSlot = _builder.BuildStructGEP2(_compDescStructType, descSlot, 1, "size_slot");
            var compSize = _builder.BuildLoad2(_context.Int64Type, sizeSlot, "comp_size");

            // Columns lookup: oldCols[compId], newCols[compId]
            var oldColSlot = _builder.BuildInBoundsGEP2(_colArrayType, mOldColsSlot, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                compId
            }, "old_col_slot");
            var oldColPtr = _builder.BuildLoad2(i8PtrType, oldColSlot, "old_col_ptr");

            var newColSlot = _builder.BuildInBoundsGEP2(_colArrayType, mNewColsSlot, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                compId
            }, "new_col_slot");
            var newColPtr = _builder.BuildLoad2(i8PtrType, newColSlot, "new_col_ptr");

            // Byte offsets: row * compSize
            var srcOffset = _builder.BuildMul(mOldRow64, compSize, "src_off");
            var dstOffset = _builder.BuildMul(mNewRow64, compSize, "dst_off");

            var srcElem = _builder.BuildInBoundsGEP2(_context.Int8Type, oldColPtr, new[] { srcOffset }, "src_elem");
            var dstElem = _builder.BuildInBoundsGEP2(_context.Int8Type, newColPtr, new[] { dstOffset }, "dst_elem");

            _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstElem, srcElem, compSize }, "");

            // Clear lowest bit: curMask = curMask & (curMask - 1)
            var maskSub1 = _builder.BuildSub(curMaskVal, LLVMValueRef.CreateConstInt(_context.Int64Type, 1), "mask_sub1");
            var nextMask = _builder.BuildAnd(curMaskVal, maskSub1, "next_mask");
            _builder.BuildStore(nextMask, curMaskAlloca);

            var moreBits = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, nextMask, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "more_bits");
            _builder.BuildCondBr(moreBits, bitLoopBB, nextWordBB);

            _builder.PositionAtEnd(nextWordBB);
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

        // Initialize empty Archetype 0 in this world using global zero mask
        _builder.BuildCall2(getArchType, getArchFunc, new[] { typedWorld, _zeroMaskGlobal }, "a0_init");
        _builder.BuildRet(typedWorld);

        // =========================================================================
        // Synchronization: Win32 SRWLock or Portable Atomic Spinlock
        // =========================================================================
        var srwFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        LLVMValueRef srwAcquireFunc;
        LLVMValueRef srwReleaseFunc;

        if (_options.Target.IsWindows)
        {
            srwAcquireFunc = _module.GetNamedFunction("AcquireSRWLockExclusive");
            if (srwAcquireFunc.Handle == IntPtr.Zero)
                srwAcquireFunc = _module.AddFunction("AcquireSRWLockExclusive", srwFuncType);
            srwReleaseFunc = _module.GetNamedFunction("ReleaseSRWLockExclusive");
            if (srwReleaseFunc.Handle == IntPtr.Zero)
                srwReleaseFunc = _module.AddFunction("ReleaseSRWLockExclusive", srwFuncType);
        }
        else
        {
            srwAcquireFunc = _module.GetNamedFunction("ecs_spin_acquire");
            if (srwAcquireFunc.Handle == IntPtr.Zero)
            {
                srwAcquireFunc = _module.AddFunction("ecs_spin_acquire", srwFuncType);
                var entryBB = srwAcquireFunc.AppendBasicBlock("entry");
                var spinLoopBB = srwAcquireFunc.AppendBasicBlock("spin_loop");
                var spinBackoffBB = srwAcquireFunc.AppendBasicBlock("spin_backoff");
                var lockAcqBB = srwAcquireFunc.AppendBasicBlock("lock_acq");

                var spinBuilder = _context.CreateBuilder();
                spinBuilder.PositionAtEnd(entryBB);
                var lockPtrParam = srwAcquireFunc.GetParam(0);
                var lockI32Ptr = spinBuilder.BuildBitCast(lockPtrParam, LLVMTypeRef.CreatePointer(_context.Int32Type, 0), "lock_i32");
                spinBuilder.BuildBr(spinLoopBB);

                spinBuilder.PositionAtEnd(spinLoopBB);
                var cmpxchg = LlvmApi.BuildAtomicCmpXchg(
                    spinBuilder,
                    lockI32Ptr,
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 1),
                    LLVMAtomicOrdering.LLVMAtomicOrderingAcquire,
                    LLVMAtomicOrdering.LLVMAtomicOrderingMonotonic,
                    0);
                var success = spinBuilder.BuildExtractValue(cmpxchg, 1, "is_locked");
                spinBuilder.BuildCondBr(success, lockAcqBB, spinBackoffBB);

                spinBuilder.PositionAtEnd(spinBackoffBB);
                if (_options.Target.Architecture == CpuArchitecture.Arm64)
                {
                    var hintType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { _context.Int32Type }, false);
                    var hintFunc = _module.GetNamedFunction("llvm.aarch64.hint");
                    if (hintFunc.Handle == IntPtr.Zero)
                        hintFunc = _module.AddFunction("llvm.aarch64.hint", hintType);
                    spinBuilder.BuildCall2(hintType, hintFunc, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "");
                }
                else
                {
                    var pauseType = LLVMTypeRef.CreateFunction(_context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    var pauseFunc = _module.GetNamedFunction("llvm.x86.sse2.pause");
                    if (pauseFunc.Handle == IntPtr.Zero)
                        pauseFunc = _module.AddFunction("llvm.x86.sse2.pause", pauseType);
                    spinBuilder.BuildCall2(pauseType, pauseFunc, Array.Empty<LLVMValueRef>(), "");
                }
                spinBuilder.BuildBr(spinLoopBB);

                spinBuilder.PositionAtEnd(lockAcqBB);
                spinBuilder.BuildRetVoid();
            }

            srwReleaseFunc = _module.GetNamedFunction("ecs_spin_release");
            if (srwReleaseFunc.Handle == IntPtr.Zero)
            {
                srwReleaseFunc = _module.AddFunction("ecs_spin_release", srwFuncType);
                var entryBB = srwReleaseFunc.AppendBasicBlock("entry");
                var relBuilder = _context.CreateBuilder();
                relBuilder.PositionAtEnd(entryBB);
                var lockPtrParam = srwReleaseFunc.GetParam(0);
                var lockI32Ptr = relBuilder.BuildBitCast(lockPtrParam, LLVMTypeRef.CreatePointer(_context.Int32Type, 0), "lock_i32");
                LlvmApi.BuildAtomicRMW(
                    relBuilder,
                    LLVMAtomicRMWBinOp.LLVMAtomicRMWBinOpSub,
                    lockI32Ptr,
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 1),
                    LLVMAtomicOrdering.LLVMAtomicOrderingRelease,
                    0);
                relBuilder.BuildRetVoid();
            }
        }
        _srwFuncType = srwFuncType;
        _srwAcquireFunc = srwAcquireFunc;
        _srwReleaseFunc = srwReleaseFunc;

        // =========================================================================
        // Helper: rt_panic(ptr msg) -> void
        // =========================================================================
        var rtPanicType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        var rtPanicFunc = _module.GetNamedFunction("rt_panic");
        if (rtPanicFunc.Handle == IntPtr.Zero)
        {
            rtPanicFunc = _module.AddFunction("rt_panic", rtPanicType);
            rtPanicFunc.Linkage = LLVMLinkage.LLVMInternalLinkage;
            var panicEntryBB = rtPanicFunc.AppendBasicBlock("entry");
            var panicBuilder = _context.CreateBuilder();
            panicBuilder.PositionAtEnd(panicEntryBB);

            var putsFunc = _module.GetNamedFunction("puts");
            var putsType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { i8PtrType }, false);
            if (putsFunc.Handle == IntPtr.Zero)
                putsFunc = _module.AddFunction("puts", putsType);

            var exitFunc = _module.GetNamedFunction("exit");
            var exitType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { _context.Int32Type }, false);
            if (exitFunc.Handle == IntPtr.Zero)
                exitFunc = _module.AddFunction("exit", exitType);

            var msgParam = rtPanicFunc.GetParam(0);
            panicBuilder.BuildCall2(putsType, putsFunc, new[] { msgParam }, "");
            panicBuilder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "");
            panicBuilder.BuildUnreachable();
        }

        // =========================================================================
        // Helper: world_alloc_entity(ptr world) -> i64 entity_handle
        // Returns (u32 id) | ((u64)gen << 32). Recycles from free_entities stack if non-empty.
        // =========================================================================
        var allocEntType = LLVMTypeRef.CreateFunction(_context.Int64Type, new[] { worldPtrType }, false);
        var allocEntFunc = _module.AddFunction("world_alloc_entity", allocEntType);
        var alEntryBB = allocEntFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(alEntryBB);

        var worldParamAl = allocEntFunc.GetParam(0);
        var entCountSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 3, "ent_count_slot");
        var entCapSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 4, "ent_cap_slot");
        var entArchSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 5, "ent_arch_slot");
        var entRowSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, 6, "ent_row_slot");
        var entGenSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, (uint)_entityGenWorldOffset, "ent_gen_slot");
        var freeEntitiesSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, (uint)_freeEntitiesWorldOffset, "free_ents_slot");
        var freeCountSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, (uint)_freeCountWorldOffset, "free_cnt_slot");
        var freeCapSlotAl = _builder.BuildStructGEP2(_worldStructType, worldParamAl, (uint)_freeCapWorldOffset, "free_cap_slot");

        var curFreeCount = _builder.BuildLoad2(_context.Int32Type, freeCountSlotAl, "cur_free_cnt");
        var hasFreeSlot = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, curFreeCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "has_free_slot");

        var popFreeBB = allocEntFunc.AppendBasicBlock("pop_free");
        var growAndAllocBB = allocEntFunc.AppendBasicBlock("grow_and_alloc");
        _builder.BuildCondBr(hasFreeSlot, popFreeBB, growAndAllocBB);

        // Path 1: Pop slot from free_entities stack
        _builder.PositionAtEnd(popFreeBB);
        var poppedIdx = _builder.BuildSub(curFreeCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "popped_idx");
        _builder.BuildStore(poppedIdx, freeCountSlotAl);

        var freeArrPop = _builder.BuildLoad2(i32PtrType, freeEntitiesSlotAl, "free_arr_pop");
        var freeSlotPtr = _builder.BuildInBoundsGEP2(_context.Int32Type, freeArrPop, new[] { poppedIdx }, "free_slot_ptr");
        var recycledId = _builder.BuildLoad2(_context.Int32Type, freeSlotPtr, "recycled_id");

        // Mark recycled entity slot as pending (-1)
        var archArrPop = _builder.BuildLoad2(i32PtrType, entArchSlotAl, "arch_arr_pop");
        var archSlotPop = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrPop, new[] { recycledId }, "arch_slot_pop");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), archSlotPop);

        var rowArrPop = _builder.BuildLoad2(i32PtrType, entRowSlotAl, "row_arr_pop");
        var rowSlotPop = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrPop, new[] { recycledId }, "row_slot_pop");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), rowSlotPop);

        // Load current generation (already incremented upon despawn)
        var genArrPop = _builder.BuildLoad2(i32PtrType, entGenSlotAl, "gen_arr_pop");
        var genSlotPop = _builder.BuildInBoundsGEP2(_context.Int32Type, genArrPop, new[] { recycledId }, "gen_slot_pop");
        var curGenPop = _builder.BuildLoad2(_context.Int32Type, genSlotPop, "cur_gen_pop");

        // Combine recycledId and curGenPop into i64 handle
        var recycledId64 = _builder.BuildZExt(recycledId, _context.Int64Type, "recycled_id64");
        var curGenPop64 = _builder.BuildZExt(curGenPop, _context.Int64Type, "cur_gen_pop64");
        var genShiftedPop = _builder.BuildShl(curGenPop64, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), "gen_shifted_pop");
        var recycledHandle = _builder.BuildOr(recycledId64, genShiftedPop, "recycled_handle");
        _builder.BuildRet(recycledHandle);

        // Path 2: Free list empty - allocate fresh slot at total entity_count
        _builder.PositionAtEnd(growAndAllocBB);
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

        // Realloc entity_gen
        var curGenArrRaw = _builder.BuildLoad2(i32PtrType, entGenSlotAl, "cur_gen_arr");
        var curGenArrI8 = _builder.BuildBitCast(curGenArrRaw, i8PtrType, "cur_gen_i8");
        var newGenArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curGenArrI8, bytesForEnt }, "new_gen_i8");
        var newGenArrTyped = _builder.BuildBitCast(newGenArrI8, i32PtrType, "new_gen_typed");
        _builder.BuildStore(newGenArrTyped, entGenSlotAl);

        _builder.BuildBr(assignEntBB);

        _builder.PositionAtEnd(assignEntBB);
        var eAlloc = curEntCount;
        var nextEntCountVal = _builder.BuildAdd(curEntCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_ent_cnt");
        _builder.BuildStore(nextEntCountVal, entCountSlotAl);

        // Initialize newly allocated entity slot: arch = -1 (pending), row = -1, gen = 1 (initial generation)
        var curArchArrAlloc = _builder.BuildLoad2(i32PtrType, entArchSlotAl, "cur_arch_arr_alloc");
        var eArchSlotAlloc = _builder.BuildInBoundsGEP2(_context.Int32Type, curArchArrAlloc, new[] { eAlloc }, "e_arch_slot_alloc");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eArchSlotAlloc);

        var curRowArrAlloc = _builder.BuildLoad2(i32PtrType, entRowSlotAl, "cur_row_arr_alloc");
        var eRowSlotAlloc = _builder.BuildInBoundsGEP2(_context.Int32Type, curRowArrAlloc, new[] { eAlloc }, "e_row_slot_alloc");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), eRowSlotAlloc);

        var curGenArrAlloc = _builder.BuildLoad2(i32PtrType, entGenSlotAl, "cur_gen_arr_alloc");
        var eGenSlotAlloc = _builder.BuildInBoundsGEP2(_context.Int32Type, curGenArrAlloc, new[] { eAlloc }, "e_gen_slot_alloc");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), eGenSlotAlloc); // Initial generation = 1

        // Combine into i64 handle: (eAlloc) | (1UL << 32)
        var eAlloc64 = _builder.BuildZExt(eAlloc, _context.Int64Type, "e_alloc64");
        var initialGen64 = LLVMValueRef.CreateConstInt(_context.Int64Type, 1UL << 32);
        var freshHandle = _builder.BuildOr(eAlloc64, initialGen64, "fresh_handle");
        _builder.BuildRet(freshHandle);

        // =========================================================================
        // Helper: world_assign_a0(ptr world, i64 entity_handle) -> void
        // =========================================================================
        var assignA0Type = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int64Type }, false);
        var assignA0Func = _module.AddFunction("world_assign_a0", assignA0Type);
        var a0EntryBB = assignA0Func.AppendBasicBlock("entry");
        _builder.PositionAtEnd(a0EntryBB);

        var wArgA0 = assignA0Func.GetParam(0);
        var handleArgA0 = assignA0Func.GetParam(1);
        var eIdA0 = _builder.BuildTrunc(handleArgA0, _context.Int32Type, "e_id_a0");

        var archArrA0Slot = _builder.BuildStructGEP2(_worldStructType, wArgA0, 5, "arch_arr_a0_slot");
        var archArrA0 = _builder.BuildLoad2(i32PtrType, archArrA0Slot, "arch_arr_a0");
        var eArchSlotA0 = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrA0, new[] { eIdA0 }, "e_arch_slot_a0");
        var curArchValA0 = _builder.BuildLoad2(_context.Int32Type, eArchSlotA0, "cur_arch_val_a0");
        var isUnassigned = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchValA0, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-1)), "is_unassigned");

        var doAssignBB = assignA0Func.AppendBasicBlock("do_assign");
        var exitA0BB = assignA0Func.AppendBasicBlock("exit_a0");
        _builder.BuildCondBr(isUnassigned, doAssignBB, exitA0BB);

        _builder.PositionAtEnd(doAssignBB);
        var a0 = _builder.BuildCall2(getArchType, getArchFunc, new[] { wArgA0, _zeroMaskGlobal }, "a0");

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

        // a0.entities[row] = handleArgA0 (i64 handle)
        var entSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr2, 3, "ent_slot0");
        var entRaw0 = _builder.BuildLoad2(i8PtrType, entSlot0, "ent_raw0");
        var entTyped0 = _builder.BuildBitCast(entRaw0, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "ent_typed0");
        var entElem0 = _builder.BuildInBoundsGEP2(_context.Int64Type, entTyped0, new[] { row }, "ent_elem0");
        _builder.BuildStore(handleArgA0, entElem0);

        // world.entity_arch[eId] = a0
        _builder.BuildStore(a0, eArchSlotA0);

        // world.entity_row[eId] = row
        var rowArrA0Slot = _builder.BuildStructGEP2(_worldStructType, wArgA0, 6, "row_arr_a0_slot");
        var rowArrA0 = _builder.BuildLoad2(i32PtrType, rowArrA0Slot, "row_arr_a0");
        var eRowSlotA0 = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrA0, new[] { eIdA0 }, "e_row_slot_a0");
        _builder.BuildStore(row, eRowSlotA0);
        _builder.BuildBr(exitA0BB);

        _builder.PositionAtEnd(exitA0BB);
        _builder.BuildRetVoid();

        // =========================================================================
        // Helper: world_spawn(ptr world) -> i64 entity_handle
        // =========================================================================
        var spawnType = LLVMTypeRef.CreateFunction(_context.Int64Type, new[] { worldPtrType }, false);
        var spawnFunc = _module.AddFunction("world_spawn", spawnType);
        var spEntryBB = spawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(spEntryBB);

        var worldParamSp = spawnFunc.GetParam(0);
        var eSpawn = _builder.BuildCall2(allocEntType, allocEntFunc, new[] { worldParamSp }, "e_spawn");
        _builder.BuildCall2(assignA0Type, assignA0Func, new[] { worldParamSp, eSpawn }, "");
        _builder.BuildRet(eSpawn);

        // =========================================================================
        // Helper: world_despawn(ptr world, i64 entity_handle) -> void
        // Checks bounds, generation (ABA protection), liveness; recycles slot to free_entities
        // =========================================================================
        var despawnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int64Type }, false);
        var despawnFunc = _module.AddFunction("world_despawn", despawnType);
        var dsEntryBB = despawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(dsEntryBB);

        var wArgDs = despawnFunc.GetParam(0);
        var handleArgDs = despawnFunc.GetParam(1);

        // Unpack id and gen
        var eIdDs = _builder.BuildTrunc(handleArgDs, _context.Int32Type, "e_id_ds");
        var eGen64Ds = _builder.BuildLShr(handleArgDs, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), "e_gen64_ds");
        var eGenDs = _builder.BuildTrunc(eGen64Ds, _context.Int32Type, "e_gen_ds");

        var entCountSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 3, "ent_count_slot_ds");
        var totalEntsDs = _builder.BuildLoad2(_context.Int32Type, entCountSlotDs, "total_ents_ds");

        var eNonNegDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, eIdDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "e_non_neg_ds");
        var eInBoundsDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, eIdDs, totalEntsDs, "e_in_bounds_ds");
        var isValidIdDs = _builder.BuildAnd(eNonNegDs, eInBoundsDs, "is_valid_id_ds");

        var checkLivenessDsBB = despawnFunc.AppendBasicBlock("ds_check_liveness");
        var dsOobBB = despawnFunc.AppendBasicBlock("ds_oob_panic");
        _builder.BuildCondBr(isValidIdDs, checkLivenessDsBB, dsOobBB);

        _builder.PositionAtEnd(dsOobBB);
        var errOobDsStr = _builder.BuildGlobalStringPtr("[ECS Error] Attempted to despawn out of bounds entity.", "ecs_err_oob_ds");
        _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errOobDsStr }, "");
        _builder.BuildUnreachable();

        // Load entity current archetype & row
        _builder.PositionAtEnd(checkLivenessDsBB);
        var entArchSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 5, "ent_arch_slot_ds");
        var entRowSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 6, "ent_row_slot_ds");
        var tablesSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, 2, "tables_slot_ds");

        var archArrDs = _builder.BuildLoad2(i32PtrType, entArchSlotDs, "arch_arr_ds");
        var eArchSlotDsInst = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrDs, new[] { eIdDs }, "e_arch_slot_ds_inst");
        var curArchIdxDs = _builder.BuildLoad2(_context.Int32Type, eArchSlotDsInst, "cur_arch_idx_ds");

        var isNegArchDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdxDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_neg_arch_ds");
        var dsDeadErrBB = despawnFunc.AppendBasicBlock("ds_dead_panic");
        var checkGenBB = despawnFunc.AppendBasicBlock("ds_check_gen");
        _builder.BuildCondBr(isNegArchDs, dsDeadErrBB, checkGenBB);

        _builder.PositionAtEnd(dsDeadErrBB);
        var isDeadDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchIdxDs, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-2)), "is_dead_ds");
        var dsDeadRealBB = despawnFunc.AppendBasicBlock("ds_dead_real");
        var dsPendRealBB = despawnFunc.AppendBasicBlock("ds_pend_real");
        _builder.BuildCondBr(isDeadDs, dsDeadRealBB, dsPendRealBB);

        _builder.PositionAtEnd(dsDeadRealBB);
        var errDeadDsStr = _builder.BuildGlobalStringPtr("[ECS Error] Attempted to mutate despawned or dead entity with world_despawn.", "ecs_err_dead_ds");
        _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errDeadDsStr }, "");
        _builder.BuildUnreachable();

        _builder.PositionAtEnd(dsPendRealBB);
        var errPendDsStr = _builder.BuildGlobalStringPtr("[ECS Error] Attempted to mutate unassigned or pending entity with world_despawn.", "ecs_err_pend_ds");
        _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errPendDsStr }, "");
        _builder.BuildUnreachable();

        // Check generation matching (ABA prevention) on alive entity
        _builder.PositionAtEnd(checkGenBB);
        var entGenSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, (uint)_entityGenWorldOffset, "ent_gen_slot_ds");
        var genArrDs = _builder.BuildLoad2(i32PtrType, entGenSlotDs, "gen_arr_ds");
        var curGenSlotDs = _builder.BuildInBoundsGEP2(_context.Int32Type, genArrDs, new[] { eIdDs }, "cur_gen_slot_ds");
        var curGenDs = _builder.BuildLoad2(_context.Int32Type, curGenSlotDs, "cur_gen_ds");
        var isGenMatchDs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curGenDs, eGenDs, "is_gen_match_ds");

        var doDespawnBB = despawnFunc.AppendBasicBlock("do_despawn");
        var dsStaleBB = despawnFunc.AppendBasicBlock("ds_stale_panic");
        _builder.BuildCondBr(isGenMatchDs, doDespawnBB, dsStaleBB);

        _builder.PositionAtEnd(dsStaleBB);
        var errStaleDsStr = _builder.BuildGlobalStringPtr("[ECS Error] Attempted to access entity with stale generation (ABA prevented).", "ecs_err_stale_ds");
        _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errStaleDsStr }, "");
        _builder.BuildUnreachable();

        _builder.PositionAtEnd(doDespawnBB);
        var rowArrDs = _builder.BuildLoad2(i32PtrType, entRowSlotDs, "row_arr_ds");
        var eRowSlotDsInst = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrDs, new[] { eIdDs }, "e_row_slot_ds_inst");
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
        // Move entity handle from lastRow to curRow
        var curEntSlotDs = _builder.BuildStructGEP2(_archStructType, curArchPtrDs, 3, "cur_ent_slot_ds");
        var curEntRawDs = _builder.BuildLoad2(i8PtrType, curEntSlotDs, "cur_ent_raw_ds");
        var curEntTypedDs = _builder.BuildBitCast(curEntRawDs, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "cur_ent_typed_ds");
        var lastEntElemDs = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedDs, new[] { lastRowDs }, "last_ent_elem_ds");
        var movedHandleDs = _builder.BuildLoad2(_context.Int64Type, lastEntElemDs, "moved_handle_ds");
        var curEntElemDs = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedDs, new[] { curRowDs }, "cur_ent_elem_ds");
        _builder.BuildStore(movedHandleDs, curEntElemDs);

        // Copy all active components from lastRow to curRow via generic migration core
        _builder.BuildCall2(migrateFuncType, migrateFunc, new[] { wArgDs, curArchIdxDs, curArchIdxDs, lastRowDs, curRowDs }, "");

        // Update world.entity_row[movedId] = curRowDs
        var movedIdDs = _builder.BuildTrunc(movedHandleDs, _context.Int32Type, "moved_id_ds");
        var movedERowSlotDs = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrDs, new[] { movedIdDs }, "moved_e_row_slot_ds");
        _builder.BuildStore(curRowDs, movedERowSlotDs);
        _builder.BuildBr(afterSwapDsBB);

        _builder.PositionAtEnd(afterSwapDsBB);
        // Mark despawned: world.entity_arch[eId] = -2, world.entity_row[eId] = -2
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-2)), eArchSlotDsInst);
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-2)), eRowSlotDsInst);

        // Increment generation to invalidate stale handles
        var nextGenVal = _builder.BuildAdd(curGenDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_gen_val");
        _builder.BuildStore(nextGenVal, curGenSlotDs);

        // Push eIdDs to free_entities stack
        var freeEntitiesSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, (uint)_freeEntitiesWorldOffset, "free_ents_slot_ds");
        var freeCountSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, (uint)_freeCountWorldOffset, "free_cnt_slot_ds");
        var freeCapSlotDs = _builder.BuildStructGEP2(_worldStructType, wArgDs, (uint)_freeCapWorldOffset, "free_cap_slot_ds");

        var curFreeCntDs = _builder.BuildLoad2(_context.Int32Type, freeCountSlotDs, "cur_free_cnt_ds");
        var curFreeCapDs = _builder.BuildLoad2(_context.Int32Type, freeCapSlotDs, "cur_free_cap_ds");
        var needGrowFree = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curFreeCntDs, curFreeCapDs, "need_grow_free");

        var growFreeBB = despawnFunc.AppendBasicBlock("grow_free");
        var pushFreeBB = despawnFunc.AppendBasicBlock("push_free");
        _builder.BuildCondBr(needGrowFree, growFreeBB, pushFreeBB);

        _builder.PositionAtEnd(growFreeBB);
        var freeCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curFreeCapDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "free_cap_zero");
        var doubleFreeCap = _builder.BuildMul(curFreeCapDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_free_cap");
        var newFreeCap = _builder.BuildSelect(freeCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 64), doubleFreeCap, "new_free_cap");
        _builder.BuildStore(newFreeCap, freeCapSlotDs);

        var newFreeCap64 = _builder.BuildZExt(newFreeCap, _context.Int64Type, "new_free_cap64");
        var bytesForFree = _builder.BuildMul(newFreeCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "bytes_for_free");
        var curFreeArrRaw = _builder.BuildLoad2(i32PtrType, freeEntitiesSlotDs, "cur_free_arr_raw");
        var curFreeArrI8 = _builder.BuildBitCast(curFreeArrRaw, i8PtrType, "cur_free_arr_i8");
        var newFreeArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curFreeArrI8, bytesForFree }, "new_free_arr_i8");
        var newFreeArrTyped = _builder.BuildBitCast(newFreeArrI8, i32PtrType, "new_free_arr_typed");
        _builder.BuildStore(newFreeArrTyped, freeEntitiesSlotDs);
        _builder.BuildBr(pushFreeBB);

        _builder.PositionAtEnd(pushFreeBB);
        var freeArrNow = _builder.BuildLoad2(i32PtrType, freeEntitiesSlotDs, "free_arr_now");
        var pushSlotPtr = _builder.BuildInBoundsGEP2(_context.Int32Type, freeArrNow, new[] { curFreeCntDs }, "push_slot_ptr");
        _builder.BuildStore(eIdDs, pushSlotPtr);
        var nextFreeCnt = _builder.BuildAdd(curFreeCntDs, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_free_cnt");
        _builder.BuildStore(nextFreeCnt, freeCountSlotDs);

        _builder.BuildRetVoid();

        EmitCommandBufferCoreHelpers(
            worldPtrType,
            allocEntFunc,
            allocEntType,
            srwAcquireFunc,
            srwReleaseFunc,
            srwFuncType,
            reallocFunc,
            reallocType,
            i8PtrType,
            i32PtrType,
            out var ensureCapFunc,
            out var ensureCapType);

        // =========================================================================
        // Helpers for each component: world_set_Comp / world_add_Comp / world_remove_Comp / world_has_Comp
        // =========================================================================
        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            var compSym = _typeChecker.Components[compName];
            var compStructType = _compStructTypes[compName];
            ulong compSize = _compSizes[compName];
            int wordIdx = k >> 6;
            int bitIdx = k & 63;
            ulong compBit = 1UL << bitIdx;
            var compBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, compBit);

            // 1. world_set_Comp and world_add_Comp
            foreach (var prefix in new[] { "world_set_", "world_add_" })
            {
                var paramTypes = new List<LLVMTypeRef> { worldPtrType, _context.Int64Type };
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
                var handleParamSet = setFunc.GetParam(1);

                // Unpack handle: id and gen
                var eIdSet = _builder.BuildTrunc(handleParamSet, _context.Int32Type, "e_id_set");
                var eGen64Set = _builder.BuildLShr(handleParamSet, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), "e_gen64_set");
                var eGenSet = _builder.BuildTrunc(eGen64Set, _context.Int32Type, "e_gen_set");

                // Bounds guard: 0 <= id < world.entity_count
                var entCountSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 3, "ent_count_slot_set");
                var totalEntsSet = _builder.BuildLoad2(_context.Int32Type, entCountSlotSet, "total_ents_set");

                var eNonNegSet = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, eIdSet, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "e_non_neg_set");
                var eInBoundsSet = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, eIdSet, totalEntsSet, "e_in_bounds_set");
                var isValidIdSet = _builder.BuildAnd(eNonNegSet, eInBoundsSet, "is_valid_id_set");
                var checkLivenessSetBB = setFunc.AppendBasicBlock("check_liveness_set");
                var oobBB = setFunc.AppendBasicBlock("set_oob_error");
                _builder.BuildCondBr(isValidIdSet, checkLivenessSetBB, oobBB);

                _builder.PositionAtEnd(oobBB);
                var errOobStr = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate out of bounds entity with {prefix}{compName}.", $"ecs_err_oob_{prefix}{compName}");
                _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errOobStr }, "");
                _builder.BuildUnreachable();

                // Load entity current archetype & row
                _builder.PositionAtEnd(checkLivenessSetBB);
                var entArchSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 5, "ent_arch_slot_set");
                var entRowSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 6, "ent_row_slot_set");
                var tablesSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 2, "tables_slot_set");

                var archArr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr");
                var entArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArr, new[] { eIdSet }, "ent_arch_slot");
                var curArchIdxRaw = _builder.BuildLoad2(_context.Int32Type, entArchSlot, "cur_arch_idx_raw");

                var isNegArch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdxRaw, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_neg_arch");
                var errEntBB = setFunc.AppendBasicBlock("set_err_entity");
                var checkGenSetBB = setFunc.AppendBasicBlock("check_gen_set");
                _builder.BuildCondBr(isNegArch, errEntBB, checkGenSetBB);

                _builder.PositionAtEnd(errEntBB);
                var isDead = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchIdxRaw, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-2)), "is_dead");
                var deadEntBB = setFunc.AppendBasicBlock("set_dead_entity_error");
                var pendingEntBB = setFunc.AppendBasicBlock("set_pending_entity_error");
                _builder.BuildCondBr(isDead, deadEntBB, pendingEntBB);

                _builder.PositionAtEnd(deadEntBB);
                var errDeadStr = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate despawned or dead entity with {prefix}{compName}.", $"ecs_err_dead_{prefix}{compName}");
                _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errDeadStr }, "");
                _builder.BuildUnreachable();

                _builder.PositionAtEnd(pendingEntBB);
                var errPendStr = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate unassigned or pending entity with {prefix}{compName}.", $"ecs_err_pending_{prefix}{compName}");
                _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errPendStr }, "");
                _builder.BuildUnreachable();

                // Generation guard (ABA prevention) on alive entity
                _builder.PositionAtEnd(checkGenSetBB);
                var entGenSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, (uint)_entityGenWorldOffset, "ent_gen_slot_set");
                var genArrSet = _builder.BuildLoad2(i32PtrType, entGenSlotSet, "gen_arr_set");
                var curGenSlotSet = _builder.BuildInBoundsGEP2(_context.Int32Type, genArrSet, new[] { eIdSet }, "cur_gen_slot_set");
                var curGenSet = _builder.BuildLoad2(_context.Int32Type, curGenSlotSet, "cur_gen_set");
                var isGenMatchSet = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curGenSet, eGenSet, "is_gen_match_set");

                var contSetBB = setFunc.AppendBasicBlock("set_cont");
                var staleSetBB = setFunc.AppendBasicBlock("set_stale_error");
                _builder.BuildCondBr(isGenMatchSet, contSetBB, staleSetBB);

                _builder.PositionAtEnd(staleSetBB);
                var errStaleSetStr = _builder.BuildGlobalStringPtr("[ECS Error] Attempted to access entity with stale generation (ABA prevented).", $"ecs_err_stale_{prefix}{compName}");
                _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errStaleSetStr }, "");
                _builder.BuildUnreachable();

                _builder.PositionAtEnd(contSetBB);
                var curArchIdx = curArchIdxRaw;

                var rowArr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr");
                var entRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArr, new[] { eIdSet }, "ent_row_slot");
                var curRow = _builder.BuildLoad2(_context.Int32Type, entRowSlot, "cur_row");

                var tablesBaseSet = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_set");
                var curArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseSet, new[] { curArchIdx }, "cur_arch_ptr");

                var curMaskSlot = _builder.BuildStructGEP2(_archStructType, curArchPtr, 0, "cur_mask_slot");
                var curMaskWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, curMaskSlot, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)wordIdx)
                }, $"cur_mask_w{wordIdx}_ptr");
                var curMask = _builder.BuildLoad2(_context.Int64Type, curMaskWPtr, "cur_mask");

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
                var tempMaskSetAlloca = _builder.BuildAlloca(_maskArrayType, "temp_mask_set");
                for (int w = 0; w < _maskWords; w++)
                {
                    var srcW = _builder.BuildInBoundsGEP2(_maskArrayType, curMaskSlot, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
                    }, $"src_set_w{w}");
                    var valW = _builder.BuildLoad2(_context.Int64Type, srcW, $"val_set_w{w}");
                    var dstW = _builder.BuildInBoundsGEP2(_maskArrayType, tempMaskSetAlloca, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
                    }, $"dst_set_w{w}");
                    _builder.BuildStore(w == wordIdx ? newMask : valW, dstW);
                }
                var newArchIdx = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamSet, tempMaskSetAlloca }, "new_arch_idx");

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

                // Store handleParamSet into newArch.entities[newRow] (i64 handle)
                var newEntSlot2 = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 3, "new_ent_slot2");
                var newEntRaw2 = _builder.BuildLoad2(i8PtrType, newEntSlot2, "new_ent_raw2");
                var newEntTyped2 = _builder.BuildBitCast(newEntRaw2, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "new_ent_typed2");
                var newEntElem2 = _builder.BuildInBoundsGEP2(_context.Int64Type, newEntTyped2, new[] { newRow }, "new_ent_elem2");
                _builder.BuildStore(handleParamSet, newEntElem2);

                // Migrate all active components common to curArch and newArch via generic descriptor core
                _builder.BuildCall2(migrateFuncType, migrateFunc, new[] { worldParamSet, curArchIdx, newArchIdx, curRow, newRow }, "");

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
                // Move entity handle from lastRow to curRow
                var curEntSlotSr = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 3, "cur_ent_sr");
                var curEntRawSr = _builder.BuildLoad2(i8PtrType, curEntSlotSr, "cur_ent_raw_sr");
                var curEntTypedSr = _builder.BuildBitCast(curEntRawSr, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "cur_ent_typed_sr");
                var lastEntElem = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedSr, new[] { lastRow }, "last_ent_elem");
                var movedHandle = _builder.BuildLoad2(_context.Int64Type, lastEntElem, "moved_handle");
                var curEntElemSr = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedSr, new[] { curRow }, "cur_ent_elem_sr");
                _builder.BuildStore(movedHandle, curEntElemSr);

                // Copy all components from lastRow to curRow via generic migration core
                _builder.BuildCall2(migrateFuncType, migrateFunc, new[] { worldParamSet, curArchIdx, curArchIdx, lastRow, curRow }, "");

                // Update world.entity_row[movedId] = curRow
                var movedId = _builder.BuildTrunc(movedHandle, _context.Int32Type, "moved_id");
                var rowArrSr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_sr");
                var movedERowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSr, new[] { movedId }, "moved_e_row_slot");
                _builder.BuildStore(curRow, movedERowSlot);
                _builder.BuildBr(afterSwapBB);

                _builder.PositionAtEnd(afterSwapBB);
                // Update e's location
                var archArrTr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr_tr");
                var eArchSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrTr, new[] { eIdSet }, "e_arch_slot_tr");
                _builder.BuildStore(newArchIdx, eArchSlotTr);

                var rowArrTr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_tr");
                var eRowSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrTr, new[] { eIdSet }, "e_row_slot_tr");
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
                    var fVal = setFunc.GetParam((uint)(f + 2)); // Param 0 is world, Param 1 is handle
                    var fGEP = _builder.BuildStructGEP2(compStructType, finalElem, (uint)f, $"{compSym.Fields[f].Name}_gep");
                    _builder.BuildStore(fVal, fGEP);
                }

                _builder.BuildRetVoid();
            }

            // 2. world_remove_Comp(ptr world, i64 e) -> void
            var remFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int64Type }, false);
            var remFunc = _module.AddFunction($"world_remove_{compName}", remFuncType);
            var remEntryBB = remFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(remEntryBB);

            var worldParamRem = remFunc.GetParam(0);
            var remHandle = remFunc.GetParam(1);

            // Unpack handle
            var remE = _builder.BuildTrunc(remHandle, _context.Int32Type, "rem_id");
            var remGen64 = _builder.BuildLShr(remHandle, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), "rem_gen64");
            var remGen = _builder.BuildTrunc(remGen64, _context.Int32Type, "rem_gen");

            // Bounds guard: 0 <= id < world.entity_count
            var entCountSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 3, "ent_count_slot_rem");
            var totalEntsRem = _builder.BuildLoad2(_context.Int32Type, entCountSlotRem, "total_ents_rem");

            var eNonNegRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, remE, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "rem_non_neg");
            var eInBoundsRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, remE, totalEntsRem, "rem_in_bounds");
            var isValidIdRem = _builder.BuildAnd(eNonNegRem, eInBoundsRem, "rem_is_valid_id");

            var checkLivenessRemBB = remFunc.AppendBasicBlock("check_liveness_rem");
            var oobRemBB = remFunc.AppendBasicBlock("rem_oob_error");
            _builder.BuildCondBr(isValidIdRem, checkLivenessRemBB, oobRemBB);

            _builder.PositionAtEnd(oobRemBB);
            var errOobStrRem = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate out of bounds entity with world_remove_{compName}.", $"ecs_err_oob_rem_{compName}");
            _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errOobStrRem }, "");
            _builder.BuildUnreachable();

            // Load entity current archetype & row
            _builder.PositionAtEnd(checkLivenessRemBB);
            var entArchSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 5, "ent_arch_slot_rem");
            var entRowSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 6, "ent_row_slot_rem");
            var tablesSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 2, "tables_slot_rem");

            var archArrRem = _builder.BuildLoad2(i32PtrType, entArchSlotRem, "arch_arr_rem");
            var remArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrRem, new[] { remE }, "rem_arch_slot");
            var curArchIdxRem = _builder.BuildLoad2(_context.Int32Type, remArchSlot, "cur_arch_rem");

            var isNegArchRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curArchIdxRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_neg_arch_rem");
            var errRemBB = remFunc.AppendBasicBlock("rem_err_entity");
            var checkGenRemBB = remFunc.AppendBasicBlock("check_gen_rem");
            _builder.BuildCondBr(isNegArchRem, errRemBB, checkGenRemBB);

            _builder.PositionAtEnd(errRemBB);
            var isDeadRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchIdxRem, LLVMValueRef.CreateConstInt(_context.Int32Type, unchecked((ulong)-2)), "is_dead_rem");
            var deadRemBB = remFunc.AppendBasicBlock("rem_dead_entity_error");
            var pendingRemBB = remFunc.AppendBasicBlock("rem_pending_entity_error");
            _builder.BuildCondBr(isDeadRem, deadRemBB, pendingRemBB);

            _builder.PositionAtEnd(deadRemBB);
            var errDeadStrRem = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate despawned or dead entity with world_remove_{compName}.", $"ecs_err_dead_rem_{compName}");
            _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errDeadStrRem }, "");
            _builder.BuildUnreachable();

            _builder.PositionAtEnd(pendingRemBB);
            var errPendStrRem = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to mutate unassigned or pending entity with world_remove_{compName}.", $"ecs_err_pending_rem_{compName}");
            _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errPendStrRem }, "");
            _builder.BuildUnreachable();

            // Generation guard (ABA prevention) on alive entity
            _builder.PositionAtEnd(checkGenRemBB);
            var entGenSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, (uint)_entityGenWorldOffset, "ent_gen_slot_rem");
            var genArrRem = _builder.BuildLoad2(i32PtrType, entGenSlotRem, "gen_arr_rem");
            var curGenSlotRem = _builder.BuildInBoundsGEP2(_context.Int32Type, genArrRem, new[] { remE }, "cur_gen_slot_rem");
            var curGenRem = _builder.BuildLoad2(_context.Int32Type, curGenSlotRem, "cur_gen_rem");
            var isGenMatchRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curGenRem, remGen, "is_gen_match_rem");

            var contRemBB = remFunc.AppendBasicBlock("rem_cont");
            var staleRemBB = remFunc.AppendBasicBlock("rem_stale_error");
            _builder.BuildCondBr(isGenMatchRem, contRemBB, staleRemBB);

            _builder.PositionAtEnd(staleRemBB);
            var errStaleRemStr = _builder.BuildGlobalStringPtr($"[ECS Error] Attempted to access entity with stale generation (ABA prevented).", $"ecs_err_stale_rem_{compName}");
            _builder.BuildCall2(rtPanicType, rtPanicFunc, new[] { errStaleRemStr }, "");
            _builder.BuildUnreachable();

            _builder.PositionAtEnd(contRemBB);

            var rowArrRem = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem");
            var remRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRem, new[] { remE }, "rem_row_slot");
            var curRowRem = _builder.BuildLoad2(_context.Int32Type, remRowSlot, "cur_row_rem");

            var tablesBaseRem = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem");
            var curArchPtrRem = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRem, new[] { curArchIdxRem }, "cur_arch_ptr_rem");
            var curMaskSlotRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem, 0, "cur_mask_rem");
            var curMaskWRemPtr = _builder.BuildInBoundsGEP2(_maskArrayType, curMaskSlotRem, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)wordIdx)
            }, $"cur_mask_w{wordIdx}_rem_ptr");
            var curMaskRem = _builder.BuildLoad2(_context.Int64Type, curMaskWRemPtr, "cur_mask_val_rem");

            var hasCompRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(curMaskRem, compBitVal, "rem_has_bit"),
                LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "has_comp_rem");

            var doRemBB = remFunc.AppendBasicBlock("do_remove");
            var exitRemBB = remFunc.AppendBasicBlock("exit_remove");
            _builder.BuildCondBr(hasCompRem, doRemBB, exitRemBB);

            _builder.PositionAtEnd(doRemBB);
            var notBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, ~compBit);
            var newMaskRem = _builder.BuildAnd(curMaskRem, notBitVal, "new_mask_rem");
            var tempMaskRemAlloca = _builder.BuildAlloca(_maskArrayType, "temp_mask_rem");
            for (int w = 0; w < _maskWords; w++)
            {
                var srcW = _builder.BuildInBoundsGEP2(_maskArrayType, curMaskSlotRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
                }, $"src_rem_w{w}");
                var valW = _builder.BuildLoad2(_context.Int64Type, srcW, $"val_rem_w{w}");
                var dstW = _builder.BuildInBoundsGEP2(_maskArrayType, tempMaskRemAlloca, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)w)
                }, $"dst_rem_w{w}");
                _builder.BuildStore(w == wordIdx ? newMaskRem : valW, dstW);
            }
            var newArchIdxRem = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamRem, tempMaskRemAlloca }, "new_arch_rem");

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

            // Store entity in newArch (i64 handle)
            var newEntSlotRem2 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 3, "new_ent_rem");
            var newEntRawRem2 = _builder.BuildLoad2(i8PtrType, newEntSlotRem2, "new_ent_raw_rem");
            var newEntTypedRem2 = _builder.BuildBitCast(newEntRawRem2, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "new_ent_typed_rem");
            var newEntElemRem2 = _builder.BuildInBoundsGEP2(_context.Int64Type, newEntTypedRem2, new[] { newRowRem }, "new_ent_elem_rem");
            _builder.BuildStore(remHandle, newEntElemRem2);

            // Migrate active components remaining in newArch via generic descriptor core
            _builder.BuildCall2(migrateFuncType, migrateFunc, new[] { worldParamRem, curArchIdxRem, newArchIdxRem, curRowRem, newRowRem }, "");

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
            var curEntTypedRemSr = _builder.BuildBitCast(curEntRawRemSr, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "ent_typed_sr_rem");
            var lastEntElemRem = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedRemSr, new[] { lastRowRem }, "last_ent_rem");
            var movedHandleRem = _builder.BuildLoad2(_context.Int64Type, lastEntElemRem, "moved_handle_rem");
            var curEntElemRemSr = _builder.BuildInBoundsGEP2(_context.Int64Type, curEntTypedRemSr, new[] { curRowRem }, "cur_ent_rem");
            _builder.BuildStore(movedHandleRem, curEntElemRemSr);

            // Copy components of curArch from lastRow to curRow via generic migration core
            _builder.BuildCall2(migrateFuncType, migrateFunc, new[] { worldParamRem, curArchIdxRem, curArchIdxRem, lastRowRem, curRowRem }, "");

            var movedIdRem = _builder.BuildTrunc(movedHandleRem, _context.Int32Type, "moved_id_rem");
            var rowArrRemSr = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem_sr");
            var movedERowSlotRem = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRemSr, new[] { movedIdRem }, "moved_e_row_slot_rem");
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

            // 3. world_has_Comp(ptr world, i64 e) -> bool
            // Queries return false on bounds error, stale generation, or dead entity (no panic)
            var hasFuncType = LLVMTypeRef.CreateFunction(_context.Int1Type, new[] { worldPtrType, _context.Int64Type }, false);
            var hasFunc = _module.AddFunction($"world_has_{compName}", hasFuncType);
            var hasEntryBB = hasFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(hasEntryBB);

            var worldParamHas = hasFunc.GetParam(0);
            var hasHandle = hasFunc.GetParam(1);

            // Unpack handle
            var hasEParam = _builder.BuildTrunc(hasHandle, _context.Int32Type, "has_id");
            var hasGen64 = _builder.BuildLShr(hasHandle, LLVMValueRef.CreateConstInt(_context.Int64Type, 32), "has_gen64");
            var hasGen = _builder.BuildTrunc(hasGen64, _context.Int32Type, "has_gen");

            var retFalseBB = hasFunc.AppendBasicBlock("ret_false");

            // Bounds guard: 0 <= id < world.entity_count -> else return false
            var entCountSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 3, "ent_count_slot_has");
            var totalEntsHas = _builder.BuildLoad2(_context.Int32Type, entCountSlotHas, "total_ents_has");

            var eNonNegHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, hasEParam, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "has_non_neg");
            var eInBoundsHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, hasEParam, totalEntsHas, "has_in_bounds");
            var isValidIdHas = _builder.BuildAnd(eNonNegHas, eInBoundsHas, "has_is_valid_id");

            var checkGenHasBB = hasFunc.AppendBasicBlock("check_gen_has");
            _builder.BuildCondBr(isValidIdHas, checkGenHasBB, retFalseBB);

            // Generation check: curGen == hasGen -> else return false
            _builder.PositionAtEnd(checkGenHasBB);
            var entGenSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, (uint)_entityGenWorldOffset, "ent_gen_slot_has");
            var genArrHas = _builder.BuildLoad2(i32PtrType, entGenSlotHas, "gen_arr_has");
            var curGenSlotHas = _builder.BuildInBoundsGEP2(_context.Int32Type, genArrHas, new[] { hasEParam }, "cur_gen_slot_has");
            var curGenHas = _builder.BuildLoad2(_context.Int32Type, curGenSlotHas, "cur_gen_has");
            var isGenMatchHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curGenHas, hasGen, "is_gen_match_has");

            var checkArchHasBB = hasFunc.AppendBasicBlock("check_arch_has");
            _builder.BuildCondBr(isGenMatchHas, checkArchHasBB, retFalseBB);

            // Liveness check: arch >= 0 -> else return false
            _builder.PositionAtEnd(checkArchHasBB);
            var entArchSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 5, "ent_arch_slot_has");
            var tablesSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 2, "tables_slot_has");

            var archArrHas = _builder.BuildLoad2(i32PtrType, entArchSlotHas, "arch_arr_has");
            var hasArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrHas, new[] { hasEParam }, "has_arch_slot");
            var curArchIdxHas = _builder.BuildLoad2(_context.Int32Type, hasArchSlot, "cur_arch_idx_has");

            var isAliveHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curArchIdxHas, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_alive_has");
            var checkMaskBB = hasFunc.AppendBasicBlock("check_mask");
            _builder.BuildCondBr(isAliveHas, checkMaskBB, retFalseBB);

            _builder.PositionAtEnd(checkMaskBB);
            var tablesBaseHas = _builder.BuildLoad2(archPtrType, tablesSlotHas, "tables_has");
            var archPtrHas = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseHas, new[] { curArchIdxHas }, "arch_ptr_has");
            var maskSlotHas = _builder.BuildStructGEP2(_archStructType, archPtrHas, 0, "mask_slot_has");
            var maskWHasPtr = _builder.BuildInBoundsGEP2(_maskArrayType, maskSlotHas, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)wordIdx)
            }, $"mask_w{wordIdx}_has_ptr");
            var archMaskHas = _builder.BuildLoad2(_context.Int64Type, maskWHasPtr, "arch_mask_has");

            var bitAndHas = _builder.BuildAnd(archMaskHas, compBitVal, "bit_and_has");
            var resultHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, bitAndHas, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "res_has");
            _builder.BuildRet(resultHas);

            _builder.PositionAtEnd(retFalseBB);
            _builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 0));

            EmitComponentCommandHelpers(
                worldPtrType,
                ensureCapFunc,
                ensureCapType,
                srwAcquireFunc,
                srwReleaseFunc,
                srwFuncType,
                i8PtrType,
                i32PtrType,
                k,
                compName,
                compSym,
                compStructType,
                compSize);
        }

        EmitCommandPlaybackHelper(
            worldPtrType,
            assignA0Func,
            assignA0Type,
            despawnFunc,
            despawnType,
            i8PtrType,
            i32PtrType,
            totalComps,
            compNames);

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

            int childOfWord = childOfId >> 6;
            int childOfBitIdx = childOfId & 63;
            ulong childOfBit = 1UL << childOfBitIdx;
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
            var childOfWordPtr = _builder.BuildInBoundsGEP2(_maskArrayType, mSlot, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)childOfWord)
            }, "childof_w_ptr");
            var childOfWordVal = _builder.BuildLoad2(_context.Int64Type, childOfWordPtr, "childof_w_val");

            var hasChildOf = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(childOfWordVal, childOfBitVal, "and_co"),
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
            var parentHandle = _builder.BuildLoad2(_context.Int64Type, parentGEP, "parent_handle");
            var isNullRoot = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, parentHandle, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "is_null_root");
            var isLegacyNegOne = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, parentHandle, LLVMValueRef.CreateConstInt(_context.Int64Type, unchecked((ulong)-1L)), "is_neg_one_root");
            var isRoot = _builder.BuildOr(isNullRoot, isLegacyNegOne, "is_root");
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

            // Swap entities (i64 handles)
            var entSlotSh = _builder.BuildStructGEP2(_archStructType, curArch, 3, "ent_slot_sh");
            var entRawSh = _builder.BuildLoad2(i8PtrType, entSlotSh, "ent_raw_sh");
            var entTypedSh = _builder.BuildBitCast(entRawSh, LLVMTypeRef.CreatePointer(_context.Int64Type, 0), "ent_typed_sh");
            var eiSlot = _builder.BuildInBoundsGEP2(_context.Int64Type, entTypedSh, new[] { curSortI }, "ei_slot");
            var ejSlot = _builder.BuildInBoundsGEP2(_context.Int64Type, entTypedSh, new[] { curSortJ }, "ej_slot");
            var eiHandle = _builder.BuildLoad2(_context.Int64Type, eiSlot, "ei_handle");
            var ejHandle = _builder.BuildLoad2(_context.Int64Type, ejSlot, "ej_handle");
            _builder.BuildStore(ejHandle, eiSlot);
            _builder.BuildStore(eiHandle, ejSlot);

            // Update world.entity_row for both entities using slot id = trunc(handle)
            var eiId = _builder.BuildTrunc(eiHandle, _context.Int32Type, "ei_id");
            var ejId = _builder.BuildTrunc(ejHandle, _context.Int32Type, "ej_id");
            var rowArrSh = _builder.BuildLoad2(i32PtrType, entRowSlotSh, "row_arr_sh");
            var eiRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { eiId }, "ei_row_slot");
            var ejRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { ejId }, "ej_row_slot");
            _builder.BuildStore(curSortJ, eiRowSlot);
            _builder.BuildStore(curSortI, ejRowSlot);

            // Swap all component columns in this archetype
            for (int c = 0; c < totalComps; c++)
            {
                var cName = compNames[c];
                int cWord = c >> 6;
                int cBitIdx = c & 63;
                ulong cBit = 1UL << cBitIdx;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var maskWPtr = _builder.BuildInBoundsGEP2(_maskArrayType, mSlot, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)cWord)
                }, $"mask_w_{cName}_sh");
                var maskWVal = _builder.BuildLoad2(_context.Int64Type, maskWPtr, $"cur_mask_{cName}_sh");

                var cAnd = _builder.BuildAnd(maskWVal, cBitVal, $"sw_co_has_{cName}");
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

        EmitEventRuntime(worldPtrType, reallocFunc, reallocType, i8PtrType, srwAcquireFunc, srwReleaseFunc, srwFuncType);
    }

}
