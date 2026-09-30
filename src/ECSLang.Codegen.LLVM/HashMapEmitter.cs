using System;
using System.Collections.Generic;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;
using ECSLang.Semantics;

namespace ECSLang.Codegen.LLVM;

/// <summary>
/// Emits high-performance Open-Addressing Hash Maps with Linear Probing and zero-overhead memory layouts.
/// Backs both user-facing Map&lt;K, V&gt; / HashMap&lt;K, V&gt; collections and World's entity name index.
/// </summary>
public sealed unsafe class HashMapEmitter
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMTargetDataRef _dataLayout;

    private readonly Dictionary<string, LLVMTypeRef> _entryStructTypes = new();
    private readonly Dictionary<string, LLVMTypeRef> _mapStructTypes = new();

    private readonly Dictionary<string, LLVMValueRef> _insertFuncs = new();
    private readonly Dictionary<string, LLVMValueRef> _getFuncs = new();
    private readonly Dictionary<string, LLVMValueRef> _containsFuncs = new();
    private readonly Dictionary<string, LLVMValueRef> _removeFuncs = new();
    private readonly Dictionary<string, LLVMValueRef> _clearFuncs = new();

    public HashMapEmitter(LLVMContextRef context, LLVMModuleRef module, LLVMTargetDataRef dataLayout)
    {
        _context = context;
        _module = module;
        _dataLayout = dataLayout;
    }

    public static string GetMapKey(string keyTypeName, string valTypeName) => $"Map<{keyTypeName}, {valTypeName}>";

    public LLVMTypeRef GetMapType(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_mapStructTypes.TryGetValue(mapKey, out var st)) return st;

        var entryType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var entryPtrType = LLVMTypeRef.CreatePointer(entryType, 0);

        // struct.Map<K, V>: { Entry* entries, i32 count, i32 capacity }
        var mapStruct = _context.CreateNamedStruct($"struct.Map.{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}");
        mapStruct.StructSetBody(new[]
        {
            entryPtrType,
            _context.Int32Type,
            _context.Int32Type
        }, false);

        _mapStructTypes[mapKey] = mapStruct;
        return mapStruct;
    }

    public LLVMTypeRef GetEntryType(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_entryStructTypes.TryGetValue(mapKey, out var st)) return st;

        var kType = mapTypeResolver(keyTypeName);
        var vType = mapTypeResolver(valTypeName);

        // struct.Entry: { K key, V val, i32 state }
        // state: 0 = Empty, 1 = Occupied, 2 = Tombstone (Deleted)
        var entryStruct = _context.CreateNamedStruct($"struct.Entry.{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}");
        entryStruct.StructSetBody(new[]
        {
            kType,
            vType,
            _context.Int32Type
        }, false);

        _entryStructTypes[mapKey] = entryStruct;
        return entryStruct;
    }

    public LLVMValueRef GetOrCreateInsert(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_insertFuncs.TryGetValue(mapKey, out var f)) return f;

        var mapStructType = GetMapType(keyTypeName, valTypeName, mapTypeResolver);
        var entryStructType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var mapPtrType = LLVMTypeRef.CreatePointer(mapStructType, 0);
        var entryPtrType = LLVMTypeRef.CreatePointer(entryStructType, 0);
        var kType = mapTypeResolver(keyTypeName);
        var vType = mapTypeResolver(valTypeName);

        var fnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { mapPtrType, kType, vType }, false);
        var func = _module.AddFunction($"rt_map_insert_{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}", fnType);
        _insertFuncs[mapKey] = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var mapPtr = func.GetParam(0);
        var keyVal = func.GetParam(1);
        var valVal = func.GetParam(2);

        var entriesSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 0, "entries_slot");
        var countSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 1, "count_slot");
        var capSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 2, "cap_slot");

        var curEntries = builder.BuildLoad2(entryPtrType, entriesSlot, "cur_entries");
        var curCount = builder.BuildLoad2(_context.Int32Type, countSlot, "cur_count");
        var curCap = builder.BuildLoad2(_context.Int32Type, capSlot, "cur_cap");

        // Check load factor: cap == 0 || (count + 1) * 4 >= cap * 3
        var zero32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        var one32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 1);
        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, zero32, "is_cap_zero");

        var nextCount = builder.BuildAdd(curCount, one32, "next_count");
        var loadFactorLeft = builder.BuildMul(nextCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 4), "lf_left");
        var loadFactorRight = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 3), "lf_right");
        var isOverloaded = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, loadFactorLeft, loadFactorRight, "is_overloaded");
        var needGrow = builder.BuildOr(isCapZero, isOverloaded, "need_grow");

        var growBB = func.AppendBasicBlock("grow");
        var probeBB = func.AppendBasicBlock("probe_init");

        builder.BuildCondBr(needGrow, growBB, probeBB);

        // --- GROW BLOCK ---
        builder.PositionAtEnd(growBB);
        var doubleCap = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cap");
        var newCap = builder.BuildSelect(isCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), doubleCap, "new_cap");

        ulong entrySize = Math.Max(8, LlvmApi.ABISizeOfType(_dataLayout, entryStructType));
        var newCap64 = builder.BuildZExt(newCap, _context.Int64Type, "new_cap_64");
        var entrySize64 = LLVMValueRef.CreateConstInt(_context.Int64Type, entrySize);

        var (callocFunc, callocType) = GetOrDeclareCalloc();
        var callocCall = builder.BuildCall2(callocType, callocFunc, new[] { newCap64, entrySize64 }, "new_raw_entries");
        var newEntries = builder.BuildBitCast(callocCall, entryPtrType, "new_entries");

        // Rehash old occupied entries if curCap > 0
        var rehashCheckBB = func.AppendBasicBlock("rehash_check");
        var rehashLoopBB = func.AppendBasicBlock("rehash_loop");
        var rehashIncBB = func.AppendBasicBlock("rehash_inc");
        var rehashDoneBB = func.AppendBasicBlock("rehash_done");

        builder.BuildCondBr(isCapZero, rehashDoneBB, rehashCheckBB);

        // rehashCheck:
        builder.PositionAtEnd(rehashCheckBB);
        var oldIdxAlloca = builder.BuildAlloca(_context.Int32Type, "old_idx");
        builder.BuildStore(zero32, oldIdxAlloca);
        builder.BuildBr(rehashLoopBB);

        // rehashLoop:
        builder.PositionAtEnd(rehashLoopBB);
        var oldIdx = builder.BuildLoad2(_context.Int32Type, oldIdxAlloca, "cur_old_idx");
        var hasMoreOld = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, oldIdx, curCap, "has_more_old");
        builder.BuildCondBr(hasMoreOld, func.AppendBasicBlock("rehash_body"), rehashDoneBB);

        // rehashBody:
        var rehashBodyBB = func.LastBasicBlock;
        builder.PositionAtEnd(rehashBodyBB);
        var oldSlotGEP = builder.BuildInBoundsGEP2(entryStructType, curEntries, new[] { oldIdx }, "old_slot_gep");
        var oldStateSlot = builder.BuildStructGEP2(entryStructType, oldSlotGEP, 2, "old_state_slot");
        var oldState = builder.BuildLoad2(_context.Int32Type, oldStateSlot, "old_state");
        var isOldOccupied = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, oldState, one32, "is_old_occ");

        var doRehashBB = func.AppendBasicBlock("do_rehash");
        builder.BuildCondBr(isOldOccupied, doRehashBB, rehashIncBB);

        // doRehash:
        builder.PositionAtEnd(doRehashBB);
        var oldKeySlot = builder.BuildStructGEP2(entryStructType, oldSlotGEP, 0, "old_key_slot");
        var oldKey = builder.BuildLoad2(kType, oldKeySlot, "old_key");
        var oldValSlot = builder.BuildStructGEP2(entryStructType, oldSlotGEP, 1, "old_val_slot");
        var oldVal = builder.BuildLoad2(vType, oldValSlot, "old_val");

        var oldHash = EmitHash(builder, keyTypeName, oldKey);
        var newCapMask = builder.BuildSub(newCap, one32, "new_cap_mask");
        var rehashHAlloca = builder.BuildAlloca(_context.Int32Type, "rehash_h");
        var initialRehashH = builder.BuildAnd(oldHash, newCapMask, "rehash_h_init");
        builder.BuildStore(initialRehashH, rehashHAlloca);

        var rehashFindBB = func.AppendBasicBlock("rehash_find");
        builder.BuildBr(rehashFindBB);

        builder.PositionAtEnd(rehashFindBB);
        var curRH = builder.BuildLoad2(_context.Int32Type, rehashHAlloca, "cur_rh");
        var newSlotGEP = builder.BuildInBoundsGEP2(entryStructType, newEntries, new[] { curRH }, "new_slot_gep");
        var newSlotStateSlot = builder.BuildStructGEP2(entryStructType, newSlotGEP, 2, "new_slot_state");
        var newSlotState = builder.BuildLoad2(_context.Int32Type, newSlotStateSlot, "new_slot_state_val");
        var isNewSlotEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, newSlotState, zero32, "is_new_empty");

        var rehashStoreBB = func.AppendBasicBlock("rehash_store");
        var rehashStepBB = func.AppendBasicBlock("rehash_step");
        builder.BuildCondBr(isNewSlotEmpty, rehashStoreBB, rehashStepBB);

        // rehashStep:
        builder.PositionAtEnd(rehashStepBB);
        var nextRH = builder.BuildAdd(curRH, one32, "next_rh");
        var wrappedRH = builder.BuildAnd(nextRH, newCapMask, "wrapped_rh");
        builder.BuildStore(wrappedRH, rehashHAlloca);
        builder.BuildBr(rehashFindBB);

        // rehashStore:
        builder.PositionAtEnd(rehashStoreBB);
        var nKeySlot = builder.BuildStructGEP2(entryStructType, newSlotGEP, 0, "n_key_slot");
        builder.BuildStore(oldKey, nKeySlot);
        var nValSlot = builder.BuildStructGEP2(entryStructType, newSlotGEP, 1, "n_val_slot");
        builder.BuildStore(oldVal, nValSlot);
        builder.BuildStore(one32, newSlotStateSlot);
        builder.BuildBr(rehashIncBB);

        // rehashInc:
        builder.PositionAtEnd(rehashIncBB);
        var nextOldIdx = builder.BuildAdd(oldIdx, one32, "next_old_idx");
        builder.BuildStore(nextOldIdx, oldIdxAlloca);
        builder.BuildBr(rehashLoopBB);

        // rehashDone:
        builder.PositionAtEnd(rehashDoneBB);
        var oldEntriesRaw = builder.BuildBitCast(curEntries, LLVMTypeRef.CreatePointer(_context.Int8Type, 0), "old_entries_raw");
        var freeOldBB = func.AppendBasicBlock("free_old");
        var skipFreeBB = func.AppendBasicBlock("skip_free");
        builder.BuildCondBr(isCapZero, skipFreeBB, freeOldBB);

        builder.PositionAtEnd(freeOldBB);
        var (freeFunc, freeType) = GetOrDeclareFree();
        builder.BuildCall2(freeType, freeFunc, new[] { oldEntriesRaw }, "");
        builder.BuildBr(skipFreeBB);

        builder.PositionAtEnd(skipFreeBB);
        builder.BuildStore(newEntries, entriesSlot);
        builder.BuildStore(newCap, capSlot);
        builder.BuildBr(probeBB);

        // --- PROBE AND INSERT BLOCK ---
        builder.PositionAtEnd(probeBB);
        var activeEntries = builder.BuildLoad2(entryPtrType, entriesSlot, "act_entries");
        var activeCap = builder.BuildLoad2(_context.Int32Type, capSlot, "act_cap");
        var activeMask = builder.BuildSub(activeCap, one32, "act_mask");

        var keyHash = EmitHash(builder, keyTypeName, keyVal);
        var initialIdx = builder.BuildAnd(keyHash, activeMask, "act_h_init");

        var idxAlloca = builder.BuildAlloca(_context.Int32Type, "probe_idx");
        builder.BuildStore(initialIdx, idxAlloca);

        var firstTombAlloca = builder.BuildAlloca(_context.Int32Type, "first_tomb");
        builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0xFFFFFFFFUL), firstTombAlloca);

        var probeLoopBB = func.AppendBasicBlock("probe_loop");
        builder.BuildBr(probeLoopBB);

        builder.PositionAtEnd(probeLoopBB);
        var pIdx = builder.BuildLoad2(_context.Int32Type, idxAlloca, "p_idx");
        var slotGEP = builder.BuildInBoundsGEP2(entryStructType, activeEntries, new[] { pIdx }, "slot_gep");
        var slotStateSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 2, "slot_state_slot");
        var slotState = builder.BuildLoad2(_context.Int32Type, slotStateSlot, "slot_state");

        var isEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, zero32, "is_empty");
        var isOcc = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, one32, "is_occ");

        var emptyBB = func.AppendBasicBlock("slot_empty");
        var occupiedBB = func.AppendBasicBlock("slot_occupied");
        var tombstoneBB = func.AppendBasicBlock("slot_tombstone");

        builder.BuildCondBr(isEmpty, emptyBB, func.AppendBasicBlock("check_occ"));
        var checkOccBB = func.LastBasicBlock;
        builder.PositionAtEnd(checkOccBB);
        builder.BuildCondBr(isOcc, occupiedBB, tombstoneBB);

        // 1. Slot is Empty -> Insert here or into first tombstone
        builder.PositionAtEnd(emptyBB);
        var firstTomb = builder.BuildLoad2(_context.Int32Type, firstTombAlloca, "ft_val");
        var hasTomb = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, firstTomb, LLVMValueRef.CreateConstInt(_context.Int32Type, 0xFFFFFFFFUL), "has_tomb");
        var finalIdx = builder.BuildSelect(hasTomb, firstTomb, pIdx, "final_insert_idx");
        var finalSlotGEP = builder.BuildInBoundsGEP2(entryStructType, activeEntries, new[] { finalIdx }, "final_slot_gep");

        var finalKeySlot = builder.BuildStructGEP2(entryStructType, finalSlotGEP, 0, "fin_key_slot");
        builder.BuildStore(keyVal, finalKeySlot);
        var finalValSlot = builder.BuildStructGEP2(entryStructType, finalSlotGEP, 1, "fin_val_slot");
        builder.BuildStore(valVal, finalValSlot);
        var finalStateSlot = builder.BuildStructGEP2(entryStructType, finalSlotGEP, 2, "fin_state_slot");
        builder.BuildStore(one32, finalStateSlot);

        // Increment count
        var latestCount = builder.BuildLoad2(_context.Int32Type, countSlot, "latest_count");
        var incCount = builder.BuildAdd(latestCount, one32, "inc_count");
        builder.BuildStore(incCount, countSlot);
        builder.BuildRetVoid();

        // 2. Slot is Occupied -> Check key equality
        builder.PositionAtEnd(occupiedBB);
        var occKeySlot = builder.BuildStructGEP2(entryStructType, slotGEP, 0, "occ_key_slot");
        var occKey = builder.BuildLoad2(kType, occKeySlot, "occ_key");
        var isKeysEqual = EmitKeysEqual(builder, keyTypeName, occKey, keyVal);

        var keyMatchBB = func.AppendBasicBlock("key_match");
        var keyMismatchBB = func.AppendBasicBlock("key_mismatch");
        builder.BuildCondBr(isKeysEqual, keyMatchBB, keyMismatchBB);

        // Key matches -> overwrite value and return
        builder.PositionAtEnd(keyMatchBB);
        var occValSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 1, "occ_val_slot");
        builder.BuildStore(valVal, occValSlot);
        builder.BuildRetVoid();

        // Key mismatch -> advance probe
        builder.PositionAtEnd(keyMismatchBB);
        var nextPIdx1 = builder.BuildAdd(pIdx, one32, "next_pidx1");
        var wrappedPIdx1 = builder.BuildAnd(nextPIdx1, activeMask, "wrapped_pidx1");
        builder.BuildStore(wrappedPIdx1, idxAlloca);
        builder.BuildBr(probeLoopBB);

        // 3. Slot is Tombstone -> record first tombstone and advance
        builder.PositionAtEnd(tombstoneBB);
        var curFT = builder.BuildLoad2(_context.Int32Type, firstTombAlloca, "cur_ft");
        var isFTUnset = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curFT, LLVMValueRef.CreateConstInt(_context.Int32Type, 0xFFFFFFFFUL), "is_ft_unset");
        var recFTBB = func.AppendBasicBlock("rec_ft");
        var skipFTBB = func.AppendBasicBlock("skip_ft");
        builder.BuildCondBr(isFTUnset, recFTBB, skipFTBB);

        builder.PositionAtEnd(recFTBB);
        builder.BuildStore(pIdx, firstTombAlloca);
        builder.BuildBr(skipFTBB);

        builder.PositionAtEnd(skipFTBB);
        var nextPIdx2 = builder.BuildAdd(pIdx, one32, "next_pidx2");
        var wrappedPIdx2 = builder.BuildAnd(nextPIdx2, activeMask, "wrapped_pidx2");
        builder.BuildStore(wrappedPIdx2, idxAlloca);
        builder.BuildBr(probeLoopBB);

        return func;
    }

    public LLVMValueRef GetOrCreateGet(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_getFuncs.TryGetValue(mapKey, out var f)) return f;

        var mapStructType = GetMapType(keyTypeName, valTypeName, mapTypeResolver);
        var entryStructType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var mapPtrType = LLVMTypeRef.CreatePointer(mapStructType, 0);
        var entryPtrType = LLVMTypeRef.CreatePointer(entryStructType, 0);
        var kType = mapTypeResolver(keyTypeName);
        var vType = mapTypeResolver(valTypeName);

        var fnType = LLVMTypeRef.CreateFunction(vType, new[] { mapPtrType, kType }, false);
        var func = _module.AddFunction($"rt_map_get_{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}", fnType);
        _getFuncs[mapKey] = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var mapPtr = func.GetParam(0);
        var keyVal = func.GetParam(1);

        var capSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 2, "cap_slot");
        var capVal = builder.BuildLoad2(_context.Int32Type, capSlot, "cap_val");
        var zero32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        var one32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 1);
        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, capVal, zero32, "is_cap_zero");

        var emptyMapBB = func.AppendBasicBlock("empty_map");
        var searchBB = func.AppendBasicBlock("search_init");
        builder.BuildCondBr(isCapZero, emptyMapBB, searchBB);

        // emptyMap:
        builder.PositionAtEnd(emptyMapBB);
        var defaultVal = LLVMValueRef.CreateConstNull(vType);
        builder.BuildRet(defaultVal);

        // searchInit:
        builder.PositionAtEnd(searchBB);
        var entriesSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 0, "entries_slot");
        var entriesVal = builder.BuildLoad2(entryPtrType, entriesSlot, "entries_val");
        var mask = builder.BuildSub(capVal, one32, "mask");

        var hash = EmitHash(builder, keyTypeName, keyVal);
        var initIdx = builder.BuildAnd(hash, mask, "init_idx");

        var idxAlloca = builder.BuildAlloca(_context.Int32Type, "g_idx");
        builder.BuildStore(initIdx, idxAlloca);

        var stepAlloca = builder.BuildAlloca(_context.Int32Type, "step_cnt");
        builder.BuildStore(zero32, stepAlloca);

        var loopBB = func.AppendBasicBlock("g_loop");
        builder.BuildBr(loopBB);

        builder.PositionAtEnd(loopBB);
        var stepVal = builder.BuildLoad2(_context.Int32Type, stepAlloca, "cur_step");
        var tooManySteps = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, stepVal, capVal, "too_many_steps");
        var stepOkBB = func.AppendBasicBlock("step_ok");
        builder.BuildCondBr(tooManySteps, emptyMapBB, stepOkBB);

        builder.PositionAtEnd(stepOkBB);
        var curIdx = builder.BuildLoad2(_context.Int32Type, idxAlloca, "cur_idx");
        var slotGEP = builder.BuildInBoundsGEP2(entryStructType, entriesVal, new[] { curIdx }, "slot_gep");
        var slotStateSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 2, "slot_state_slot");
        var slotState = builder.BuildLoad2(_context.Int32Type, slotStateSlot, "slot_state");

        var isEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, zero32, "is_empty");
        var checkOccBB = func.AppendBasicBlock("check_occ");
        builder.BuildCondBr(isEmpty, emptyMapBB, checkOccBB);

        builder.PositionAtEnd(checkOccBB);
        var isOcc = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, one32, "is_occ");
        var testKeyBB = func.AppendBasicBlock("test_key");
        var advanceBB = func.AppendBasicBlock("advance");
        builder.BuildCondBr(isOcc, testKeyBB, advanceBB);

        builder.PositionAtEnd(testKeyBB);
        var slotKeySlot = builder.BuildStructGEP2(entryStructType, slotGEP, 0, "slot_key_slot");
        var slotKey = builder.BuildLoad2(kType, slotKeySlot, "slot_key");
        var isEq = EmitKeysEqual(builder, keyTypeName, slotKey, keyVal);
        var foundBB = func.AppendBasicBlock("found");
        builder.BuildCondBr(isEq, foundBB, advanceBB);

        // Found:
        builder.PositionAtEnd(foundBB);
        var slotValSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 1, "slot_val_slot");
        var foundVal = builder.BuildLoad2(vType, slotValSlot, "found_val");
        builder.BuildRet(foundVal);

        // Advance:
        builder.PositionAtEnd(advanceBB);
        var nextStep = builder.BuildAdd(stepVal, one32, "next_step");
        builder.BuildStore(nextStep, stepAlloca);
        var nextIdx = builder.BuildAdd(curIdx, one32, "next_idx");
        var wrappedIdx = builder.BuildAnd(nextIdx, mask, "wrapped_idx");
        builder.BuildStore(wrappedIdx, idxAlloca);
        builder.BuildBr(loopBB);

        return func;
    }

    public LLVMValueRef GetOrCreateContains(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_containsFuncs.TryGetValue(mapKey, out var f)) return f;

        var mapStructType = GetMapType(keyTypeName, valTypeName, mapTypeResolver);
        var entryStructType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var mapPtrType = LLVMTypeRef.CreatePointer(mapStructType, 0);
        var entryPtrType = LLVMTypeRef.CreatePointer(entryStructType, 0);
        var kType = mapTypeResolver(keyTypeName);

        var fnType = LLVMTypeRef.CreateFunction(_context.Int1Type, new[] { mapPtrType, kType }, false);
        var func = _module.AddFunction($"rt_map_contains_{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}", fnType);
        _containsFuncs[mapKey] = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var mapPtr = func.GetParam(0);
        var keyVal = func.GetParam(1);

        var capSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 2, "cap_slot");
        var capVal = builder.BuildLoad2(_context.Int32Type, capSlot, "cap_val");
        var zero32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        var one32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 1);
        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, capVal, zero32, "is_cap_zero");

        var retFalseBB = func.AppendBasicBlock("ret_false");
        var searchBB = func.AppendBasicBlock("search_init");
        builder.BuildCondBr(isCapZero, retFalseBB, searchBB);

        builder.PositionAtEnd(retFalseBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 0));

        builder.PositionAtEnd(searchBB);
        var entriesSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 0, "entries_slot");
        var entriesVal = builder.BuildLoad2(entryPtrType, entriesSlot, "entries_val");
        var mask = builder.BuildSub(capVal, one32, "mask");

        var hash = EmitHash(builder, keyTypeName, keyVal);
        var initIdx = builder.BuildAnd(hash, mask, "init_idx");

        var idxAlloca = builder.BuildAlloca(_context.Int32Type, "c_idx");
        builder.BuildStore(initIdx, idxAlloca);

        var stepAlloca = builder.BuildAlloca(_context.Int32Type, "step_cnt");
        builder.BuildStore(zero32, stepAlloca);

        var loopBB = func.AppendBasicBlock("c_loop");
        builder.BuildBr(loopBB);

        builder.PositionAtEnd(loopBB);
        var stepVal = builder.BuildLoad2(_context.Int32Type, stepAlloca, "cur_step");
        var tooManySteps = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, stepVal, capVal, "too_many_steps");
        var stepOkBB = func.AppendBasicBlock("step_ok");
        builder.BuildCondBr(tooManySteps, retFalseBB, stepOkBB);

        builder.PositionAtEnd(stepOkBB);
        var curIdx = builder.BuildLoad2(_context.Int32Type, idxAlloca, "cur_idx");
        var slotGEP = builder.BuildInBoundsGEP2(entryStructType, entriesVal, new[] { curIdx }, "slot_gep");
        var slotStateSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 2, "slot_state_slot");
        var slotState = builder.BuildLoad2(_context.Int32Type, slotStateSlot, "slot_state");

        var isEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, zero32, "is_empty");
        var checkOccBB = func.AppendBasicBlock("check_occ");
        builder.BuildCondBr(isEmpty, retFalseBB, checkOccBB);

        builder.PositionAtEnd(checkOccBB);
        var isOcc = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, one32, "is_occ");
        var testKeyBB = func.AppendBasicBlock("test_key");
        var advanceBB = func.AppendBasicBlock("advance");
        builder.BuildCondBr(isOcc, testKeyBB, advanceBB);

        builder.PositionAtEnd(testKeyBB);
        var slotKeySlot = builder.BuildStructGEP2(entryStructType, slotGEP, 0, "slot_key_slot");
        var slotKey = builder.BuildLoad2(kType, slotKeySlot, "slot_key");
        var isEq = EmitKeysEqual(builder, keyTypeName, slotKey, keyVal);
        var foundBB = func.AppendBasicBlock("found");
        builder.BuildCondBr(isEq, foundBB, advanceBB);

        builder.PositionAtEnd(foundBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 1));

        builder.PositionAtEnd(advanceBB);
        var nextStep = builder.BuildAdd(stepVal, one32, "next_step");
        builder.BuildStore(nextStep, stepAlloca);
        var nextIdx = builder.BuildAdd(curIdx, one32, "next_idx");
        var wrappedIdx = builder.BuildAnd(nextIdx, mask, "wrapped_idx");
        builder.BuildStore(wrappedIdx, idxAlloca);
        builder.BuildBr(loopBB);

        return func;
    }

    public LLVMValueRef GetOrCreateRemove(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_removeFuncs.TryGetValue(mapKey, out var f)) return f;

        var mapStructType = GetMapType(keyTypeName, valTypeName, mapTypeResolver);
        var entryStructType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var mapPtrType = LLVMTypeRef.CreatePointer(mapStructType, 0);
        var entryPtrType = LLVMTypeRef.CreatePointer(entryStructType, 0);
        var kType = mapTypeResolver(keyTypeName);

        var fnType = LLVMTypeRef.CreateFunction(_context.Int1Type, new[] { mapPtrType, kType }, false);
        var func = _module.AddFunction($"rt_map_remove_{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}", fnType);
        _removeFuncs[mapKey] = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var mapPtr = func.GetParam(0);
        var keyVal = func.GetParam(1);

        var capSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 2, "cap_slot");
        var capVal = builder.BuildLoad2(_context.Int32Type, capSlot, "cap_val");
        var zero32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        var one32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 1);
        var two32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 2); // Tombstone state
        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, capVal, zero32, "is_cap_zero");

        var retFalseBB = func.AppendBasicBlock("ret_false");
        var searchBB = func.AppendBasicBlock("search_init");
        builder.BuildCondBr(isCapZero, retFalseBB, searchBB);

        builder.PositionAtEnd(retFalseBB);
        builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 0));

        builder.PositionAtEnd(searchBB);
        var entriesSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 0, "entries_slot");
        var entriesVal = builder.BuildLoad2(entryPtrType, entriesSlot, "entries_val");
        var mask = builder.BuildSub(capVal, one32, "mask");

        var hash = EmitHash(builder, keyTypeName, keyVal);
        var initIdx = builder.BuildAnd(hash, mask, "init_idx");

        var idxAlloca = builder.BuildAlloca(_context.Int32Type, "r_idx");
        builder.BuildStore(initIdx, idxAlloca);

        var stepAlloca = builder.BuildAlloca(_context.Int32Type, "step_cnt");
        builder.BuildStore(zero32, stepAlloca);

        var loopBB = func.AppendBasicBlock("r_loop");
        builder.BuildBr(loopBB);

        builder.PositionAtEnd(loopBB);
        var stepVal = builder.BuildLoad2(_context.Int32Type, stepAlloca, "cur_step");
        var tooManySteps = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, stepVal, capVal, "too_many_steps");
        var stepOkBB = func.AppendBasicBlock("step_ok");
        builder.BuildCondBr(tooManySteps, retFalseBB, stepOkBB);

        builder.PositionAtEnd(stepOkBB);
        var curIdx = builder.BuildLoad2(_context.Int32Type, idxAlloca, "cur_idx");
        var slotGEP = builder.BuildInBoundsGEP2(entryStructType, entriesVal, new[] { curIdx }, "slot_gep");
        var slotStateSlot = builder.BuildStructGEP2(entryStructType, slotGEP, 2, "slot_state_slot");
        var slotState = builder.BuildLoad2(_context.Int32Type, slotStateSlot, "slot_state");

        var isEmpty = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, zero32, "is_empty");
        var checkOccBB = func.AppendBasicBlock("check_occ");
        builder.BuildCondBr(isEmpty, retFalseBB, checkOccBB);

        builder.PositionAtEnd(checkOccBB);
        var isOcc = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, slotState, one32, "is_occ");
        var testKeyBB = func.AppendBasicBlock("test_key");
        var advanceBB = func.AppendBasicBlock("advance");
        builder.BuildCondBr(isOcc, testKeyBB, advanceBB);

        builder.PositionAtEnd(testKeyBB);
        var slotKeySlot = builder.BuildStructGEP2(entryStructType, slotGEP, 0, "slot_key_slot");
        var slotKey = builder.BuildLoad2(kType, slotKeySlot, "slot_key");
        var isEq = EmitKeysEqual(builder, keyTypeName, slotKey, keyVal);
        var foundBB = func.AppendBasicBlock("found");
        builder.BuildCondBr(isEq, foundBB, advanceBB);

        // Found -> Mark as Tombstone and decrement count
        builder.PositionAtEnd(foundBB);
        builder.BuildStore(two32, slotStateSlot);

        var countSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 1, "count_slot");
        var curCount = builder.BuildLoad2(_context.Int32Type, countSlot, "cur_count");
        var decCount = builder.BuildSub(curCount, one32, "dec_count");
        builder.BuildStore(decCount, countSlot);
        builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int1Type, 1));

        builder.PositionAtEnd(advanceBB);
        var nextStep = builder.BuildAdd(stepVal, one32, "next_step");
        builder.BuildStore(nextStep, stepAlloca);
        var nextIdx = builder.BuildAdd(curIdx, one32, "next_idx");
        var wrappedIdx = builder.BuildAnd(nextIdx, mask, "wrapped_idx");
        builder.BuildStore(wrappedIdx, idxAlloca);
        builder.BuildBr(loopBB);

        return func;
    }

    public LLVMValueRef GetOrCreateClear(string keyTypeName, string valTypeName, Func<string, LLVMTypeRef> mapTypeResolver)
    {
        string mapKey = GetMapKey(keyTypeName, valTypeName);
        if (_clearFuncs.TryGetValue(mapKey, out var f)) return f;

        var mapStructType = GetMapType(keyTypeName, valTypeName, mapTypeResolver);
        var entryStructType = GetEntryType(keyTypeName, valTypeName, mapTypeResolver);
        var mapPtrType = LLVMTypeRef.CreatePointer(mapStructType, 0);

        var fnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { mapPtrType }, false);
        var func = _module.AddFunction($"rt_map_clear_{Sanitize(keyTypeName)}_{Sanitize(valTypeName)}", fnType);
        _clearFuncs[mapKey] = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        builder.PositionAtEnd(entryBB);

        var mapPtr = func.GetParam(0);

        var capSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 2, "cap_slot");
        var capVal = builder.BuildLoad2(_context.Int32Type, capSlot, "cap_val");
        var zero32 = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, capVal, zero32, "is_cap_zero");

        var doClearBB = func.AppendBasicBlock("do_clear");
        var retBB = func.AppendBasicBlock("ret");
        builder.BuildCondBr(isCapZero, retBB, doClearBB);

        builder.PositionAtEnd(doClearBB);
        var entriesSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 0, "entries_slot");
        var entriesVal = builder.BuildLoad2(LLVMTypeRef.CreatePointer(entryStructType, 0), entriesSlot, "entries_val");
        var entriesRaw = builder.BuildBitCast(entriesVal, LLVMTypeRef.CreatePointer(_context.Int8Type, 0), "entries_raw");

        ulong entrySize = Math.Max(8, LlvmApi.ABISizeOfType(_dataLayout, entryStructType));
        var cap64 = builder.BuildZExt(capVal, _context.Int64Type, "cap_64");
        var totalBytes = builder.BuildMul(cap64, LLVMValueRef.CreateConstInt(_context.Int64Type, entrySize), "total_bytes");

        var (memsetFunc, memsetType) = GetOrDeclareMemset();
        builder.BuildCall2(memsetType, memsetFunc, new[]
        {
            entriesRaw,
            zero32,
            totalBytes
        }, "");

        var countSlot = builder.BuildStructGEP2(mapStructType, mapPtr, 1, "count_slot");
        builder.BuildStore(zero32, countSlot);
        builder.BuildBr(retBB);

        builder.PositionAtEnd(retBB);
        builder.BuildRetVoid();

        return func;
    }

    private LLVMValueRef EmitHash(LLVMBuilderRef builder, string keyTypeName, LLVMValueRef keyVal)
    {
        if (keyTypeName is "string" or "str")
        {
            // FNV-1a Hash for C-strings:
            // uint32_t hash = 2166136261u;
            // while (*s) { hash = (hash ^ (uint8_t)(*s)) * 16777619u; s++; }
            var currentFn = builder.InsertBlock.Parent;

            var hashAlloca = builder.BuildAlloca(_context.Int32Type, "fnv_hash");
            builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 2166136261u), hashAlloca);

            var ptrAlloca = builder.BuildAlloca(LLVMTypeRef.CreatePointer(_context.Int8Type, 0), "fnv_ptr");
            builder.BuildStore(keyVal, ptrAlloca);

            var loopBB = currentFn.AppendBasicBlock("fnv_loop");
            var bodyBB = currentFn.AppendBasicBlock("fnv_body");
            var doneBB = currentFn.AppendBasicBlock("fnv_done");

            builder.BuildBr(loopBB);

            // loopBB:
            builder.PositionAtEnd(loopBB);
            var curP = builder.BuildLoad2(LLVMTypeRef.CreatePointer(_context.Int8Type, 0), ptrAlloca, "cur_p");
            var ch = builder.BuildLoad2(_context.Int8Type, curP, "ch");
            var isEnd = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, ch, LLVMValueRef.CreateConstInt(_context.Int8Type, 0), "is_end");
            builder.BuildCondBr(isEnd, doneBB, bodyBB);

            // bodyBB:
            builder.PositionAtEnd(bodyBB);
            var ch32 = builder.BuildZExt(ch, _context.Int32Type, "ch32");
            var curH = builder.BuildLoad2(_context.Int32Type, hashAlloca, "cur_h");
            var xored = builder.BuildXor(curH, ch32, "xored");
            var nextH = builder.BuildMul(xored, LLVMValueRef.CreateConstInt(_context.Int32Type, 16777619u), "next_h");
            builder.BuildStore(nextH, hashAlloca);

            var nextP = builder.BuildInBoundsGEP2(_context.Int8Type, curP, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "next_p");
            builder.BuildStore(nextP, ptrAlloca);
            builder.BuildBr(loopBB);

            // doneBB:
            builder.PositionAtEnd(doneBB);
            return builder.BuildLoad2(_context.Int32Type, hashAlloca, "final_hash");
        }

        if (keyTypeName is "f32" or "float")
        {
            var bits = builder.BuildBitCast(keyVal, _context.Int32Type, "f32_bits");
            return builder.BuildMul(bits, LLVMValueRef.CreateConstInt(_context.Int32Type, 2654435761u), "f32_hash");
        }

        // Default integer hash (Thomas Wang / Knuth multiplicative):
        var intVal = keyVal;
        if (keyVal.TypeOf == _context.Int64Type)
        {
            intVal = builder.BuildTrunc(keyVal, _context.Int32Type, "trunc_i64");
        }
        else if (keyVal.TypeOf != _context.Int32Type)
        {
            intVal = builder.BuildZExt(keyVal, _context.Int32Type, "zext_i32");
        }
        return builder.BuildMul(intVal, LLVMValueRef.CreateConstInt(_context.Int32Type, 2654435761u), "knuth_hash");
    }

    private LLVMValueRef EmitKeysEqual(LLVMBuilderRef builder, string keyTypeName, LLVMValueRef k1, LLVMValueRef k2)
    {
        if (keyTypeName is "string" or "str")
        {
            var (strcmpFunc, strcmpType) = GetOrDeclareStrcmp();
            var res = builder.BuildCall2(strcmpType, strcmpFunc, new[] { k1, k2 }, "str_cmp");
            return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, res, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_str_eq");
        }

        if (keyTypeName is "f32" or "float" or "f64" or "double")
        {
            return builder.BuildFCmp(LLVMRealPredicate.LLVMRealOEQ, k1, k2, "is_feq");
        }

        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, k1, k2, "is_eq");
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareCalloc()
    {
        var fType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(_context.Int8Type, 0), new[] { _context.Int64Type, _context.Int64Type }, false);
        var f = _module.GetNamedFunction("calloc");
        if (f.Handle == IntPtr.Zero) f = _module.AddFunction("calloc", fType);
        return (f, fType);
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareFree()
    {
        var fType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { LLVMTypeRef.CreatePointer(_context.Int8Type, 0) }, false);
        var f = _module.GetNamedFunction("free");
        if (f.Handle == IntPtr.Zero) f = _module.AddFunction("free", fType);
        return (f, fType);
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareStrcmp()
    {
        var i8Ptr = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { i8Ptr, i8Ptr }, false);
        var f = _module.GetNamedFunction("strcmp");
        if (f.Handle == IntPtr.Zero) f = _module.AddFunction("strcmp", fType);
        return (f, fType);
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareMemset()
    {
        var i8Ptr = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fType = LLVMTypeRef.CreateFunction(i8Ptr, new[] { i8Ptr, _context.Int32Type, _context.Int64Type }, false);
        var f = _module.GetNamedFunction("memset");
        if (f.Handle == IntPtr.Zero) f = _module.AddFunction("memset", fType);
        return (f, fType);
    }

    private static string Sanitize(string name)
    {
        return name.Replace("<", "_")
                   .Replace(">", "_")
                   .Replace(",", "_")
                   .Replace(" ", "")
                   .Replace("[", "_")
                   .Replace("]", "_")
                   .Replace(";", "_");
    }
}
