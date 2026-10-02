using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe bool TryGenerateTimeCall(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        CallExpression call,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        out LLVMValueRef result)
    {
        switch (call.Callee)
        {
            case "stopwatch_start":
            case "sys_time_ticks":
            {
                if (_options.Target.IsWindows)
                {
                    var i64PtrType = LLVMTypeRef.CreatePointer(context.Int64Type, 0);
                    var qpcFunc = GetOrDeclareCrtFunc(module, "QueryPerformanceCounter", context.Int32Type, new[] { i64PtrType });
                    var qpcType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(qpcFunc);

                    var ctrAlloca = CreateEntryBlockAlloca(context, function, context.Int64Type, "sw_start_alloca");
                    builder.BuildCall2(qpcType, qpcFunc, new[] { ctrAlloca }, "");
                    result = builder.BuildLoad2(context.Int64Type, ctrAlloca, "sw_start_ticks");
                    return true;
                }
                else
                {
                    // POSIX clock_gettime(CLOCK_MONOTONIC = 1, &ts)
                    var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
                    var timespecStructType = LLVMTypeRef.CreateStruct(new[] { context.Int64Type, context.Int64Type }, false);
                    var clockGetTimeFunc = GetOrDeclareCrtFunc(module, "clock_gettime", context.Int32Type, new[] { context.Int32Type, i8PtrType });
                    var clockGetTimeType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(clockGetTimeFunc);

                    var tsAlloca = CreateEntryBlockAlloca(context, function, timespecStructType, "sw_ts_alloca");
                    var tsPtrCast = builder.BuildPointerCast(tsAlloca, i8PtrType, "sw_ts_ptr");
                    builder.BuildCall2(clockGetTimeType, clockGetTimeFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1), tsPtrCast }, "");

                    var secSlot = builder.BuildStructGEP2(timespecStructType, tsAlloca, 0, "sw_sec_slot");
                    var nsecSlot = builder.BuildStructGEP2(timespecStructType, tsAlloca, 1, "sw_nsec_slot");
                    var secVal = builder.BuildLoad2(context.Int64Type, secSlot, "sw_sec");
                    var nsecVal = builder.BuildLoad2(context.Int64Type, nsecSlot, "sw_nsec");

                    var billion = LLVMValueRef.CreateConstInt(context.Int64Type, 1000000000);
                    var secNs = builder.BuildMul(secVal, billion, "sw_sec_ns");
                    result = builder.BuildAdd(secNs, nsecVal, "sw_start_ns");
                    return true;
                }
            }

            case "stopwatch_ms":
            {
                var startVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                if (startVal.TypeOf != context.Int64Type)
                {
                    startVal = builder.BuildSExtOrBitCast(startVal, context.Int64Type, "sw_start_i64");
                }

                if (_options.Target.IsWindows)
                {
                    var i64PtrType = LLVMTypeRef.CreatePointer(context.Int64Type, 0);
                    var qpcFunc = GetOrDeclareCrtFunc(module, "QueryPerformanceCounter", context.Int32Type, new[] { i64PtrType });
                    var qpcType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(qpcFunc);
                    var qpfFunc = GetOrDeclareCrtFunc(module, "QueryPerformanceFrequency", context.Int32Type, new[] { i64PtrType });
                    var qpfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(qpfFunc);

                    var nowAlloca = CreateEntryBlockAlloca(context, function, context.Int64Type, "sw_now_alloca");
                    var freqAlloca = CreateEntryBlockAlloca(context, function, context.Int64Type, "sw_freq_alloca");

                    builder.BuildCall2(qpcType, qpcFunc, new[] { nowAlloca }, "");
                    builder.BuildCall2(qpfType, qpfFunc, new[] { freqAlloca }, "");

                    var nowVal = builder.BuildLoad2(context.Int64Type, nowAlloca, "sw_now_ticks");
                    var freqVal = builder.BuildLoad2(context.Int64Type, freqAlloca, "sw_freq");

                    var diffTicks = builder.BuildSub(nowVal, startVal, "sw_diff_ticks");
                    var diffDouble = builder.BuildSIToFP(diffTicks, context.DoubleType, "sw_diff_f64");
                    var freqDouble = builder.BuildSIToFP(freqVal, context.DoubleType, "sw_freq_f64");

                    var thousandDouble = LLVMValueRef.CreateConstReal(context.DoubleType, 1000.0);
                    var msMul = builder.BuildFMul(diffDouble, thousandDouble, "sw_ms_mul");
                    var msDouble = builder.BuildFDiv(msMul, freqDouble, "sw_ms_f64");

                    result = builder.BuildFPTrunc(msDouble, context.FloatType, "sw_ms_f32");
                    return true;
                }
                else
                {
                    // POSIX clock_gettime
                    var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
                    var timespecStructType = LLVMTypeRef.CreateStruct(new[] { context.Int64Type, context.Int64Type }, false);
                    var clockGetTimeFunc = GetOrDeclareCrtFunc(module, "clock_gettime", context.Int32Type, new[] { context.Int32Type, i8PtrType });
                    var clockGetTimeType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(clockGetTimeFunc);

                    var tsAlloca = CreateEntryBlockAlloca(context, function, timespecStructType, "sw_now_ts_alloca");
                    var tsPtrCast = builder.BuildPointerCast(tsAlloca, i8PtrType, "sw_now_ts_ptr");
                    builder.BuildCall2(clockGetTimeType, clockGetTimeFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1), tsPtrCast }, "");

                    var secSlot = builder.BuildStructGEP2(timespecStructType, tsAlloca, 0, "sw_now_sec_slot");
                    var nsecSlot = builder.BuildStructGEP2(timespecStructType, tsAlloca, 1, "sw_now_nsec_slot");
                    var secVal = builder.BuildLoad2(context.Int64Type, secSlot, "sw_now_sec");
                    var nsecVal = builder.BuildLoad2(context.Int64Type, nsecSlot, "sw_now_nsec");

                    var billion = LLVMValueRef.CreateConstInt(context.Int64Type, 1000000000);
                    var secNs = builder.BuildMul(secVal, billion, "sw_now_sec_ns");
                    var nowNs = builder.BuildAdd(secNs, nsecVal, "sw_now_ns");

                    var diffNs = builder.BuildSub(nowNs, startVal, "sw_diff_ns");
                    var diffDouble = builder.BuildSIToFP(diffNs, context.DoubleType, "sw_diff_f64");
                    var millionDouble = LLVMValueRef.CreateConstReal(context.DoubleType, 1000000.0);
                    var msDouble = builder.BuildFDiv(diffDouble, millionDouble, "sw_ms_f64");

                    result = builder.BuildFPTrunc(msDouble, context.FloatType, "sw_ms_f32");
                    return true;
                }
            }

            default:
                result = default;
                return false;
        }
    }
}
