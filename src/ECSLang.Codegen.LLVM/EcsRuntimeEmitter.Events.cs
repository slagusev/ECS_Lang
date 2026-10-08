using System;
using System.Collections.Generic;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed partial class EcsRuntimeEmitter
{
    private unsafe void EmitEventRuntime(
        LLVMTypeRef worldPtrType,
        LLVMValueRef reallocFunc,
        LLVMTypeRef reallocType,
        LLVMTypeRef i8PtrType)
    {
        // 1. world_emit_Event helpers
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

        // 2. world_swap_events(ptr world)
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
