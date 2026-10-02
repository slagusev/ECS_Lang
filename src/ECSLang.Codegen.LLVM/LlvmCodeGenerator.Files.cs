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

                // Body block
                builder.PositionAtEnd(writeBB);
                var contentLen = builder.BuildCall2(strlenType, strlenFunc, new[] { contentVal }, "content_len");
                builder.BuildCall2(fwriteType, fwriteFunc, new[] { contentVal, LLVMValueRef.CreateConstInt(context.Int64Type, 1), contentLen, fp }, "written_count");
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

            case "file_read_text":
            {
                var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                var fopenFunc = GetOrDeclareCrtFunc(module, "fopen", i8PtrType, new[] { i8PtrType, i8PtrType });
                var fopenType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fopenFunc);
                var fcloseFunc = GetOrDeclareCrtFunc(module, "fclose", context.Int32Type, new[] { i8PtrType });
                var fcloseType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fcloseFunc);
                var fseekFunc = GetOrDeclareCrtFunc(module, "fseek", context.Int32Type, new[] { i8PtrType, context.Int64Type, context.Int32Type });
                var fseekType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(fseekFunc);
                var ftellFunc = GetOrDeclareCrtFunc(module, "ftell", context.Int64Type, new[] { i8PtrType });
                var ftellType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ftellFunc);
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

                // Read block
                builder.PositionAtEnd(readBB);
                // Seek to end: fseek(fp, 0, 2)
                builder.BuildCall2(fseekType, fseekFunc, new[] { fp, LLVMValueRef.CreateConstInt(context.Int64Type, 0), LLVMValueRef.CreateConstInt(context.Int32Type, 2) }, "seek_end");
                var fileSize = builder.BuildCall2(ftellType, ftellFunc, new[] { fp }, "file_size");
                // Seek to start: fseek(fp, 0, 0)
                builder.BuildCall2(fseekType, fseekFunc, new[] { fp, LLVMValueRef.CreateConstInt(context.Int64Type, 0), LLVMValueRef.CreateConstInt(context.Int32Type, 0) }, "seek_set");

                var allocSize = builder.BuildAdd(fileSize, LLVMValueRef.CreateConstInt(context.Int64Type, 1), "alloc_size");
                var buf = builder.BuildCall2(mallocType, mallocFunc, new[] { allocSize }, "read_buf");

                var bytesRead = builder.BuildCall2(freadType, freadFunc, new[] { buf, LLVMValueRef.CreateConstInt(context.Int64Type, 1), fileSize, fp }, "bytes_read");
                var termSlot = builder.BuildInBoundsGEP2(context.Int8Type, buf, new[] { bytesRead }, "term_slot");
                builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), termSlot);

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

            default:
                result = default;
                return false;
        }
    }

    private unsafe LLVMValueRef GetOrDeclareCrtFunc(LLVMModuleRef module, string name, LLVMTypeRef returnType, LLVMTypeRef[] paramTypes)
    {
        var fn = module.GetNamedFunction(name);
        if (fn.Handle != IntPtr.Zero) return fn;
        var fnType = LLVMTypeRef.CreateFunction(returnType, paramTypes, false);
        return module.AddFunction(name, fnType);
    }
}
