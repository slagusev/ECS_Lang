using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe void CompileBlock(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        BlockStatement block,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        bool isMain = false,
        bool hasWaitKey = false)
    {
        foreach (var stmt in block.Statements)
        {
            if (builder.InsertBlock.Terminator.Handle != IntPtr.Zero)
                break;

            SetDebugLocation(context, builder, stmt.Span);

            switch (stmt)
            {
                case VariableDeclarationStatement varDecl:
                    var tName = varDecl.TypeName ?? _typeChecker.GetNodeType(varDecl.Initializer).Name;
                    var varType = MapType(context, tName, ecs);
                    if (!locals.TryGetValue(varDecl.Name, out var alloca))
                    {
                        alloca = CreateEntryBlockAlloca(context, function, varType, varDecl.Name);
                        locals[varDecl.Name] = alloca;
                    }
                    var initVal = CompileExpression(context, module, builder, function, varDecl.Initializer, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    initVal = CoerceValue(builder, context, initVal, varType);
                    builder.BuildStore(initVal, alloca);
                    varTypes[varDecl.Name] = tName;
                    break;

                case AssignmentStatement assign:
                    if (locals.TryGetValue(assign.TargetName, out var targetPtr))
                    {
                        var newVal = CompileExpression(context, module, builder, function, assign.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        LLVMValueRef destPtr;
                        string? targetTypeName = null;
                        if (assign.MemberName != null && varTypes.TryGetValue(assign.TargetName, out targetTypeName))
                        {
                            var structType = ecs.GetComponentStructType(targetTypeName);
                            int fieldOffset = ecs.GetFieldOffset(targetTypeName, assign.MemberName);
                            var fieldGEP = builder.BuildStructGEP2(structType, targetPtr, (uint)fieldOffset, $"{assign.TargetName}_{assign.MemberName}_gep");

                            if (assign.Index != null)
                            {
                                var memType = _typeChecker.GetMemberType(targetTypeName, assign.MemberName, assign.Span);
                                var fieldArrType = MapType(context, memType.Name, ecs);
                                var idxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                                var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                                destPtr = builder.BuildInBoundsGEP2(fieldArrType, fieldGEP, new[] { zero, idxVal }, $"{assign.TargetName}_{assign.MemberName}_elem_gep");
                            }
                            else
                            {
                                destPtr = fieldGEP;
                            }
                        }
                        else
                        {
                            if (assign.Index != null && varTypes.TryGetValue(assign.TargetName, out var arrTypeName))
                            {
                                var arrTypeSym = TypeSymbol.FromName(arrTypeName);
                                var idxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));

                                if (arrTypeSym.IsMap)
                                {
                                    arrTypeSym.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                                    var keyVal = CompileExpression(context, module, builder, function, assign.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                                    var insertFunc = _mapEmitter!.GetOrCreateInsert(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                                    var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                                    builder.BuildCall2(ifType, insertFunc, new[] { targetPtr, keyVal, newVal }, "");
                                    break;
                                }
                                else if (arrTypeSym.IsDynamicArray)
                                {
                                    arrTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                                    var dynArrStructType = MapType(context, arrTypeSym.Name, ecs);
                                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                                    var dataSlot = builder.BuildStructGEP2(dynArrStructType, targetPtr, 0, $"{assign.TargetName}_data_slot");
                                    var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, $"{assign.TargetName}_data_ptr");
                                    destPtr = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { idxVal }, $"{assign.TargetName}_elem_gep");
                                }
                                else
                                {
                                    var arrType = MapType(context, arrTypeName, ecs);
                                    var zero = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                                    destPtr = builder.BuildInBoundsGEP2(arrType, targetPtr, new[] { zero, idxVal }, $"{assign.TargetName}_elem_gep");
                                }
                            }
                            else
                            {
                                destPtr = targetPtr;
                            }
                        }

                        if (assign.Op == AssignmentOperator.Assign)
                        {
                            if (assign.MemberName != null && targetTypeName != null)
                            {
                                var memType = _typeChecker.GetMemberType(targetTypeName, assign.MemberName, assign.Span);
                                var fieldType = MapType(context, memType.Name, ecs);
                                newVal = CoerceValue(builder, context, newVal, fieldType);
                            }
                            else if (varTypes.TryGetValue(assign.TargetName, out var targetVarType))
                            {
                                var destType = MapType(context, targetVarType, ecs);
                                newVal = CoerceValue(builder, context, newVal, destType);
                            }
                            builder.BuildStore(newVal, destPtr);
                        }
                        else
                        {
                            LLVMTypeRef destType = newVal.TypeOf;
                            if (assign.MemberName != null && targetTypeName != null)
                            {
                                var memType = _typeChecker.GetMemberType(targetTypeName, assign.MemberName, assign.Span);
                                destType = MapType(context, memType.Name, ecs);
                            }
                            else if (varTypes.TryGetValue(assign.TargetName, out var targetVarType))
                            {
                                destType = MapType(context, targetVarType, ecs);
                            }

                            newVal = CoerceValue(builder, context, newVal, destType);
                            var currentVal = builder.BuildLoad2(destType, destPtr, "cur_val");
                            var isFp = destType == context.FloatType || destType == context.DoubleType;
                            var resVal = assign.Op switch
                            {
                                AssignmentOperator.PlusAssign => isFp
                                    ? builder.BuildFAdd(currentVal, newVal, "fadd")
                                    : builder.BuildAdd(currentVal, newVal, "add"),
                                AssignmentOperator.MinusAssign => isFp
                                    ? builder.BuildFSub(currentVal, newVal, "fsub")
                                    : builder.BuildSub(currentVal, newVal, "sub"),
                                AssignmentOperator.MulAssign => isFp
                                    ? builder.BuildFMul(currentVal, newVal, "fmul")
                                    : builder.BuildMul(currentVal, newVal, "mul"),
                                AssignmentOperator.DivAssign => isFp
                                    ? builder.BuildFDiv(currentVal, newVal, "fdiv")
                                    : builder.BuildSDiv(currentVal, newVal, "div"),
                                _ => newVal
                            };
                            builder.BuildStore(resVal, destPtr);
                        }
                    }
                    break;

                case IfStatement ifStmt:
                    var condVal = CompileExpression(context, module, builder, function, ifStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var thenBlock = function.AppendBasicBlock("then");
                    var elseBlock = ifStmt.ElseBranch != null ? function.AppendBasicBlock("else") : default;
                    var mergeBlock = function.AppendBasicBlock("if_merge");

                    var falseDest = ifStmt.ElseBranch != null ? elseBlock : mergeBlock;
                    builder.BuildCondBr(condVal, thenBlock, falseDest);

                    builder.PositionAtEnd(thenBlock);
                    CompileBlock(context, module, builder, function, ifStmt.ThenBranch, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(mergeBlock);

                    if (ifStmt.ElseBranch != null)
                    {
                        builder.PositionAtEnd(elseBlock);
                        if (ifStmt.ElseBranch is BlockStatement elseStmtBlock)
                            CompileBlock(context, module, builder, function, elseStmtBlock, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);

                        if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                            builder.BuildBr(mergeBlock);
                    }

                    builder.PositionAtEnd(mergeBlock);
                    break;

                case WhileStatement whileStmt:
                    var whileCondBB = function.AppendBasicBlock("while_cond");
                    var whileBodyBB = function.AppendBasicBlock("while_body");
                    var whileExitBB = function.AppendBasicBlock("while_exit");

                    builder.BuildBr(whileCondBB);

                    builder.PositionAtEnd(whileCondBB);
                    var loopCondVal = CompileExpression(context, module, builder, function, whileStmt.Condition, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    builder.BuildCondBr(loopCondVal, whileBodyBB, whileExitBB);

                    builder.PositionAtEnd(whileBodyBB);
                    CompileBlock(context, module, builder, function, whileStmt.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(whileCondBB);

                    builder.PositionAtEnd(whileExitBB);
                    break;

                case ForStatement forStmt:
                    var startVal = CompileExpression(context, module, builder, function, forStmt.Start, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var endVal = CompileExpression(context, module, builder, function, forStmt.End, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                    var loopVarAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, forStmt.VariableName);
                    builder.BuildStore(startVal, loopVarAlloca);

                    var oldLocal = locals.TryGetValue(forStmt.VariableName, out var prevLocal) ? prevLocal : default;
                    var oldType = varTypes.TryGetValue(forStmt.VariableName, out var prevType) ? prevType : null;

                    locals[forStmt.VariableName] = loopVarAlloca;
                    varTypes[forStmt.VariableName] = "i32";

                    var forCondBB = function.AppendBasicBlock("for_cond");
                    var forBodyBB = function.AppendBasicBlock("for_body");
                    var forIncBB = function.AppendBasicBlock("for_inc");
                    var forExitBB = function.AppendBasicBlock("for_exit");

                    builder.BuildBr(forCondBB);

                    // for_cond: while (i < end)
                    builder.PositionAtEnd(forCondBB);
                    var curVal = builder.BuildLoad2(context.Int32Type, loopVarAlloca, forStmt.VariableName);
                    var cmpVal = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curVal, endVal, "for_cmp");
                    builder.BuildCondBr(cmpVal, forBodyBB, forExitBB);

                    // for_body
                    builder.PositionAtEnd(forBodyBB);
                    CompileBlock(context, module, builder, function, forStmt.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                        builder.BuildBr(forIncBB);

                    // for_inc: i++
                    builder.PositionAtEnd(forIncBB);
                    var curValInc = builder.BuildLoad2(context.Int32Type, loopVarAlloca, "for_cur");
                    var nextVal = builder.BuildAdd(curValInc, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "for_next");
                    builder.BuildStore(nextVal, loopVarAlloca);
                    builder.BuildBr(forCondBB);

                    // for_exit
                    builder.PositionAtEnd(forExitBB);

                    if (oldType != null)
                    {
                        locals[forStmt.VariableName] = oldLocal;
                        varTypes[forStmt.VariableName] = oldType;
                    }
                    else
                    {
                        locals.Remove(forStmt.VariableName);
                        varTypes.Remove(forStmt.VariableName);
                    }
                    break;

                case ReturnStatement retStmt:
                    if (isMain && !hasWaitKey && !_options.NoWaitOnExit && !_options.IsRelease)
                    {
                        var msg = builder.BuildGlobalStringPtr("Press Enter to exit...", "prompt_exit");
                        builder.BuildCall2(putsType, putsFunc, new[] { msg }, "puts_exit");
                        var getcharFunc = module.GetNamedFunction("getchar");
                        var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                        builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "auto_wait_key");
                    }
                    if (retStmt.Value != null)
                    {
                        var retVal = CompileExpression(context, module, builder, function, retStmt.Value, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        builder.BuildRet(retVal);
                    }
                    else
                    {
                        builder.BuildRetVoid();
                    }
                    break;

                case MatchStatement matchStmt:
                    CompileMatchStatement(context, module, builder, function, matchStmt, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                    break;

                case ExpressionStatement exprStmt:
                    CompileExpression(context, module, builder, function, exprStmt.Expression, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    break;
            }
        }
    }

    private void CompileMatchStatement(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        MatchStatement match,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc,
        bool isMain,
        bool hasWaitKey)
    {
        var sType = _typeChecker.GetNodeType(match.Scrutinee);
        if (sType.IsOption)
        {
            sType.TryGetOptionInfo(out var elemType);
            var elemLlvmType = MapType(context, elemType.Name, ecs);
            var optLlvmType = MapType(context, sType.Name, ecs);

            var scrutVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var scrutAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "match_opt_scrut");
            builder.BuildStore(scrutVal, scrutAlloca);

            var tagGEP = builder.BuildStructGEP2(optLlvmType, scrutAlloca, 0, "opt_tag_gep");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagGEP, "opt_tag");

            var optMergeBB = function.AppendBasicBlock("opt_match_merge");
            var optDefaultBB = function.AppendBasicBlock("opt_match_default");

            MatchArm? someArm = null;
            string? someBindVar = null;
            MatchArm? noneArm = null;
            MatchArm? optWildcardArm = null;

            foreach (var arm in match.Arms)
            {
                if (arm.Pattern is WildcardExpression)
                {
                    optWildcardArm = arm;
                }
                else if (arm.Pattern is IdentifierExpression { Name: "None" } || arm.Pattern is CallExpression { Callee: "None" } ||
                         (arm.Pattern is IdentifierExpression idN && idN.Name.EndsWith("::None")))
                {
                    noneArm = arm;
                }
                else if (arm.Pattern is CallExpression callSome && (callSome.Callee == "Some" || callSome.Callee.EndsWith("::Some")) && callSome.Arguments.Count == 1 && callSome.Arguments[0] is IdentifierExpression bindId)
                {
                    someArm = arm;
                    someBindVar = bindId.Name;
                }
            }

            var optSwitchInst = builder.BuildSwitch(tagVal, optDefaultBB, 2);

            if (someArm != null)
            {
                var someBB = function.AppendBasicBlock("match_arm_some");
                optSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 1), someBB);

                builder.PositionAtEnd(someBB);
                if (someBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(optLlvmType, scrutAlloca, 1, $"{someBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(elemLlvmType, valGEP, someBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, elemLlvmType, someBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[someBindVar] = bindAlloca;
                    varTypes[someBindVar] = elemType.Name;
                }

                CompileBlock(context, module, builder, function, someArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (someBindVar != null)
                {
                    locals.Remove(someBindVar);
                    varTypes.Remove(someBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(optMergeBB);
                }
            }

            if (noneArm != null)
            {
                var noneBB = function.AppendBasicBlock("match_arm_none");
                optSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 0), noneBB);

                builder.PositionAtEnd(noneBB);
                CompileBlock(context, module, builder, function, noneArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(optMergeBB);
                }
            }

            builder.PositionAtEnd(optDefaultBB);
            if (optWildcardArm != null)
            {
                CompileBlock(context, module, builder, function, optWildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            }
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(optMergeBB);
            }

            builder.PositionAtEnd(optMergeBB);
            return;
        }

        if (sType.IsResult)
        {
            sType.TryGetResultInfo(out var okType, out var errType);
            var okLlvmType = MapType(context, okType.Name, ecs);
            var errLlvmType = MapType(context, errType.Name, ecs);
            var resLlvmType = MapType(context, sType.Name, ecs);

            var scrutVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
            var scrutAlloca = CreateEntryBlockAlloca(context, function, resLlvmType, "match_res_scrut");
            builder.BuildStore(scrutVal, scrutAlloca);

            var tagGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 0, "res_tag_gep");
            var tagVal = builder.BuildLoad2(context.Int32Type, tagGEP, "res_tag");

            var resMergeBB = function.AppendBasicBlock("res_match_merge");
            var resDefaultBB = function.AppendBasicBlock("res_match_default");

            MatchArm? okArm = null;
            string? okBindVar = null;
            MatchArm? errArm = null;
            string? errBindVar = null;
            MatchArm? resWildcardArm = null;

            foreach (var arm in match.Arms)
            {
                if (arm.Pattern is WildcardExpression)
                {
                    resWildcardArm = arm;
                }
                else if (arm.Pattern is CallExpression callOk && (callOk.Callee == "Ok" || callOk.Callee.EndsWith("::Ok")) && callOk.Arguments.Count == 1 && callOk.Arguments[0] is IdentifierExpression bindOk)
                {
                    okArm = arm;
                    okBindVar = bindOk.Name;
                }
                else if (arm.Pattern is CallExpression callErr && (callErr.Callee == "Err" || callErr.Callee.EndsWith("::Err")) && callErr.Arguments.Count == 1 && callErr.Arguments[0] is IdentifierExpression bindErr)
                {
                    errArm = arm;
                    errBindVar = bindErr.Name;
                }
            }

            var resSwitchInst = builder.BuildSwitch(tagVal, resDefaultBB, 2);

            if (okArm != null)
            {
                var okBB = function.AppendBasicBlock("match_arm_ok");
                resSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 0), okBB); // Tag 0 = Ok

                builder.PositionAtEnd(okBB);
                if (okBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 1, $"{okBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(okLlvmType, valGEP, okBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, okLlvmType, okBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[okBindVar] = bindAlloca;
                    varTypes[okBindVar] = okType.Name;
                }

                CompileBlock(context, module, builder, function, okArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (okBindVar != null)
                {
                    locals.Remove(okBindVar);
                    varTypes.Remove(okBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(resMergeBB);
                }
            }

            if (errArm != null)
            {
                var errBB = function.AppendBasicBlock("match_arm_err");
                resSwitchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, 1), errBB); // Tag 1 = Err

                builder.PositionAtEnd(errBB);
                if (errBindVar != null)
                {
                    var valGEP = builder.BuildStructGEP2(resLlvmType, scrutAlloca, 2, $"{errBindVar}_gep");
                    var valLoaded = builder.BuildLoad2(errLlvmType, valGEP, errBindVar);
                    var bindAlloca = CreateEntryBlockAlloca(context, function, errLlvmType, errBindVar);
                    builder.BuildStore(valLoaded, bindAlloca);
                    locals[errBindVar] = bindAlloca;
                    varTypes[errBindVar] = errType.Name;
                }

                CompileBlock(context, module, builder, function, errArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
                if (errBindVar != null)
                {
                    locals.Remove(errBindVar);
                    varTypes.Remove(errBindVar);
                }

                if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    builder.BuildBr(resMergeBB);
                }
            }

            builder.PositionAtEnd(resDefaultBB);
            if (resWildcardArm != null)
            {
                CompileBlock(context, module, builder, function, resWildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            }
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(resMergeBB);
            }

            builder.PositionAtEnd(resMergeBB);
            return;
        }

        var scrutineeVal = CompileExpression(context, module, builder, function, match.Scrutinee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
        scrutineeVal = EnsureInt32(context, builder, scrutineeVal, "match_scrut");

        var mergeBB = function.AppendBasicBlock("match_merge");
        var defaultBB = function.AppendBasicBlock("match_default");

        var nonWildcardArms = new List<(int Value, MatchArm Arm)>();
        MatchArm? wildcardArm = null;

        foreach (var arm in match.Arms)
        {
            if (arm.Pattern is WildcardExpression)
            {
                wildcardArm = arm;
            }
            else if (arm.Pattern is NumberLiteralExpression num)
            {
                int val = int.Parse(num.RawValue);
                nonWildcardArms.Add((val, arm));
            }
            else if (arm.Pattern is MemberAccessExpression mem && mem.Target is IdentifierExpression id && _typeChecker.Enums.TryGetValue(id.Name, out var eSym))
            {
                if (eSym.Members.TryGetValue(mem.MemberName, out var mSym))
                {
                    nonWildcardArms.Add((mSym.Value, arm));
                }
            }
        }

        var switchInst = builder.BuildSwitch(scrutineeVal, defaultBB, (uint)nonWildcardArms.Count);

        foreach (var (val, arm) in nonWildcardArms)
        {
            var armBB = function.AppendBasicBlock($"match_arm_{val}");
            switchInst.AddCase(LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)val, false), armBB);

            builder.PositionAtEnd(armBB);
            CompileBlock(context, module, builder, function, arm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
            if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                builder.BuildBr(mergeBB);
            }
        }

        // Default / wildcard block
        builder.PositionAtEnd(defaultBB);
        if (wildcardArm != null)
        {
            CompileBlock(context, module, builder, function, wildcardArm.Body, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain, hasWaitKey);
        }
        if (builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
        {
            builder.BuildBr(mergeBB);
        }

        builder.PositionAtEnd(mergeBB);
    }


}
