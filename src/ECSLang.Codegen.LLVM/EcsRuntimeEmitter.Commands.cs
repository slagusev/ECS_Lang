using System;
using System.Collections.Generic;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class EcsRuntimeEmitter
{
    private unsafe void EmitCommandBufferCoreHelpers(
        LLVMTypeRef worldPtrType,
        LLVMValueRef allocEntFunc,
        LLVMTypeRef allocEntType,
        LLVMValueRef srwAcquireFunc,
        LLVMValueRef srwReleaseFunc,
        LLVMTypeRef srwFuncType,
        LLVMValueRef reallocFunc,
        LLVMTypeRef reallocType,
        LLVMTypeRef i8PtrType,
        LLVMTypeRef i32PtrType,
        out LLVMValueRef ensureCapFunc,
        out LLVMTypeRef ensureCapType)
    {
        // =========================================================================
        // Helper: world_cmd_ensure_cap(ptr world, i32 needed) -> void
        // =========================================================================
        ensureCapType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        ensureCapFunc = _module.AddFunction("world_cmd_ensure_cap", ensureCapType);
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
    }

    private unsafe void EmitComponentCommandHelpers(
        LLVMTypeRef worldPtrType,
        LLVMValueRef ensureCapFunc,
        LLVMTypeRef ensureCapType,
        LLVMValueRef srwAcquireFunc,
        LLVMValueRef srwReleaseFunc,
        LLVMTypeRef srwFuncType,
        LLVMTypeRef i8PtrType,
        LLVMTypeRef i32PtrType,
        int k,
        string compName,
        ComponentSymbol compSym,
        LLVMTypeRef compStructType,
        ulong compSize)
    {
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

            var wArgCRem = cmdRemFunc.GetParam(0);
            var eArgCRem = cmdRemFunc.GetParam(1);

            var lockSlotRem = _builder.BuildStructGEP2(_worldStructType, wArgCRem, 10, "cmd_lock_slot_crem");
            var lockPtrRem = _builder.BuildBitCast(lockSlotRem, i8PtrType, "lock_ptr_crem");
            _builder.BuildCall2(srwFuncType, srwAcquireFunc, new[] { lockPtrRem }, "");

            _builder.BuildCall2(ensureCapType, ensureCapFunc, new[] { wArgCRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 12) }, "");

            var cmdCountSlotCRem = _builder.BuildStructGEP2(_worldStructType, wArgCRem, 7, "cmd_cnt_slot_crem");
            var cmdDataSlotCRem = _builder.BuildStructGEP2(_worldStructType, wArgCRem, 9, "cmd_data_slot_crem");
            var curCntCRem = _builder.BuildLoad2(_context.Int32Type, cmdCountSlotCRem, "cur_cnt_crem");
            var dataPtrCRem = _builder.BuildLoad2(i8PtrType, cmdDataSlotCRem, "data_ptr_crem");

            var curCntCRem64 = _builder.BuildZExt(curCntCRem, _context.Int64Type, "cur_cnt_crem64");
            var writePtrCRem = _builder.BuildInBoundsGEP2(_context.Int8Type, dataPtrCRem, new[] { curCntCRem64 }, "write_ptr_crem");
            var writePtrCRemI32 = _builder.BuildBitCast(writePtrCRem, i32PtrType, "write_ptr_crem_i32");

            var opSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0) }, "op_slot_crem");
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 4), opSlotCRem); // OP 4: REMOVE

            var eSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 1) }, "e_slot_crem");
            _builder.BuildStore(eArgCRem, eSlotCRem);

            var compIdSlotCRem = _builder.BuildInBoundsGEP2(_context.Int32Type, writePtrCRemI32, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 2) }, "comp_id_slot_crem");
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k), compIdSlotCRem);

            var newCntCRem = _builder.BuildAdd(curCntCRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 12), "new_cnt_crem");
            _builder.BuildStore(newCntCRem, cmdCountSlotCRem);

            _builder.BuildCall2(srwFuncType, srwReleaseFunc, new[] { lockPtrRem }, "");
            _builder.BuildRetVoid();
        }
    }

    private unsafe void EmitCommandPlaybackHelper(
        LLVMTypeRef worldPtrType,
        LLVMValueRef assignA0Func,
        LLVMTypeRef assignA0Type,
        LLVMValueRef despawnFunc,
        LLVMTypeRef despawnType,
        LLVMTypeRef i8PtrType,
        LLVMTypeRef i32PtrType,
        int totalComps,
        IReadOnlyList<string> compNames)
    {
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
    }
}
