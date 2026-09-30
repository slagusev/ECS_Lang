using System;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed class StringArenaEmitter
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;

    private LLVMTypeRef? _chunkStructType;
    private LLVMValueRef? _allocFunc;
    private LLVMValueRef? _resetFunc;
    private LLVMValueRef? _freeAllFunc;

    public StringArenaEmitter(LLVMContextRef context, LLVMModuleRef module)
    {
        _context = context;
        _module = module;
    }

    public LLVMTypeRef GetChunkStructType()
    {
        if (_chunkStructType.HasValue) return _chunkStructType.Value;

        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        // struct.StringArenaChunk: { ptr next, i64 used, i64 capacity }
        var chunk = _context.CreateNamedStruct("struct.StringArenaChunk");
        chunk.StructSetBody(new[]
        {
            i8PtrType,           // 0: next
            _context.Int64Type,  // 1: used
            _context.Int64Type   // 2: capacity
        }, false);

        _chunkStructType = chunk;
        return chunk;
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareMalloc()
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { _context.Int64Type }, false);
        var fn = _module.GetNamedFunction("malloc");
        if (fn.Handle == IntPtr.Zero)
            fn = _module.AddFunction("malloc", fnType);
        return (fn, fnType);
    }

    private (LLVMValueRef func, LLVMTypeRef type) GetOrDeclareFree()
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        var fn = _module.GetNamedFunction("free");
        if (fn.Handle == IntPtr.Zero)
            fn = _module.AddFunction("free", fnType);
        return (fn, fnType);
    }

    public LLVMValueRef GetOrCreateArenaAlloc(EcsRuntimeEmitter ecs)
    {
        if (_allocFunc.HasValue) return _allocFunc.Value;

        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, _context.Int64Type }, false);
        var func = _module.AddFunction("rt_world_string_arena_alloc", fnType);
        _allocFunc = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        var nullWorldBB = func.AppendBasicBlock("null_world");
        var validWorldBB = func.AppendBasicBlock("valid_world");
        var checkFitBB = func.AppendBasicBlock("check_fit");
        var fitBB = func.AppendBasicBlock("fit");
        var newChunkBB = func.AppendBasicBlock("new_chunk");

        builder.PositionAtEnd(entryBB);
        var worldParam = func.GetParam(0);
        var sizeParam = func.GetParam(1);

        var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
        var isWorldNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, worldParam, nullPtr, "is_wnull");
        builder.BuildCondBr(isWorldNull, nullWorldBB, validWorldBB);

        // --- fallback: malloc(size) ---
        builder.PositionAtEnd(nullWorldBB);
        var (mallocFunc, mallocType) = GetOrDeclareMalloc();
        var rawAlloc = builder.BuildCall2(mallocType, mallocFunc, new[] { sizeParam }, "raw_alloc");
        builder.BuildRet(rawAlloc);

        // --- validWorldBB ---
        builder.PositionAtEnd(validWorldBB);
        // Align requested size to 8 bytes: aligned = (size + 7) & ~7
        var seven64 = LLVMValueRef.CreateConstInt(_context.Int64Type, 7);
        var notSeven64 = LLVMValueRef.CreateConstInt(_context.Int64Type, ~7UL);
        var sizePlus7 = builder.BuildAdd(sizeParam, seven64, "sz_plus_7");
        var alignedSize = builder.BuildAnd(sizePlus7, notSeven64, "aligned_size");

        var worldStructType = ecs.WorldStructType;
        var worldTyped = builder.BuildBitCast(worldParam, LLVMTypeRef.CreatePointer(worldStructType, 0), "world_typed");

        var headSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaHeadWorldOffset, "head_slot");
        var curHeadRaw = builder.BuildLoad2(i8PtrType, headSlot, "cur_head_raw");
        var isHeadNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curHeadRaw, nullPtr, "is_head_null");
        builder.BuildCondBr(isHeadNull, newChunkBB, checkFitBB);

        // --- checkFitBB ---
        builder.PositionAtEnd(checkFitBB);
        var chunkStructType = GetChunkStructType();
        var curHeadTyped = builder.BuildBitCast(curHeadRaw, LLVMTypeRef.CreatePointer(chunkStructType, 0), "head_typed");
        var usedSlot = builder.BuildStructGEP2(chunkStructType, curHeadTyped, 1, "used_slot");
        var capSlot = builder.BuildStructGEP2(chunkStructType, curHeadTyped, 2, "cap_slot");

        var curUsed = builder.BuildLoad2(_context.Int64Type, usedSlot, "cur_used");
        var curCap = builder.BuildLoad2(_context.Int64Type, capSlot, "cur_cap");

        var remCap = builder.BuildSub(curCap, curUsed, "rem_cap");
        var canFit = builder.BuildICmp(LLVMIntPredicate.LLVMIntULE, alignedSize, remCap, "can_fit");
        builder.BuildCondBr(canFit, fitBB, newChunkBB);

        // --- fitBB ---
        builder.PositionAtEnd(fitBB);
        // data pointer = curHeadRaw + 24 (header size) + curUsed
        var header24 = LLVMValueRef.CreateConstInt(_context.Int64Type, 24);
        var byteOffset = builder.BuildAdd(header24, curUsed, "byte_offset");
        var resPtrFit = builder.BuildInBoundsGEP2(_context.Int8Type, curHeadRaw, new[] { byteOffset }, "res_fit");

        var nextUsed = builder.BuildAdd(curUsed, alignedSize, "next_used");
        builder.BuildStore(nextUsed, usedSlot);
        builder.BuildRet(resPtrFit);

        // --- newChunkBB ---
        builder.PositionAtEnd(newChunkBB);
        // chunkCap = max(65536, alignedSize)
        var defaultCap64 = LLVMValueRef.CreateConstInt(_context.Int64Type, 65536);
        var isLarge = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, alignedSize, defaultCap64, "is_large");
        var chunkCap = builder.BuildSelect(isLarge, alignedSize, defaultCap64, "chunk_cap");

        var totalBytes = builder.BuildAdd(chunkCap, header24, "total_chunk_bytes");
        var newChunkRaw = builder.BuildCall2(mallocType, mallocFunc, new[] { totalBytes }, "new_chunk_raw");
        var newChunkTyped = builder.BuildBitCast(newChunkRaw, LLVMTypeRef.CreatePointer(chunkStructType, 0), "new_chunk_typed");

        var chunksListSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaChunksWorldOffset, "chunks_list_slot");
        var curChunksList = builder.BuildLoad2(i8PtrType, chunksListSlot, "cur_chunks_list");

        // newChunk.next = curChunksList
        var nNextSlot = builder.BuildStructGEP2(chunkStructType, newChunkTyped, 0, "n_next_slot");
        builder.BuildStore(curChunksList, nNextSlot);

        // newChunk.used = alignedSize
        var nUsedSlot = builder.BuildStructGEP2(chunkStructType, newChunkTyped, 1, "n_used_slot");
        builder.BuildStore(alignedSize, nUsedSlot);

        // newChunk.capacity = chunkCap
        var nCapSlot = builder.BuildStructGEP2(chunkStructType, newChunkTyped, 2, "n_cap_slot");
        builder.BuildStore(chunkCap, nCapSlot);

        // world.chunks = newChunkRaw
        builder.BuildStore(newChunkRaw, chunksListSlot);
        // world.head = newChunkRaw
        builder.BuildStore(newChunkRaw, headSlot);

        // return newChunkRaw + 24
        var resPtrNew = builder.BuildInBoundsGEP2(_context.Int8Type, newChunkRaw, new[] { header24 }, "res_new");
        builder.BuildRet(resPtrNew);

        return func;
    }

    public LLVMValueRef GetOrCreateArenaReset(EcsRuntimeEmitter ecs)
    {
        if (_resetFunc.HasValue) return _resetFunc.Value;

        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        var func = _module.AddFunction("rt_world_string_arena_reset", fnType);
        _resetFunc = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        var workBB = func.AppendBasicBlock("work");
        var loopHeadBB = func.AppendBasicBlock("loop_head");
        var loopBodyBB = func.AppendBasicBlock("loop_body");
        var resetDoneBB = func.AppendBasicBlock("reset_done");
        var exitBB = func.AppendBasicBlock("exit");

        builder.PositionAtEnd(entryBB);
        var worldParam = func.GetParam(0);
        var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
        var isWorldNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, worldParam, nullPtr, "is_wnull");
        builder.BuildCondBr(isWorldNull, exitBB, workBB);

        // workBB:
        builder.PositionAtEnd(workBB);
        var worldStructType = ecs.WorldStructType;
        var worldTyped = builder.BuildBitCast(worldParam, LLVMTypeRef.CreatePointer(worldStructType, 0), "world_typed");
        var chunksListSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaChunksWorldOffset, "chunks_list_slot");
        var firstChunkRaw = builder.BuildLoad2(i8PtrType, chunksListSlot, "first_chunk_raw");

        var chunkPtrAlloca = builder.BuildAlloca(i8PtrType, "chunk_cur_alloca");
        builder.BuildStore(firstChunkRaw, chunkPtrAlloca);
        builder.BuildBr(loopHeadBB);

        // loopHeadBB:
        builder.PositionAtEnd(loopHeadBB);
        var curChunk = builder.BuildLoad2(i8PtrType, chunkPtrAlloca, "cur_chunk");
        var isDone = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curChunk, nullPtr, "is_done");
        builder.BuildCondBr(isDone, resetDoneBB, loopBodyBB);

        // loopBodyBB:
        builder.PositionAtEnd(loopBodyBB);
        var chunkStructType = GetChunkStructType();
        var curChunkTyped = builder.BuildBitCast(curChunk, LLVMTypeRef.CreatePointer(chunkStructType, 0), "cur_chunk_typed");

        // curChunk.used = 0
        var usedSlot = builder.BuildStructGEP2(chunkStructType, curChunkTyped, 1, "c_used_slot");
        builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int64Type, 0), usedSlot);

        // next = curChunk.next
        var nextSlot = builder.BuildStructGEP2(chunkStructType, curChunkTyped, 0, "c_next_slot");
        var nextChunk = builder.BuildLoad2(i8PtrType, nextSlot, "next_chunk");
        builder.BuildStore(nextChunk, chunkPtrAlloca);
        builder.BuildBr(loopHeadBB);

        // resetDoneBB:
        builder.PositionAtEnd(resetDoneBB);
        var headSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaHeadWorldOffset, "head_slot");
        builder.BuildStore(firstChunkRaw, headSlot);
        builder.BuildBr(exitBB);

        // exitBB:
        builder.PositionAtEnd(exitBB);
        builder.BuildRetVoid();

        return func;
    }

    public LLVMValueRef GetOrCreateArenaFreeAll(EcsRuntimeEmitter ecs)
    {
        if (_freeAllFunc.HasValue) return _freeAllFunc.Value;

        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var fnType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { i8PtrType }, false);
        var func = _module.AddFunction("rt_world_string_arena_free_all", fnType);
        _freeAllFunc = func;

        using var builder = _context.CreateBuilder();
        var entryBB = func.AppendBasicBlock("entry");
        var workBB = func.AppendBasicBlock("work");
        var loopCheckBB = func.AppendBasicBlock("loop_check");
        var loopBodyBB = func.AppendBasicBlock("loop_body");
        var freeDoneBB = func.AppendBasicBlock("free_done");
        var exitBB = func.AppendBasicBlock("exit");

        builder.PositionAtEnd(entryBB);
        var worldParam = func.GetParam(0);
        var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
        var isWorldNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, worldParam, nullPtr, "is_wnull");
        builder.BuildCondBr(isWorldNull, exitBB, workBB);

        // workBB:
        builder.PositionAtEnd(workBB);
        var worldStructType = ecs.WorldStructType;
        var worldTyped = builder.BuildBitCast(worldParam, LLVMTypeRef.CreatePointer(worldStructType, 0), "world_typed");
        var chunksListSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaChunksWorldOffset, "chunks_list_slot");
        var firstChunkRaw = builder.BuildLoad2(i8PtrType, chunksListSlot, "first_chunk_raw");

        var chunkPtrAlloca = builder.BuildAlloca(i8PtrType, "free_chunk_alloca");
        builder.BuildStore(firstChunkRaw, chunkPtrAlloca);
        builder.BuildBr(loopCheckBB);

        // loopCheckBB:
        builder.PositionAtEnd(loopCheckBB);
        var curChunk = builder.BuildLoad2(i8PtrType, chunkPtrAlloca, "free_cur_chunk");
        var isDone = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curChunk, nullPtr, "free_is_done");
        builder.BuildCondBr(isDone, freeDoneBB, loopBodyBB);

        // loopBodyBB:
        builder.PositionAtEnd(loopBodyBB);
        var chunkStructType = GetChunkStructType();
        var curChunkTyped = builder.BuildBitCast(curChunk, LLVMTypeRef.CreatePointer(chunkStructType, 0), "free_cur_typed");

        var nextSlot = builder.BuildStructGEP2(chunkStructType, curChunkTyped, 0, "free_next_slot");
        var nextChunk = builder.BuildLoad2(i8PtrType, nextSlot, "free_next_chunk");
        builder.BuildStore(nextChunk, chunkPtrAlloca);

        var (freeFunc, freeType) = GetOrDeclareFree();
        builder.BuildCall2(freeType, freeFunc, new[] { curChunk }, "");
        builder.BuildBr(loopCheckBB);

        // freeDoneBB:
        builder.PositionAtEnd(freeDoneBB);
        builder.BuildStore(nullPtr, chunksListSlot);
        var headSlot = builder.BuildStructGEP2(worldStructType, worldTyped, (uint)ecs.StringArenaHeadWorldOffset, "head_slot");
        builder.BuildStore(nullPtr, headSlot);
        builder.BuildBr(exitBB);

        // exitBB:
        builder.PositionAtEnd(exitBB);
        builder.BuildRetVoid();

        return func;
    }
}
