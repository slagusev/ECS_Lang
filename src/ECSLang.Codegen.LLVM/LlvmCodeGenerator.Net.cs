using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private void EmitNetworkDeclarations(LLVMContextRef context, LLVMModuleRef module)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var i32Type = context.Int32Type;
        var i64Type = context.Int64Type;
        var i16Type = context.Int16Type;
        var voidType = context.VoidType;

        var strlenFunc = module.GetNamedFunction("strlen");
        var mallocFunc = module.GetNamedFunction("malloc");
        var freeFunc = module.GetNamedFunction("free");
        var memsetFunc = module.GetNamedFunction("memset");

        var strlenType = LLVMTypeRef.CreateFunction(i64Type, new[] { i8PtrType }, false);
        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i64Type }, false);
        var freeType = LLVMTypeRef.CreateFunction(voidType, new[] { i8PtrType }, false);
        var memsetType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i32Type, i64Type }, false);

        if (strlenFunc.Handle == IntPtr.Zero) strlenFunc = module.AddFunction("strlen", strlenType);
        if (mallocFunc.Handle == IntPtr.Zero) mallocFunc = module.AddFunction("malloc", mallocType);
        if (freeFunc.Handle == IntPtr.Zero) freeFunc = module.AddFunction("free", freeType);
        if (memsetFunc.Handle == IntPtr.Zero) memsetFunc = module.AddFunction("memset", memsetType);

        var memcpyType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, i64Type }, false);
        var memcpyFunc = module.GetNamedFunction("memcpy");
        if (memcpyFunc.Handle == IntPtr.Zero) memcpyFunc = module.AddFunction("memcpy", memcpyType);

        var memmoveType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i8PtrType, i64Type }, false);
        var memmoveFunc = module.GetNamedFunction("memmove");
        if (memmoveFunc.Handle == IntPtr.Zero) memmoveFunc = module.AddFunction("memmove", memmoveType);

        var reallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, i64Type }, false);
        var reallocFunc = module.GetNamedFunction("realloc");
        if (reallocFunc.Handle == IntPtr.Zero) reallocFunc = module.AddFunction("realloc", reallocType);

        var htonlType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
        var htonlFunc = module.GetNamedFunction("htonl");
        if (htonlFunc.Handle == IntPtr.Zero) htonlFunc = module.AddFunction("htonl", htonlType);

        var ntohlType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
        var ntohlFunc = module.GetNamedFunction("ntohl");
        if (ntohlFunc.Handle == IntPtr.Zero) ntohlFunc = module.AddFunction("ntohl", ntohlType);

        var vecU8Type = context.GetStructType(new[] { i8PtrType, i32Type, i32Type }, false);

        bool isWindows = _options.Target.IsWindows;

        // Raw C socket API declarations
        LLVMValueRef wsaStartupFunc = default;
        LLVMValueRef wsaCleanupFunc = default;
        LLVMValueRef wsaGetLastErrorFunc = default;
        LLVMValueRef ioctlFunc = default;
        LLVMValueRef closeFunc = default;

        var socketType = LLVMTypeRef.CreateFunction(i64Type, new[] { i32Type, i32Type, i32Type }, false);
        var socketFunc = module.AddFunction("socket", socketType);

        var bindType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type }, false);
        var bindFunc = module.AddFunction("bind", bindType);

        var listenType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type }, false);
        var listenFunc = module.AddFunction("listen", listenType);

        var acceptType = LLVMTypeRef.CreateFunction(i64Type, new[] { i64Type, i8PtrType, i8PtrType }, false);
        var acceptFunc = module.AddFunction("accept", acceptType);

        var connectType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type }, false);
        var connectFunc = module.AddFunction("connect", connectType);

        var sendType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type, i32Type }, false);
        var sendFunc = module.AddFunction("send", sendType);

        var recvType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type, i32Type }, false);
        var recvFunc = module.AddFunction("recv", recvType);

        var sendtoType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type, i32Type, i8PtrType, i32Type }, false);
        var sendtoFunc = module.AddFunction("sendto", sendtoType);

        var recvfromType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i8PtrType, i32Type, i32Type, i8PtrType, i8PtrType }, false);
        var recvfromFunc = module.AddFunction("recvfrom", recvfromType);

        var setsockoptType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i32Type, i8PtrType, i32Type }, false);
        var setsockoptFunc = module.AddFunction("setsockopt", setsockoptType);

        var htonsType = LLVMTypeRef.CreateFunction(i16Type, new[] { i16Type }, false);
        var htonsFunc = module.AddFunction("htons", htonsType);

        var inetAddrType = LLVMTypeRef.CreateFunction(i32Type, new[] { i8PtrType }, false);
        var inetAddrFunc = module.AddFunction("inet_addr", inetAddrType);

        if (isWindows)
        {
            var wsaStartupType = LLVMTypeRef.CreateFunction(i32Type, new[] { i16Type, i8PtrType }, false);
            wsaStartupFunc = module.AddFunction("WSAStartup", wsaStartupType);

            var wsaCleanupType = LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false);
            wsaCleanupFunc = module.AddFunction("WSACleanup", wsaCleanupType);

            var wsaGetLastErrorType = LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false);
            wsaGetLastErrorFunc = module.AddFunction("WSAGetLastError", wsaGetLastErrorType);

            var ioctlsocketType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i8PtrType }, false);
            ioctlFunc = module.AddFunction("ioctlsocket", ioctlsocketType);

            var closesocketType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type }, false);
            closeFunc = module.AddFunction("closesocket", closesocketType);
        }
        else
        {
            var closeType = LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type }, false);
            closeFunc = module.AddFunction("close", closeType);
        }

        // Helper builder for emitting ecs_net_* wrappers
        using var netBuilder = context.CreateBuilder();

        // 1. ecs_net_init() -> i32
        var netInitType = LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false);
        var netInitFunc = module.AddFunction("ecs_net_init", netInitType);
        var netInitBB = netInitFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(netInitBB);
        if (isWindows)
        {
            var wsaDataArr = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 512), "wsa_data");
            var wsaDataPtr = netBuilder.BuildBitCast(wsaDataArr, i8PtrType, "wsa_ptr");
            var res = netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, new[] { i16Type, i8PtrType }, false),
                wsaStartupFunc,
                new[] { LLVMValueRef.CreateConstInt(i16Type, 0x0202, false), wsaDataPtr },
                "wsa_res");
            netBuilder.BuildRet(res);
        }
        else
        {
            netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, 0, false));
        }

        // 2. ecs_net_cleanup() -> void
        var netCleanupType = LLVMTypeRef.CreateFunction(voidType, Array.Empty<LLVMTypeRef>(), false);
        var netCleanupFunc = module.AddFunction("ecs_net_cleanup", netCleanupType);
        var netCleanupBB = netCleanupFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(netCleanupBB);
        if (isWindows)
        {
            netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false),
                wsaCleanupFunc,
                Array.Empty<LLVMValueRef>(),
                "");
        }
        netBuilder.BuildRetVoid();

        // 3. ecs_net_tcp_listen(port: i32) -> i32
        var netListenType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
        var netListenFunc = module.AddFunction("ecs_net_tcp_listen", netListenType);
        var listenBB = netListenFunc.AppendBasicBlock("entry");
        var listenErrBB = netListenFunc.AppendBasicBlock("err");
        var listenOkBB = netListenFunc.AppendBasicBlock("ok");
        netBuilder.PositionAtEnd(listenBB);
        var portParam = netListenFunc.GetParam(0);

        var sVal = netBuilder.BuildCall2(
            socketType,
            socketFunc,
            new[] { LLVMValueRef.CreateConstInt(i32Type, 2), LLVMValueRef.CreateConstInt(i32Type, 1), LLVMValueRef.CreateConstInt(i32Type, 0) },
            "sock");
        var sInvalid = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, sVal, LLVMValueRef.CreateConstInt(i64Type, 0), "s_invalid");
        netBuilder.BuildCondBr(sInvalid, listenErrBB, listenOkBB);

        netBuilder.PositionAtEnd(listenErrBB);
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(listenOkBB);
        if (!isWindows)
        {
            var optValAlloca = netBuilder.BuildAlloca(i32Type, "opt_one");
            netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 1), optValAlloca);
            var optValPtr = netBuilder.BuildBitCast(optValAlloca, i8PtrType, "opt_ptr");
            int solSocket = 1;
            int soReuseAddr = 2;
            netBuilder.BuildCall2(
                setsockoptType,
                setsockoptFunc,
                new[] { sVal, LLVMValueRef.CreateConstInt(i32Type, (ulong)solSocket), LLVMValueRef.CreateConstInt(i32Type, (ulong)soReuseAddr), optValPtr, LLVMValueRef.CreateConstInt(i32Type, 4) },
                "");
        }

        var addrAlloca = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "sin_addr");
        var addrPtr = netBuilder.BuildBitCast(addrAlloca, i8PtrType, "sin_ptr");
        netBuilder.BuildCall2(
            memsetType,
            memsetFunc,
            new[] { addrPtr, LLVMValueRef.CreateConstInt(i32Type, 0), LLVMValueRef.CreateConstInt(i64Type, 16) },
            "");

        var familyPtr = netBuilder.BuildBitCast(addrPtr, LLVMTypeRef.CreatePointer(i16Type, 0), "fam_ptr");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i16Type, 2), familyPtr);

        var portI16 = netBuilder.BuildTrunc(portParam, i16Type, "port_i16");
        var netPort = netBuilder.BuildCall2(htonsType, htonsFunc, new[] { portI16 }, "net_port");
        var portPtrRaw = netBuilder.BuildGEP2(context.Int8Type, addrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 2) }, "p_raw");
        var portPtr = netBuilder.BuildBitCast(portPtrRaw, LLVMTypeRef.CreatePointer(i16Type, 0), "p_ptr");
        netBuilder.BuildStore(netPort, portPtr);

        var inAddrRaw = netBuilder.BuildGEP2(context.Int8Type, addrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "in_raw");
        var inAddrPtr = netBuilder.BuildBitCast(inAddrRaw, LLVMTypeRef.CreatePointer(i32Type, 0), "in_ptr");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 0), inAddrPtr);

        var listenErrCleanBB = netListenFunc.AppendBasicBlock("listen_err_clean");
        var bindOkBB = netListenFunc.AppendBasicBlock("bind_ok");
        var bindRes = netBuilder.BuildCall2(bindType, bindFunc, new[] { sVal, addrPtr, LLVMValueRef.CreateConstInt(i32Type, 16) }, "bind_res");
        var bindFailed = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, bindRes, LLVMValueRef.CreateConstInt(i32Type, 0), "bind_failed");
        netBuilder.BuildCondBr(bindFailed, listenErrCleanBB, bindOkBB);

        netBuilder.PositionAtEnd(bindOkBB);
        var listenRes = netBuilder.BuildCall2(listenType, listenFunc, new[] { sVal, LLVMValueRef.CreateConstInt(i32Type, 128) }, "listen_res");
        var listenFailed = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, listenRes, LLVMValueRef.CreateConstInt(i32Type, 0), "listen_failed");
        var listenSuccessBB = netListenFunc.AppendBasicBlock("listen_success");
        netBuilder.BuildCondBr(listenFailed, listenErrCleanBB, listenSuccessBB);

        netBuilder.PositionAtEnd(listenErrCleanBB);
        netBuilder.BuildCall2(LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type }, false), closeFunc, new[] { sVal }, "");
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(listenSuccessBB);
        if (isWindows)
        {
            var nonBlockAlloca = netBuilder.BuildAlloca(i32Type, "nb_one");
            netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 1), nonBlockAlloca);
            var nbPtr = netBuilder.BuildBitCast(nonBlockAlloca, i8PtrType, "nb_ptr");
            netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i8PtrType }, false),
                ioctlFunc,
                new[] { sVal, LLVMValueRef.CreateConstInt(i32Type, 0x8004667E), nbPtr },
                "");
        }
        var sVal32 = netBuilder.BuildTrunc(sVal, i32Type, "s_val32");
        netBuilder.BuildRet(sVal32);

        // 4. ecs_net_tcp_connect(ip: ptr, port: i32) -> i32
        var netConnectType = LLVMTypeRef.CreateFunction(i32Type, new[] { i8PtrType, i32Type }, false);
        var netConnectFunc = module.AddFunction("ecs_net_tcp_connect", netConnectType);
        var connBB = netConnectFunc.AppendBasicBlock("entry");
        var connErrBB = netConnectFunc.AppendBasicBlock("err");
        var connOkBB = netConnectFunc.AppendBasicBlock("ok");
        netBuilder.PositionAtEnd(connBB);
        var cIpParam = netConnectFunc.GetParam(0);
        var cPortParam = netConnectFunc.GetParam(1);

        var csVal = netBuilder.BuildCall2(
            socketType,
            socketFunc,
            new[] { LLVMValueRef.CreateConstInt(i32Type, 2), LLVMValueRef.CreateConstInt(i32Type, 1), LLVMValueRef.CreateConstInt(i32Type, 0) },
            "c_sock");
        var csInvalid = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, csVal, LLVMValueRef.CreateConstInt(i64Type, 0), "cs_invalid");
        netBuilder.BuildCondBr(csInvalid, connErrBB, connOkBB);

        netBuilder.PositionAtEnd(connErrBB);
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(connOkBB);
        var cAddrAlloca = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "c_addr");
        var cAddrPtr = netBuilder.BuildBitCast(cAddrAlloca, i8PtrType, "c_addr_ptr");
        netBuilder.BuildCall2(memsetType, memsetFunc, new[] { cAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 0), LLVMValueRef.CreateConstInt(i64Type, 16) }, "");

        var cFamPtr = netBuilder.BuildBitCast(cAddrPtr, LLVMTypeRef.CreatePointer(i16Type, 0), "c_fam");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i16Type, 2), cFamPtr);

        var cPortI16 = netBuilder.BuildTrunc(cPortParam, i16Type, "c_port_i16");
        var cNetPort = netBuilder.BuildCall2(htonsType, htonsFunc, new[] { cPortI16 }, "c_net_port");
        var cPortRaw = netBuilder.BuildGEP2(context.Int8Type, cAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 2) }, "");
        var cPortPtr = netBuilder.BuildBitCast(cPortRaw, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(cNetPort, cPortPtr);

        var cInAddr = netBuilder.BuildCall2(inetAddrType, inetAddrFunc, new[] { cIpParam }, "ip_parsed");
        var cInRaw = netBuilder.BuildGEP2(context.Int8Type, cAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "");
        var cInPtr = netBuilder.BuildBitCast(cInRaw, LLVMTypeRef.CreatePointer(i32Type, 0), "");
        netBuilder.BuildStore(cInAddr, cInPtr);

        var connRes = netBuilder.BuildCall2(connectType, connectFunc, new[] { csVal, cAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 16) }, "conn_res");

        if (isWindows)
        {
            var cNbAlloca = netBuilder.BuildAlloca(i32Type, "c_nb");
            netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 1), cNbAlloca);
            var cNbPtr = netBuilder.BuildBitCast(cNbAlloca, i8PtrType, "");
            netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i8PtrType }, false),
                ioctlFunc,
                new[] { csVal, LLVMValueRef.CreateConstInt(i32Type, 0x8004667E), cNbPtr },
                "");
        }

        var connFailed = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, connRes, LLVMValueRef.CreateConstInt(i32Type, 0), "conn_failed");
        var connCheckErrBB = netConnectFunc.AppendBasicBlock("conn_check_err");
        var connSuccessBB = netConnectFunc.AppendBasicBlock("conn_success");
        netBuilder.BuildCondBr(connFailed, connCheckErrBB, connSuccessBB);

        netBuilder.PositionAtEnd(connCheckErrBB);
        if (isWindows)
        {
            var lastErr = netBuilder.BuildCall2(LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false), wsaGetLastErrorFunc, Array.Empty<LLVMValueRef>(), "last_err");
            var wouldBlock = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, lastErr, LLVMValueRef.CreateConstInt(i32Type, 10035), "is_wb");
            var connFailCleanBB = netConnectFunc.AppendBasicBlock("conn_fail_clean");
            netBuilder.BuildCondBr(wouldBlock, connSuccessBB, connFailCleanBB);

            netBuilder.PositionAtEnd(connFailCleanBB);
            netBuilder.BuildCall2(LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type }, false), closeFunc, new[] { csVal }, "");
            netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));
        }
        else
        {
            var csVal32 = netBuilder.BuildTrunc(csVal, i32Type, "");
            netBuilder.BuildRet(csVal32);
        }

        netBuilder.PositionAtEnd(connSuccessBB);
        var csOk32 = netBuilder.BuildTrunc(csVal, i32Type, "");
        netBuilder.BuildRet(csOk32);

        // 5. ecs_net_tcp_accept(listener_fd: i32) -> i32
        var netAcceptType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
        var netAcceptFunc = module.AddFunction("ecs_net_tcp_accept", netAcceptType);
        var acceptBB = netAcceptFunc.AppendBasicBlock("entry");
        var acceptOkBB = netAcceptFunc.AppendBasicBlock("ok");
        var acceptErrBB = netAcceptFunc.AppendBasicBlock("err");
        netBuilder.PositionAtEnd(acceptBB);
        var lFdParam = netAcceptFunc.GetParam(0);
        var lFd64 = netBuilder.BuildZExt(lFdParam, i64Type, "l_fd64");

        var nullPtr = LLVMValueRef.CreateConstPointerNull(i8PtrType);
        var clientFd = netBuilder.BuildCall2(acceptType, acceptFunc, new[] { lFd64, nullPtr, nullPtr }, "client_fd");
        var clientInvalid = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, clientFd, LLVMValueRef.CreateConstInt(i64Type, 0), "client_invalid");
        netBuilder.BuildCondBr(clientInvalid, acceptErrBB, acceptOkBB);

        netBuilder.PositionAtEnd(acceptErrBB);
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(acceptOkBB);
        if (isWindows)
        {
            var clNbAlloca = netBuilder.BuildAlloca(i32Type, "cl_nb");
            netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 1), clNbAlloca);
            var clNbPtr = netBuilder.BuildBitCast(clNbAlloca, i8PtrType, "");
            netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i8PtrType }, false),
                ioctlFunc,
                new[] { clientFd, LLVMValueRef.CreateConstInt(i32Type, 0x8004667E), clNbPtr },
                "");
        }
        var clientFd32 = netBuilder.BuildTrunc(clientFd, i32Type, "client_fd32");
        netBuilder.BuildRet(clientFd32);

        // 6. ecs_net_tcp_send(fd: i32, data: ptr) -> i32
        var netSendType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type, i8PtrType }, false);
        var netSendFunc = module.AddFunction("ecs_net_tcp_send", netSendType);
        var sendBB = netSendFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(sendBB);
        var sFdParam = netSendFunc.GetParam(0);
        var sFd64 = netBuilder.BuildZExt(sFdParam, i64Type, "");
        var sDataParam = netSendFunc.GetParam(1);

        var dataLen64 = netBuilder.BuildCall2(strlenType, strlenFunc, new[] { sDataParam }, "data_len64");
        var dataLen32 = netBuilder.BuildTrunc(dataLen64, i32Type, "data_len32");
        var sentBytes = netBuilder.BuildCall2(sendType, sendFunc, new[] { sFd64, sDataParam, dataLen32, LLVMValueRef.CreateConstInt(i32Type, 0) }, "sent");
        netBuilder.BuildRet(sentBytes);

        // 7. ecs_net_tcp_recv(fd: i32, max_len: i32) -> ptr
        var netRecvType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i32Type, i32Type }, false);
        var netRecvFunc = module.AddFunction("ecs_net_tcp_recv", netRecvType);
        var recvBB = netRecvFunc.AppendBasicBlock("entry");
        var recvHasDataBB = netRecvFunc.AppendBasicBlock("has_data");
        var recvEmptyBB = netRecvFunc.AppendBasicBlock("empty");
        netBuilder.PositionAtEnd(recvBB);
        var rFdParam = netRecvFunc.GetParam(0);
        var rFd64 = netBuilder.BuildZExt(rFdParam, i64Type, "");
        var rMaxLenParam = netRecvFunc.GetParam(1);

        var maxLenPos = netBuilder.BuildSelect(
            netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, rMaxLenParam, LLVMValueRef.CreateConstInt(i32Type, 0), ""),
            rMaxLenParam,
            LLVMValueRef.CreateConstInt(i32Type, 4096),
            "max_len_clean");

        var allocSize = netBuilder.BuildAdd(maxLenPos, LLVMValueRef.CreateConstInt(i32Type, 1), "alloc_sz");
        var allocSize64 = netBuilder.BuildZExt(allocSize, i64Type, "alloc_sz64");
        var rBuf = netBuilder.BuildCall2(mallocType, mallocFunc, new[] { allocSize64 }, "recv_buf");

        var rRead = netBuilder.BuildCall2(recvType, recvFunc, new[] { rFd64, rBuf, maxLenPos, LLVMValueRef.CreateConstInt(i32Type, 0) }, "read_bytes");
        var hasData = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, rRead, LLVMValueRef.CreateConstInt(i32Type, 0), "has_data");
        netBuilder.BuildCondBr(hasData, recvHasDataBB, recvEmptyBB);

        netBuilder.PositionAtEnd(recvHasDataBB);
        var rRead64 = netBuilder.BuildSExt(rRead, i64Type, "r_read64");
        var termPtr = netBuilder.BuildGEP2(context.Int8Type, rBuf, new[] { rRead64 }, "term_ptr");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), termPtr);
        netBuilder.BuildRet(rBuf);

        netBuilder.PositionAtEnd(recvEmptyBB);
        netBuilder.BuildCall2(freeType, freeFunc, new[] { rBuf }, "");
        var emptyConst = netBuilder.BuildGlobalStringPtr("", "net_empty");
        netBuilder.BuildRet(emptyConst);

        // 8. ecs_net_udp_bind(port: i32) -> i32
        var netUdpBindType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
        var netUdpBindFunc = module.AddFunction("ecs_net_udp_bind", netUdpBindType);
        var uBindBB = netUdpBindFunc.AppendBasicBlock("entry");
        var uBindErrBB = netUdpBindFunc.AppendBasicBlock("err");
        var uBindOkBB = netUdpBindFunc.AppendBasicBlock("ok");
        netBuilder.PositionAtEnd(uBindBB);
        var uPortParam = netUdpBindFunc.GetParam(0);

        var usVal = netBuilder.BuildCall2(
            socketType,
            socketFunc,
            new[] { LLVMValueRef.CreateConstInt(i32Type, 2), LLVMValueRef.CreateConstInt(i32Type, 2), LLVMValueRef.CreateConstInt(i32Type, 0) },
            "u_sock");
        var usInvalid = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, usVal, LLVMValueRef.CreateConstInt(i64Type, 0), "");
        netBuilder.BuildCondBr(usInvalid, uBindErrBB, uBindOkBB);

        netBuilder.PositionAtEnd(uBindErrBB);
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(uBindOkBB);
        if (isWindows)
        {
            var uNbAlloca = netBuilder.BuildAlloca(i32Type, "u_nb");
            netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 1), uNbAlloca);
            var uNbPtr = netBuilder.BuildBitCast(uNbAlloca, i8PtrType, "");
            netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type, i32Type, i8PtrType }, false),
                ioctlFunc,
                new[] { usVal, LLVMValueRef.CreateConstInt(i32Type, 0x8004667E), uNbPtr },
                "");
        }

        var uAddrAlloca = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "u_addr");
        var uAddrPtr = netBuilder.BuildBitCast(uAddrAlloca, i8PtrType, "");
        netBuilder.BuildCall2(memsetType, memsetFunc, new[] { uAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 0), LLVMValueRef.CreateConstInt(i64Type, 16) }, "");

        var uFamPtr = netBuilder.BuildBitCast(uAddrPtr, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i16Type, 2), uFamPtr);

        var uPortI16 = netBuilder.BuildTrunc(uPortParam, i16Type, "");
        var uNetPort = netBuilder.BuildCall2(htonsType, htonsFunc, new[] { uPortI16 }, "");
        var uPortRaw = netBuilder.BuildGEP2(context.Int8Type, uAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 2) }, "");
        var uPortPtr = netBuilder.BuildBitCast(uPortRaw, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(uNetPort, uPortPtr);

        var uInRaw = netBuilder.BuildGEP2(context.Int8Type, uAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "");
        var uInPtr = netBuilder.BuildBitCast(uInRaw, LLVMTypeRef.CreatePointer(i32Type, 0), "");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 0), uInPtr);

        netBuilder.BuildCall2(bindType, bindFunc, new[] { usVal, uAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 16) }, "");
        var usVal32 = netBuilder.BuildTrunc(usVal, i32Type, "");
        netBuilder.BuildRet(usVal32);

        // 8b. ecs_net_udp_connect(fd: i32, ip: ptr, port: i32) -> i32
        var netUdpConnectType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type, i8PtrType, i32Type }, false);
        var netUdpConnectFunc = module.AddFunction("ecs_net_udp_connect", netUdpConnectType);
        var ucBB = netUdpConnectFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(ucBB);
        var ucFd = netUdpConnectFunc.GetParam(0);
        var ucFd64 = netBuilder.BuildZExt(ucFd, i64Type, "");
        var ucIp = netUdpConnectFunc.GetParam(1);
        var ucPort = netUdpConnectFunc.GetParam(2);

        var ucAddrAlloca = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "uc_addr");
        var ucAddrPtr = netBuilder.BuildBitCast(ucAddrAlloca, i8PtrType, "");
        netBuilder.BuildCall2(memsetType, memsetFunc, new[] { ucAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 0), LLVMValueRef.CreateConstInt(i64Type, 16) }, "");

        var ucFamPtr = netBuilder.BuildBitCast(ucAddrPtr, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i16Type, 2), ucFamPtr);

        var ucPortI16 = netBuilder.BuildTrunc(ucPort, i16Type, "");
        var ucNetPort = netBuilder.BuildCall2(htonsType, htonsFunc, new[] { ucPortI16 }, "");
        var ucPortRaw = netBuilder.BuildGEP2(context.Int8Type, ucAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 2) }, "");
        var ucPortPtr = netBuilder.BuildBitCast(ucPortRaw, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(ucNetPort, ucPortPtr);

        var ucInAddr = netBuilder.BuildCall2(inetAddrType, inetAddrFunc, new[] { ucIp }, "");
        var ucInRaw = netBuilder.BuildGEP2(context.Int8Type, ucAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "");
        var ucInPtr = netBuilder.BuildBitCast(ucInRaw, LLVMTypeRef.CreatePointer(i32Type, 0), "");
        netBuilder.BuildStore(ucInAddr, ucInPtr);

        var ucRes = netBuilder.BuildCall2(connectType, connectFunc, new[] { ucFd64, ucAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 16) }, "uc_res");
        netBuilder.BuildRet(ucRes);

        // 9. ecs_net_udp_send_to(fd: i32, ip: ptr, port: i32, data: ptr) -> i32
        var netUdpSendType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type, i8PtrType, i32Type, i8PtrType }, false);
        var netUdpSendFunc = module.AddFunction("ecs_net_udp_send_to", netUdpSendType);
        var uSendBB = netUdpSendFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(uSendBB);
        var usFd = netUdpSendFunc.GetParam(0);
        var usFd64 = netBuilder.BuildZExt(usFd, i64Type, "");
        var usIp = netUdpSendFunc.GetParam(1);
        var usPort = netUdpSendFunc.GetParam(2);
        var usData = netUdpSendFunc.GetParam(3);

        var usLen64 = netBuilder.BuildCall2(strlenType, strlenFunc, new[] { usData }, "");
        var usLen32 = netBuilder.BuildTrunc(usLen64, i32Type, "");

        var utAddrAlloca = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "ut_addr");
        var utAddrPtr = netBuilder.BuildBitCast(utAddrAlloca, i8PtrType, "");
        netBuilder.BuildCall2(memsetType, memsetFunc, new[] { utAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 0), LLVMValueRef.CreateConstInt(i64Type, 16) }, "");

        var utFamPtr = netBuilder.BuildBitCast(utAddrPtr, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i16Type, 2), utFamPtr);

        var utPortI16 = netBuilder.BuildTrunc(usPort, i16Type, "");
        var utNetPort = netBuilder.BuildCall2(htonsType, htonsFunc, new[] { utPortI16 }, "");
        var utPortRaw = netBuilder.BuildGEP2(context.Int8Type, utAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 2) }, "");
        var utPortPtr = netBuilder.BuildBitCast(utPortRaw, LLVMTypeRef.CreatePointer(i16Type, 0), "");
        netBuilder.BuildStore(utNetPort, utPortPtr);

        var utInAddr = netBuilder.BuildCall2(inetAddrType, inetAddrFunc, new[] { usIp }, "");
        var utInRaw = netBuilder.BuildGEP2(context.Int8Type, utAddrPtr, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "");
        var utInPtr = netBuilder.BuildBitCast(utInRaw, LLVMTypeRef.CreatePointer(i32Type, 0), "");
        netBuilder.BuildStore(utInAddr, utInPtr);

        var uSent = netBuilder.BuildCall2(sendtoType, sendtoFunc, new[] { usFd64, usData, usLen32, LLVMValueRef.CreateConstInt(i32Type, 0), utAddrPtr, LLVMValueRef.CreateConstInt(i32Type, 16) }, "u_sent");
        netBuilder.BuildRet(uSent);

        // 10. ecs_net_udp_recv_from(fd: i32, max_len: i32) -> ptr
        var netUdpRecvType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i32Type, i32Type }, false);
        var netUdpRecvFunc = module.AddFunction("ecs_net_udp_recv_from", netUdpRecvType);
        var urBB = netUdpRecvFunc.AppendBasicBlock("entry");
        var urHasDataBB = netUdpRecvFunc.AppendBasicBlock("has_data");
        var urEmptyBB = netUdpRecvFunc.AppendBasicBlock("empty");
        netBuilder.PositionAtEnd(urBB);
        var urFd = netUdpRecvFunc.GetParam(0);
        var urFd64 = netBuilder.BuildZExt(urFd, i64Type, "");
        var urMaxLen = netUdpRecvFunc.GetParam(1);

        var uBufSz = netBuilder.BuildSelect(
            netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, urMaxLen, LLVMValueRef.CreateConstInt(i32Type, 0), ""),
            urMaxLen,
            LLVMValueRef.CreateConstInt(i32Type, 4096),
            "");
        var uAllocSz = netBuilder.BuildAdd(uBufSz, LLVMValueRef.CreateConstInt(i32Type, 1), "");
        var uBuf = netBuilder.BuildCall2(mallocType, mallocFunc, new[] { netBuilder.BuildZExt(uAllocSz, i64Type, "") }, "u_buf");

        var uFromAddr = netBuilder.BuildAlloca(LLVMTypeRef.CreateArray(context.Int8Type, 16), "u_from_addr");
        var uFromPtr = netBuilder.BuildBitCast(uFromAddr, i8PtrType, "");
        var uFromLenAlloca = netBuilder.BuildAlloca(i32Type, "u_from_len");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(i32Type, 16), uFromLenAlloca);
        var uFromLenPtr = netBuilder.BuildBitCast(uFromLenAlloca, i8PtrType, "");

        var uRecv = netBuilder.BuildCall2(recvfromType, recvfromFunc, new[] { urFd64, uBuf, uBufSz, LLVMValueRef.CreateConstInt(i32Type, 0), uFromPtr, uFromLenPtr }, "u_recv");
        var uHasData = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, uRecv, LLVMValueRef.CreateConstInt(i32Type, 0), "");
        netBuilder.BuildCondBr(uHasData, urHasDataBB, urEmptyBB);

        netBuilder.PositionAtEnd(urHasDataBB);
        var uRecv64 = netBuilder.BuildSExt(uRecv, i64Type, "");
        var uTermPtr = netBuilder.BuildGEP2(context.Int8Type, uBuf, new[] { uRecv64 }, "");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), uTermPtr);
        netBuilder.BuildRet(uBuf);

        netBuilder.PositionAtEnd(urEmptyBB);
        netBuilder.BuildCall2(freeType, freeFunc, new[] { uBuf }, "");
        netBuilder.BuildRet(netBuilder.BuildGlobalStringPtr("", "net_udp_empty"));

        // 11. ecs_net_close(fd: i32) -> void
        var netCloseType = LLVMTypeRef.CreateFunction(voidType, new[] { i32Type }, false);
        var netCloseFunc = module.AddFunction("ecs_net_close", netCloseType);
        var closeBB = netCloseFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(closeBB);
        var closeFdParam = netCloseFunc.GetParam(0);
        var closeFd64 = netBuilder.BuildZExt(closeFdParam, i64Type, "");
        netBuilder.BuildCall2(
            LLVMTypeRef.CreateFunction(i32Type, new[] { i64Type }, false),
            closeFunc,
            new[] { closeFd64 },
            "");
        netBuilder.BuildRetVoid();

        // 12. ecs_net_get_last_error() -> i32
        var netErrType = LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false);
        var netErrFunc = module.AddFunction("ecs_net_get_last_error", netErrType);
        var netErrBB = netErrFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(netErrBB);
        if (isWindows)
        {
            var errVal = netBuilder.BuildCall2(
                LLVMTypeRef.CreateFunction(i32Type, Array.Empty<LLVMTypeRef>(), false),
                wsaGetLastErrorFunc,
                Array.Empty<LLVMValueRef>(),
                "err_val");
            netBuilder.BuildRet(errVal);
        }
        else
        {
            netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, 0, false));
        }

        // 13. ecs_net_poll_wait(timeout_ms: i32) -> void
        var netPollWaitType = LLVMTypeRef.CreateFunction(voidType, new[] { i32Type }, false);
        var netPollWaitFunc = module.AddFunction("ecs_net_poll_wait", netPollWaitType);
        var pollWaitBB = netPollWaitFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(pollWaitBB);
        var msParam = netPollWaitFunc.GetParam(0);
        if (isWindows)
        {
            var sleepType = LLVMTypeRef.CreateFunction(voidType, new[] { i32Type }, false);
            var sleepFunc = module.GetNamedFunction("Sleep");
            if (sleepFunc.Handle == IntPtr.Zero)
            {
                sleepFunc = module.AddFunction("Sleep", sleepType);
            }
            netBuilder.BuildCall2(sleepType, sleepFunc, new[] { msParam }, "");
        }
        else
        {
            var usleepType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type }, false);
            var usleepFunc = module.GetNamedFunction("usleep");
            if (usleepFunc.Handle == IntPtr.Zero)
            {
                usleepFunc = module.AddFunction("usleep", usleepType);
            }
            var usec = netBuilder.BuildMul(msParam, LLVMValueRef.CreateConstInt(i32Type, 1000, false), "usec");
            netBuilder.BuildCall2(usleepType, usleepFunc, new[] { usec }, "");
        }
        netBuilder.BuildRetVoid();

        // 14. ecs_net_tcp_send_framed(fd: i32, data: ptr) -> i32
        var netSendFramedType = LLVMTypeRef.CreateFunction(i32Type, new[] { i32Type, i8PtrType }, false);
        var netSendFramedFunc = module.AddFunction("ecs_net_tcp_send_framed", netSendFramedType);
        var sfBB = netSendFramedFunc.AppendBasicBlock("entry");
        netBuilder.PositionAtEnd(sfBB);
        var sfFd = netSendFramedFunc.GetParam(0);
        var sfFd64 = netBuilder.BuildZExt(sfFd, i64Type, "sf_fd64");
        var sfData = netSendFramedFunc.GetParam(1);

        var sfPayloadLen64 = netBuilder.BuildCall2(strlenType, strlenFunc, new[] { sfData }, "sf_plen64");
        var sfPayloadLen32 = netBuilder.BuildTrunc(sfPayloadLen64, i32Type, "sf_plen32");
        var sfNetLen = netBuilder.BuildCall2(htonlType, htonlFunc, new[] { sfPayloadLen32 }, "sf_netlen");

        var sfTotalLen32 = netBuilder.BuildAdd(sfPayloadLen32, LLVMValueRef.CreateConstInt(i32Type, 4), "sf_tot_len32");
        var sfTotalLen64 = netBuilder.BuildZExt(sfTotalLen32, i64Type, "sf_tot_len64");
        var sfBuf = netBuilder.BuildCall2(mallocType, mallocFunc, new[] { sfTotalLen64 }, "sf_buf");

        var sfLenSlot = netBuilder.BuildBitCast(sfBuf, LLVMTypeRef.CreatePointer(i32Type, 0), "sf_len_slot");
        netBuilder.BuildStore(sfNetLen, sfLenSlot);

        var sfPayloadDst = netBuilder.BuildGEP2(context.Int8Type, sfBuf, new[] { LLVMValueRef.CreateConstInt(i32Type, 4) }, "sf_payload_dst");
        netBuilder.BuildCall2(memcpyType, memcpyFunc, new[] { sfPayloadDst, sfData, sfPayloadLen64 }, "");

        var sfSent = netBuilder.BuildCall2(sendType, sendFunc, new[] { sfFd64, sfBuf, sfTotalLen32, LLVMValueRef.CreateConstInt(i32Type, 0) }, "sf_sent");
        netBuilder.BuildCall2(freeType, freeFunc, new[] { sfBuf }, "");
        netBuilder.BuildRet(sfSent);

        // 15. ecs_net_tcp_recv_append(fd: i32, buf: Vec<u8>, max_len: i32) -> Vec<u8>
        var netRecvAppendType = LLVMTypeRef.CreateFunction(vecU8Type, new[] { i32Type, vecU8Type, i32Type }, false);
        var netRecvAppendFunc = module.AddFunction("ecs_net_tcp_recv_append", netRecvAppendType);
        var raEntryBB = netRecvAppendFunc.AppendBasicBlock("entry");
        var raHasDataBB = netRecvAppendFunc.AppendBasicBlock("has_data");
        var raNoDataBB = netRecvAppendFunc.AppendBasicBlock("no_data");
        netBuilder.PositionAtEnd(raEntryBB);

        var raFd = netRecvAppendFunc.GetParam(0);
        var raFd64 = netBuilder.BuildZExt(raFd, i64Type, "ra_fd64");
        var raBuf = netRecvAppendFunc.GetParam(1);
        var raMaxLen = netRecvAppendFunc.GetParam(2);

        var raMaxLenClean = netBuilder.BuildSelect(
            netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, raMaxLen, LLVMValueRef.CreateConstInt(i32Type, 0), ""),
            raMaxLen,
            LLVMValueRef.CreateConstInt(i32Type, 4096),
            "ra_maxlen_clean");

        var raTempBuf = netBuilder.BuildCall2(mallocType, mallocFunc, new[] { netBuilder.BuildZExt(raMaxLenClean, i64Type, "") }, "ra_tmp");
        var raRead = netBuilder.BuildCall2(recvType, recvFunc, new[] { raFd64, raTempBuf, raMaxLenClean, LLVMValueRef.CreateConstInt(i32Type, 0) }, "ra_read");

        var raHasData = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, raRead, LLVMValueRef.CreateConstInt(i32Type, 0), "ra_has_data");
        netBuilder.BuildCondBr(raHasData, raHasDataBB, raNoDataBB);

        netBuilder.PositionAtEnd(raNoDataBB);
        netBuilder.BuildCall2(freeType, freeFunc, new[] { raTempBuf }, "");
        netBuilder.BuildRet(raBuf);

        netBuilder.PositionAtEnd(raHasDataBB);
        var curData = netBuilder.BuildExtractValue(raBuf, 0, "cur_data");
        var curLen = netBuilder.BuildExtractValue(raBuf, 1, "cur_len");
        var curCap = netBuilder.BuildExtractValue(raBuf, 2, "cur_cap");

        var neededLen = netBuilder.BuildAdd(curLen, raRead, "needed_len");
        var needGrow = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, neededLen, curCap, "need_grow");

        var raGrowBB = netRecvAppendFunc.AppendBasicBlock("ra_grow");
        var raAppendBB = netRecvAppendFunc.AppendBasicBlock("ra_append");
        netBuilder.BuildCondBr(needGrow, raGrowBB, raAppendBB);

        netBuilder.PositionAtEnd(raGrowBB);
        var doubleCap = netBuilder.BuildMul(curCap, LLVMValueRef.CreateConstInt(i32Type, 2), "ra_double_cap");
        var largerCap = netBuilder.BuildSelect(
            netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, neededLen, doubleCap, ""),
            neededLen,
            doubleCap,
            "ra_larger_cap");
        var newCapCalculated = netBuilder.BuildSelect(
            netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, largerCap, LLVMValueRef.CreateConstInt(i32Type, 16), ""),
            LLVMValueRef.CreateConstInt(i32Type, 16),
            largerCap,
            "ra_new_cap");
        var reallocPtr = netBuilder.BuildCall2(reallocType, reallocFunc, new[] { curData, netBuilder.BuildZExt(newCapCalculated, i64Type, "") }, "ra_realloc");
        netBuilder.BuildBr(raAppendBB);

        netBuilder.PositionAtEnd(raAppendBB);
        var finalData = netBuilder.BuildPhi(i8PtrType, "final_data");
        finalData.AddIncoming(new[] { curData, reallocPtr }, new[] { raHasDataBB, raGrowBB }, 2);

        var finalCap = netBuilder.BuildPhi(i32Type, "final_cap");
        finalCap.AddIncoming(new[] { curCap, newCapCalculated }, new[] { raHasDataBB, raGrowBB }, 2);

        var appendDst = netBuilder.BuildGEP2(context.Int8Type, finalData, new[] { curLen }, "ra_append_dst");
        netBuilder.BuildCall2(memmoveType, memmoveFunc, new[] { appendDst, raTempBuf, netBuilder.BuildZExt(raRead, i64Type, "") }, "");
        netBuilder.BuildCall2(freeType, freeFunc, new[] { raTempBuf }, "");

        var raRes0 = netBuilder.BuildInsertValue(LLVMValueRef.CreateConstNull(vecU8Type), finalData, 0, "ra_res0");
        var raRes1 = netBuilder.BuildInsertValue(raRes0, neededLen, 1, "ra_res1");
        var raRes2 = netBuilder.BuildInsertValue(raRes1, finalCap, 2, "ra_res2");
        netBuilder.BuildRet(raRes2);

        // 16. ecs_net_buffer_read_i32(buf: Vec<u8>, offset: i32) -> i32
        var netReadI32Type = LLVMTypeRef.CreateFunction(i32Type, new[] { vecU8Type, i32Type }, false);
        var netReadI32Func = module.AddFunction("ecs_net_buffer_read_i32", netReadI32Type);
        var rbEntryBB = netReadI32Func.AppendBasicBlock("entry");
        var rbValidBB = netReadI32Func.AppendBasicBlock("valid");
        var rbInvalidBB = netReadI32Func.AppendBasicBlock("invalid");
        netBuilder.PositionAtEnd(rbEntryBB);

        var rbBuf = netReadI32Func.GetParam(0);
        var rbOffset = netReadI32Func.GetParam(1);

        var rbData = netBuilder.BuildExtractValue(rbBuf, 0, "rb_data");
        var rbLen = netBuilder.BuildExtractValue(rbBuf, 1, "rb_len");

        var offGeq0 = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, rbOffset, LLVMValueRef.CreateConstInt(i32Type, 0), "off_geq_0");
        var offPlus4 = netBuilder.BuildAdd(rbOffset, LLVMValueRef.CreateConstInt(i32Type, 4), "off_plus_4");
        var offLeqLen = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, offPlus4, rbLen, "off_leq_len");
        var offValid = netBuilder.BuildAnd(offGeq0, offLeqLen, "off_valid");
        netBuilder.BuildCondBr(offValid, rbValidBB, rbInvalidBB);

        netBuilder.PositionAtEnd(rbInvalidBB);
        netBuilder.BuildRet(LLVMValueRef.CreateConstInt(i32Type, unchecked((ulong)-1), true));

        netBuilder.PositionAtEnd(rbValidBB);
        var rbPtrRaw = netBuilder.BuildGEP2(context.Int8Type, rbData, new[] { rbOffset }, "rb_ptr_raw");
        var rbAlloca = netBuilder.BuildAlloca(i32Type, "rb_alloca");
        var rbAllocaPtr = netBuilder.BuildBitCast(rbAlloca, i8PtrType, "rb_alloca_ptr");
        netBuilder.BuildCall2(memcpyType, memcpyFunc, new[] { rbAllocaPtr, rbPtrRaw, LLVMValueRef.CreateConstInt(i64Type, 4) }, "");
        var rawVal = netBuilder.BuildLoad2(i32Type, rbAlloca, "rb_raw_val");
        var hostVal = netBuilder.BuildCall2(ntohlType, ntohlFunc, new[] { rawVal }, "rb_host_val");
        netBuilder.BuildRet(hostVal);

        // 17. ecs_net_buffer_extract_str(buf: Vec<u8>, offset: i32, len: i32) -> ptr
        var netExtractStrType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { vecU8Type, i32Type, i32Type }, false);
        var netExtractStrFunc = module.AddFunction("ecs_net_buffer_extract_str", netExtractStrType);
        var esEntryBB = netExtractStrFunc.AppendBasicBlock("entry");
        var esValidBB = netExtractStrFunc.AppendBasicBlock("valid");
        var esInvalidBB = netExtractStrFunc.AppendBasicBlock("invalid");
        netBuilder.PositionAtEnd(esEntryBB);

        var esBuf = netExtractStrFunc.GetParam(0);
        var esOffset = netExtractStrFunc.GetParam(1);
        var esLen = netExtractStrFunc.GetParam(2);

        var esData = netBuilder.BuildExtractValue(esBuf, 0, "es_data");
        var esBufLen = netBuilder.BuildExtractValue(esBuf, 1, "es_buflen");

        var esOffGeq0 = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, esOffset, LLVMValueRef.CreateConstInt(i32Type, 0), "es_off_geq_0");
        var esLenGeq0 = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, esLen, LLVMValueRef.CreateConstInt(i32Type, 0), "es_len_geq_0");
        var esEnd = netBuilder.BuildAdd(esOffset, esLen, "es_end");
        var esEndLeq = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, esEnd, esBufLen, "es_end_leq");
        var esCond = netBuilder.BuildAnd(netBuilder.BuildAnd(esOffGeq0, esLenGeq0, ""), esEndLeq, "es_cond");
        netBuilder.BuildCondBr(esCond, esValidBB, esInvalidBB);

        netBuilder.PositionAtEnd(esInvalidBB);
        netBuilder.BuildRet(netBuilder.BuildGlobalStringPtr("", "net_extract_empty"));

        netBuilder.PositionAtEnd(esValidBB);
        var esAllocSz = netBuilder.BuildAdd(esLen, LLVMValueRef.CreateConstInt(i32Type, 1), "es_alloc_sz");
        var esAllocSz64 = netBuilder.BuildZExt(esAllocSz, i64Type, "es_alloc_sz64");
        var esStr = netBuilder.BuildCall2(mallocType, mallocFunc, new[] { esAllocSz64 }, "es_str");

        var esSrc = netBuilder.BuildGEP2(context.Int8Type, esData, new[] { esOffset }, "es_src");
        var esLen64 = netBuilder.BuildZExt(esLen, i64Type, "es_len64");
        netBuilder.BuildCall2(memcpyType, memcpyFunc, new[] { esStr, esSrc, esLen64 }, "");

        var esTermPtr = netBuilder.BuildGEP2(context.Int8Type, esStr, new[] { esLen64 }, "es_term");
        netBuilder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 0), esTermPtr);
        netBuilder.BuildRet(esStr);

        // 18. ecs_net_buffer_drain(buf: Vec<u8>, count: i32) -> Vec<u8>
        var netDrainType = LLVMTypeRef.CreateFunction(vecU8Type, new[] { vecU8Type, i32Type }, false);
        var netDrainFunc = module.AddFunction("ecs_net_buffer_drain", netDrainType);
        var drEntryBB = netDrainFunc.AppendBasicBlock("entry");
        var drShiftBB = netDrainFunc.AppendBasicBlock("shift");
        var drClearBB = netDrainFunc.AppendBasicBlock("clear");
        var drNopBB = netDrainFunc.AppendBasicBlock("nop");
        netBuilder.PositionAtEnd(drEntryBB);

        var drBuf = netDrainFunc.GetParam(0);
        var drCount = netDrainFunc.GetParam(1);

        var drData = netBuilder.BuildExtractValue(drBuf, 0, "dr_data");
        var drLen = netBuilder.BuildExtractValue(drBuf, 1, "dr_len");
        var drCap = netBuilder.BuildExtractValue(drBuf, 2, "dr_cap");

        var countLeq0 = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, drCount, LLVMValueRef.CreateConstInt(i32Type, 0), "count_leq_0");
        var drCheckClearBB = netDrainFunc.AppendBasicBlock("check_clear");
        netBuilder.BuildCondBr(countLeq0, drNopBB, drCheckClearBB);

        netBuilder.PositionAtEnd(drNopBB);
        netBuilder.BuildRet(drBuf);

        netBuilder.PositionAtEnd(drCheckClearBB);
        var countGeqLen = netBuilder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, drCount, drLen, "count_geq_len");
        netBuilder.BuildCondBr(countGeqLen, drClearBB, drShiftBB);

        netBuilder.PositionAtEnd(drClearBB);
        var drClearRes = netBuilder.BuildInsertValue(drBuf, LLVMValueRef.CreateConstInt(i32Type, 0), 1, "dr_clear_res");
        netBuilder.BuildRet(drClearRes);

        netBuilder.PositionAtEnd(drShiftBB);
        var drRemaining = netBuilder.BuildSub(drLen, drCount, "dr_rem");
        var drSrc = netBuilder.BuildGEP2(context.Int8Type, drData, new[] { drCount }, "dr_src");
        netBuilder.BuildCall2(memmoveType, memmoveFunc, new[] { drData, drSrc, netBuilder.BuildZExt(drRemaining, i64Type, "") }, "");
        var drShiftRes = netBuilder.BuildInsertValue(drBuf, drRemaining, 1, "dr_shift_res");
        netBuilder.BuildRet(drShiftRes);
    }
}

