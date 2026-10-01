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
    }
}
