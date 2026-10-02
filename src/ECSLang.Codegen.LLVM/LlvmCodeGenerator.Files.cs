using System;
using System.Collections.Generic;
using ECSLang.Core.AST;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe bool TryGenerateFileIoCall(
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
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        switch (call.Callee)
        {
            case "file_exists":
            {
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);

                var resAlloca = CreateEntryBlockAlloca(context, function, context.Int1Type, "fe_res_alloca");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 0), resAlloca);

                var modeVal = builder.BuildGlobalStringPtr("rb", "mode_fe_rb");
                var fp = builder.BuildCall2(fopenType, fopenFunc, new[] { pathVal, modeVal }, "fe_fp");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isNotNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, fp, nullPtr, "fe_not_null");

                var closeBB = function.AppendBasicBlock("fe_close");
                var mergeBB = function.AppendBasicBlock("fe_merge");

                builder.BuildCondBr(isNotNull, closeBB, mergeBB);

                builder.PositionAtEnd(closeBB);
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 1), resAlloca);
                builder.BuildCall2(fcloseType, fcloseFunc, new[] { fp }, "");
                builder.BuildBr(mergeBB);

                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(context.Int1Type, resAlloca, "fe_res");
                return true;
            }

            case "file_write_text":
            case "file_append_text":
            {
                bool isAppend = call.Callee == "file_append_text";
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var contentVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);
                var fwriteFunc = GetOrDeclareCrtFunc(module, "fwrite", context.Int64Type, new[] { i8PtrType, context.Int64Type, context.Int64Type, i8PtrType });
                var fwriteType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fwriteFunc);
                var strlenFunc = GetOrDeclareCrtFunc(module, "strlen", context.Int64Type, new[] { i8PtrType });
                var strlenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(strlenFunc);

                var resStructType = MapType(context, "Result<bool, string>", ecs);
                var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, isAppend ? "fa_res_alloca" : "fw_res_alloca");

                var modeStr = isAppend ? "ab" : "wb";
                var modeVal = builder.BuildGlobalStringPtr(modeStr, isAppend ? "mode_ab" : "mode_wb");
                var fp = builder.BuildCall2(fopenType, fopenFunc, new[] { pathVal, modeVal }, "fw_fp");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isNotNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, fp, nullPtr, "fw_not_null");

                var writeBB = function.AppendBasicBlock(isAppend ? "fa_body" : "fw_body");
                var errBB = function.AppendBasicBlock(isAppend ? "fa_err" : "fw_err");
                var mergeBB = function.AppendBasicBlock(isAppend ? "fa_merge" : "fw_merge");

                builder.BuildCondBr(isNotNull, writeBB, errBB);

                // Body block (Atomic locked write)
                builder.PositionAtEnd(writeBB);
                EmitLockFile(module, builder, fp, i8PtrType, context);
                var contentLen = builder.BuildCall2(strlenType, strlenFunc, new[] { contentVal }, "content_len");
                builder.BuildCall2(fwriteType, fwriteFunc, new[] { contentVal, LLVMValueRef.CreateConstInt(context.Int64Type, 1), contentLen, fp }, "written_count");
                EmitUnlockFile(module, builder, fp, i8PtrType, context);
                builder.BuildCall2(fcloseType, fcloseFunc, new[] { fp }, "");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(i8PtrType), builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Error block
                builder.PositionAtEnd(errBB);
                var errString = isAppend ? "Failed to open file for appending" : "Failed to open file for writing";
                var errMsg = builder.BuildGlobalStringPtr(errString, isAppend ? "err_fa_msg" : "err_fw_msg");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(errMsg, builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Merge
                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(resStructType, resAlloca, isAppend ? "fa_res" : "fw_res");
                return true;
            }

            case "file_write_bin":
            {
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);
                var fwriteFunc = GetOrDeclareCrtFunc(module, "fwrite", context.Int64Type, new[] { i8PtrType, context.Int64Type, context.Int64Type, i8PtrType });
                var fwriteType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fwriteFunc);

                var resStructType = MapType(context, "Result<bool, string>", ecs);
                var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "fwb_res_alloca");

                var modeVal = builder.BuildGlobalStringPtr("wb", "mode_fwb_wb");
                var fp = builder.BuildCall2(fopenType, fopenFunc, new[] { pathVal, modeVal }, "fwb_fp");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isNotNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, fp, nullPtr, "fwb_not_null");

                var writeBB = function.AppendBasicBlock("fwb_body");
                var errBB = function.AppendBasicBlock("fwb_err");
                var mergeBB = function.AppendBasicBlock("fwb_merge");

                builder.BuildCondBr(isNotNull, writeBB, errBB);

                // Body block (Atomic locked write of raw bytes)
                builder.PositionAtEnd(writeBB);
                EmitLockFile(module, builder, fp, i8PtrType, context);

                var dynArrStructType = MapType(context, "[u8]", ecs);
                LLVMValueRef dynStructPtr;
                if (call.Arguments[1] is IdentifierExpression id && locals.TryGetValue(id.Name, out var idPtr))
                {
                    dynStructPtr = idPtr;
                }
                else
                {
                    var bytesVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "bytes_wb_tmp");
                    builder.BuildStore(bytesVal, tempAlloca);
                    dynStructPtr = tempAlloca;
                }

                var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                var dataPtr = builder.BuildLoad2(i8PtrType, dataSlot, "bytes_data_ptr");
                var len32 = builder.BuildLoad2(context.Int32Type, lenSlot, "bytes_len_32");
                var len64 = builder.BuildZExt(len32, context.Int64Type, "bytes_len_64");

                builder.BuildCall2(fwriteType, fwriteFunc, new[] { dataPtr, LLVMValueRef.CreateConstInt(context.Int64Type, 1), len64, fp }, "bytes_written");
                EmitUnlockFile(module, builder, fp, i8PtrType, context);
                builder.BuildCall2(fcloseType, fcloseFunc, new[] { fp }, "");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(i8PtrType), builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Error block
                builder.PositionAtEnd(errBB);
                var errMsg = builder.BuildGlobalStringPtr("Failed to open binary file for writing", "err_fwb_msg");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(errMsg, builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Merge
                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(resStructType, resAlloca, "fwb_res");
                return true;
            }

            case "file_read_text":
            {
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);
                var freadFunc = GetOrDeclareCrtFunc(module, "fread", context.Int64Type, new[] { i8PtrType, context.Int64Type, context.Int64Type, i8PtrType });
                var freadType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(freadFunc);
                var mallocFunc = GetOrDeclareCrtFunc(module, "malloc", i8PtrType, new[] { context.Int64Type });
                var mallocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(mallocFunc);

                var resStructType = MapType(context, "Result<string, string>", ecs);
                var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "fr_res_alloca");

                var modeVal = builder.BuildGlobalStringPtr("rb", "mode_fr_rb");
                var fp = builder.BuildCall2(fopenType, fopenFunc, new[] { pathVal, modeVal }, "fr_fp");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isNotNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, fp, nullPtr, "fr_not_null");

                var readBB = function.AppendBasicBlock("fr_body");
                var errBB = function.AppendBasicBlock("fr_err");
                var mergeBB = function.AppendBasicBlock("fr_merge");

                builder.BuildCondBr(isNotNull, readBB, errBB);

                // Error block
                builder.PositionAtEnd(errBB);
                var errMsg = builder.BuildGlobalStringPtr("Failed to open file for reading", "err_fr_msg");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(i8PtrType), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(errMsg, builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Read block (Atomic locked read with 64-bit offsets)
                builder.PositionAtEnd(readBB);
                EmitLockFile(module, builder, fp, i8PtrType, context);

                var fileSize64 = EmitSeekEndAndGetSize64(module, builder, fp, i8PtrType, context);

                var allocSize = builder.BuildAdd(fileSize64, LLVMValueRef.CreateConstInt(context.Int64Type, 1), "alloc_size");
                var buf = builder.BuildCall2(mallocType, mallocFunc, new[] { allocSize }, "read_buf");

                var bytesRead = builder.BuildCall2(freadType, freadFunc, new[] { buf, LLVMValueRef.CreateConstInt(context.Int64Type, 1), fileSize64, fp }, "bytes_read");
                var termSlot = builder.BuildInBoundsGEP2(context.Int8Type, buf, new[] { bytesRead }, "term_slot");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), termSlot);

                EmitUnlockFile(module, builder, fp, i8PtrType, context);
                builder.BuildCall2(fcloseType, fcloseFunc, new[] { fp }, "");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(buf, builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(i8PtrType), builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Merge
                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(resStructType, resAlloca, "fr_res");
                return true;
            }

            case "file_read_bin":
            {
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);
                var freadFunc = GetOrDeclareCrtFunc(module, "fread", context.Int64Type, new[] { i8PtrType, context.Int64Type, context.Int64Type, i8PtrType });
                var freadType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(freadFunc);
                var mallocFunc = GetOrDeclareCrtFunc(module, "malloc", i8PtrType, new[] { context.Int64Type });
                var mallocType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(mallocFunc);

                var dynArrStructType = MapType(context, "[u8]", ecs);
                var resStructType = MapType(context, "Result<Vec<u8>, string>", ecs);
                var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "frb_res_alloca");

                var modeVal = builder.BuildGlobalStringPtr("rb", "mode_frb_rb");
                var fp = builder.BuildCall2(fopenType, fopenFunc, new[] { pathVal, modeVal }, "frb_fp");
                var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
                var isNotNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, fp, nullPtr, "frb_not_null");

                var readBB = function.AppendBasicBlock("frb_body");
                var errBB = function.AppendBasicBlock("frb_err");
                var mergeBB = function.AppendBasicBlock("frb_merge");

                builder.BuildCondBr(isNotNull, readBB, errBB);

                // Error block
                builder.PositionAtEnd(errBB);
                var errMsg = builder.BuildGlobalStringPtr("Failed to open binary file for reading", "err_frb_msg");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(dynArrStructType), builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(errMsg, builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Read block (Atomic locked read with 64-bit offsets into Vec<u8>)
                builder.PositionAtEnd(readBB);
                EmitLockFile(module, builder, fp, i8PtrType, context);

                var fileSize64 = EmitSeekEndAndGetSize64(module, builder, fp, i8PtrType, context);

                // Allocate at least 1 byte if empty file
                var isZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, fileSize64, LLVMValueRef.CreateConstInt(context.Int64Type, 0), "is_zero_len");
                var safeAllocSize = builder.BuildSelect(isZero, LLVMValueRef.CreateConstInt(context.Int64Type, 1), fileSize64, "safe_bin_alloc");
                var buf = builder.BuildCall2(mallocType, mallocFunc, new[] { safeAllocSize }, "read_bin_buf");

                var bytesRead64 = builder.BuildCall2(freadType, freadFunc, new[] { buf, LLVMValueRef.CreateConstInt(context.Int64Type, 1), fileSize64, fp }, "bytes_read_bin");

                EmitUnlockFile(module, builder, fp, i8PtrType, context);
                builder.BuildCall2(fcloseType, fcloseFunc, new[] { fp }, "");

                // Construct Vec<u8> struct { data: i8*, len: i32, cap: i32 }
                var len32 = builder.BuildTrunc(bytesRead64, context.Int32Type, "bin_len_32");
                var vecAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "vec_bin_res");
                var p0 = builder.BuildStructGEP2(dynArrStructType, vecAlloca, 0, "vec_data");
                var p1 = builder.BuildStructGEP2(dynArrStructType, vecAlloca, 1, "vec_len");
                var p2 = builder.BuildStructGEP2(dynArrStructType, vecAlloca, 2, "vec_cap");
                builder.BuildStore(buf, p0);
                builder.BuildStore(len32, p1);
                builder.BuildStore(len32, p2);

                var vecVal = builder.BuildLoad2(dynArrStructType, vecAlloca, "vec_val");

                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag"));
                builder.BuildStore(vecVal, builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_val"));
                builder.BuildStore(LLVMValueRef.CreateConstNull(i8PtrType), builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_val"));
                builder.BuildBr(mergeBB);

                // Merge
                builder.PositionAtEnd(mergeBB);
                result = builder.BuildLoad2(resStructType, resAlloca, "frb_res");
                return true;
            }

            default:
                result = default;
                return false;
        }
    }

    private unsafe void EmitLockFile(LLVMModuleRef module, LLVMBuilderRef builder, LLVMValueRef fp, LLVMTypeRef i8PtrType, LLVMContextRef context)
    {
        string lockFuncName = _options.Target.IsWindows ? "_lock_file" : "flockfile";
        var lockFunc = GetOrDeclareCrtFunc(module, lockFuncName, context.VoidType, new[] { i8PtrType });
        var lockType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(lockFunc);
        builder.BuildCall2(lockType, lockFunc, new[] { fp }, "");
    }

    private unsafe void EmitUnlockFile(LLVMModuleRef module, LLVMBuilderRef builder, LLVMValueRef fp, LLVMTypeRef i8PtrType, LLVMContextRef context)
    {
        string unlockFuncName = _options.Target.IsWindows ? "_unlock_file" : "funlockfile";
        var unlockFunc = GetOrDeclareCrtFunc(module, unlockFuncName, context.VoidType, new[] { i8PtrType });
        var unlockType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(unlockFunc);
        builder.BuildCall2(unlockType, unlockFunc, new[] { fp }, "");
    }

    private unsafe LLVMValueRef EmitSeekEndAndGetSize64(
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef fp,
        LLVMTypeRef i8PtrType,
        LLVMContextRef context)
    {
        string seekFuncName = _options.Target.IsWindows ? "_fseeki64" : "fseeko";
        string tellFuncName = _options.Target.IsWindows ? "_ftelli64" : "ftello";

        var seekFunc = GetOrDeclareCrtFunc(module, seekFuncName, context.Int32Type, new[] { i8PtrType, context.Int64Type, context.Int32Type });
        var seekType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(seekFunc);
        var tellFunc = GetOrDeclareCrtFunc(module, tellFuncName, context.Int64Type, new[] { i8PtrType });
        var tellType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(tellFunc);

        // Seek to end: seek(fp, 0L, 2) [SEEK_END = 2]
        builder.BuildCall2(seekType, seekFunc, new[] { fp, LLVMValueRef.CreateConstInt(context.Int64Type, 0), LLVMValueRef.CreateConstInt(context.Int32Type, 2) }, "seek_end_64");
        var fileSize64 = builder.BuildCall2(tellType, tellFunc, new[] { fp }, "file_size_64");
        // Seek to start: seek(fp, 0L, 0) [SEEK_SET = 0]
        builder.BuildCall2(seekType, seekFunc, new[] { fp, LLVMValueRef.CreateConstInt(context.Int64Type, 0), LLVMValueRef.CreateConstInt(context.Int32Type, 0) }, "seek_set_64");

        return fileSize64;
    }

    private unsafe LLVMValueRef GetOrDeclareCrtFunc(LLVMModuleRef module, string name, LLVMTypeRef returnType, LLVMTypeRef[] paramTypes)
    {
        var fn = module.GetNamedFunction(name);
        if (fn.Handle != IntPtr.Zero) return fn;
        var fnType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
        return module.AddFunction(name, fnType);
    }
}
