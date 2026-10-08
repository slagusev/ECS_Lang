using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class LlvmCodeGenerator
{
    private unsafe LLVMValueRef CompileExpression(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        LLVMValueRef function,
        ExpressionNode expr,
        Dictionary<string, LLVMValueRef> locals,
        Dictionary<string, string> varTypes,
        EcsRuntimeEmitter ecs,
        LLVMTypeRef putsType,
        LLVMValueRef putsFunc,
        LLVMTypeRef printfType,
        LLVMValueRef printfFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        switch (expr)
        {
            case NumberLiteralExpression num:
                if (num.IsFloatingPoint)
                {
                    double dVal = double.Parse(num.RawValue, System.Globalization.CultureInfo.InvariantCulture);
                    return LLVMValueRef.CreateConstReal(context.FloatType, dVal);
                }
                else
                {
                    long iVal = num.RawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt64(num.RawValue, 16)
                        : long.Parse(num.RawValue);
                    return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)iVal, false);
                }

            case BooleanLiteralExpression b:
                return LLVMValueRef.CreateConstInt(context.Int1Type, b.Value ? 1UL : 0UL, false);

            case StringLiteralExpression str:
                return builder.BuildGlobalStringPtr(str.Value, "str_lit");

            case CastExpressionNode cast:
                return GenerateCastExpression(context, module, builder, function, cast, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case ResourceGetExpressionNode resGet:
                return GenerateResourceGetExpression(context, module, builder, function, resGet, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case ErrorPropagationExpressionNode tryExpr:
                return GenerateErrorPropagationExpression(context, module, builder, function, tryExpr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case BulkSpawnExpressionNode bulkSpawn:
                return GenerateBulkSpawnExpression(context, module, builder, function, bulkSpawn, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case IdentifierExpression ident:
                if (locals.TryGetValue(ident.Name, out var varPtr))
                {
                    string typeName = varTypes.TryGetValue(ident.Name, out var vt) ? vt : _typeChecker.GetNodeType(ident).Name;
                    var llvmType = MapType(context, typeName, ecs);
                    return builder.BuildLoad2(llvmType, varPtr, ident.Name);
                }
                if (ident.Name == "None" || (ident.Name.StartsWith("Option<") && ident.Name.EndsWith("::None")))
                {
                    return CompileNoneIdentifier(context, function, ident, ecs, builder);
                }
                if (_typeChecker.Constants.TryGetValue(ident.Name, out var constSym))
                {
                    return EmitConstantLiteral(context, builder, constSym);
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case MemberAccessExpression mem:
                var memTargetType = _typeChecker.GetNodeType(mem.Target);
                if (memTargetType == TypeSymbol.String && mem.MemberName is "len" or "length")
                {
                    var strVal = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { strVal }, "slen64");
                    return builder.BuildTrunc(len64, context.Int32Type, "slen32");
                }
                if (memTargetType == TypeSymbol.StrView && mem.MemberName is "len" or "length")
                {
                    var viewVal = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildExtractValue(viewVal, 1, "view_len");
                }
                if (memTargetType.IsDynamicArray && mem.MemberName is "len" or "length" or "capacity")
                {
                    var dynArrStructType = MapType(context, memTargetType.Name, ecs);
                    LLVMValueRef dynStructPtr;
                    if (mem.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    uint fieldIdx = mem.MemberName == "capacity" ? 2u : 1u;
                    var fieldSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, fieldIdx, $"dyn_{mem.MemberName}_slot");
                    return builder.BuildLoad2(context.Int32Type, fieldSlot, $"dyn_{mem.MemberName}_val");
                }
                if (memTargetType.IsMap && mem.MemberName is "len" or "length" or "count" or "capacity")
                {
                    var mapStructType = MapType(context, memTargetType.Name, ecs);
                    LLVMValueRef mapStructPtr;
                    if (mem.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    uint fieldIdx = mem.MemberName == "capacity" ? 2u : 1u;
                    var fieldSlot = builder.BuildStructGEP2(mapStructType, mapStructPtr, fieldIdx, $"map_{mem.MemberName}_slot");
                    return builder.BuildLoad2(context.Int32Type, fieldSlot, $"map_{mem.MemberName}_val");
                }
                if (mem.Target is IdentifierExpression enumId && _typeChecker.Enums.TryGetValue(enumId.Name, out var enumSym))
                {
                    if (enumSym.Members.TryGetValue(mem.MemberName, out var mSym))
                    {
                        return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)mSym.Value, false);
                    }
                }
                LLVMValueRef structPtr;
                string? memStructTypeName = null;
                string fieldPrefix = "mem";

                if (mem.Target is IdentifierExpression targetId && locals.TryGetValue(targetId.Name, out var memTargetIdPtr))
                {
                    structPtr = memTargetIdPtr;
                    if (varTypes.TryGetValue(targetId.Name, out var stName))
                    {
                        memStructTypeName = stName;
                    }
                    fieldPrefix = targetId.Name;
                }
                else
                {
                    var memTargetVal = CompileExpression(context, module, builder, function, mem.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var tSym = _typeChecker.GetNodeType(mem.Target);
                    memStructTypeName = tSym.Name;
                    var stType = ecs.GetComponentStructType(memStructTypeName);
                    var tempAlloca = CreateEntryBlockAlloca(context, function, stType, "tmp_mem_target");
                    builder.BuildStore(memTargetVal, tempAlloca);
                    structPtr = tempAlloca;
                    fieldPrefix = memStructTypeName;
                }

                if (memStructTypeName != null &&
                    (_typeChecker.Structs.ContainsKey(memStructTypeName) ||
                     _typeChecker.Structs.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(memStructTypeName)) ||
                     _typeChecker.Components.ContainsKey(memStructTypeName) ||
                     _typeChecker.Components.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(memStructTypeName)) ||
                     _typeChecker.Resources.ContainsKey(memStructTypeName) ||
                     _typeChecker.Events.ContainsKey(memStructTypeName)))
                {
                    var structType = ecs.GetComponentStructType(memStructTypeName);
                    int offset = ecs.GetFieldOffset(memStructTypeName, mem.MemberName);
                    var fieldGEP = builder.BuildStructGEP2(structType, structPtr, (uint)offset, $"{fieldPrefix}_{mem.MemberName}");

                    string? fieldTypeName = null;
                    if (_typeChecker.Structs.TryGetValue(memStructTypeName, out var sSym) ||
                        _typeChecker.Structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(memStructTypeName), out sSym))
                    {
                        var f = sSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                        if (f != null) fieldTypeName = f.Type.Name;
                    }
                    if (fieldTypeName == null && (_typeChecker.Components.TryGetValue(memStructTypeName, out var cSym) ||
                        _typeChecker.Components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(memStructTypeName), out cSym)))
                    {
                        var f = cSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                        if (f != null) fieldTypeName = f.Type.Name;
                    }
                    if (fieldTypeName == null && _typeChecker.Resources.TryGetValue(memStructTypeName, out var rSym))
                    {
                        var f = rSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                        if (f != null) fieldTypeName = f.Type.Name;
                    }
                    if (fieldTypeName == null && _typeChecker.Events.TryGetValue(memStructTypeName, out var evSym))
                    {
                        var f = evSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                        if (f != null) fieldTypeName = f.Type.Name;
                    }
                    if (fieldTypeName == null)
                    {
                        fieldTypeName = _typeChecker.GetNodeType(mem).Name;
                    }

                    var fieldType = MapType(context, fieldTypeName, ecs);
                    return builder.BuildLoad2(fieldType, fieldGEP, $"{mem.MemberName}_val");
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case ArrayLiteralExpression arrLit:
                return CompileArrayLiteralExpression(context, module, builder, function, arrLit, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case IndexExpression idxExpr:
                return CompileIndexExpression(context, module, builder, function, idxExpr, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case MethodCallExpression methodCall:
                var targetType = _typeChecker.GetNodeType(methodCall.Target);

                if (TryGenerateCollectionMethodCall(context, module, builder, function, methodCall, targetType, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var collMethodRes))
                {
                    return collMethodRes;
                }

                var targetVal = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                if (methodCall.MethodName == "to_string")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    return EmitToString(context, module, builder, targetVal, targetType, i8PtrType, ecs, worldPtr);
                }

                if (targetType == TypeSymbol.String && TryGenerateStringMethodCall(context, module, builder, function, methodCall, targetVal, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var strMethodRes))
                {
                    return strMethodRes;
                }

                if (targetType == TypeSymbol.StrView && TryGenerateStrViewMethodCall(context, module, builder, function, methodCall, targetVal, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var strViewMethodRes))
                {
                    return strViewMethodRes;
                }

                if (TryGenerateTaggedUnionMethodCall(context, module, builder, function, methodCall, targetVal, targetType, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var tuMethodRes))
                {
                    return tuMethodRes;
                }

                // Check if target is a struct/component with implemented method
                var structMethodName = $"{targetType.Name}_{methodCall.MethodName}";
                var structMethodFunc = module.GetNamedFunction(structMethodName);
                if (structMethodFunc.Handle == IntPtr.Zero && targetType.Name.Contains("<"))
                {
                    var cleanTargetName = TypeSymbol.ToMonomorphizedIdentifier(targetType.Name);
                    structMethodFunc = module.GetNamedFunction($"{cleanTargetName}_{methodCall.MethodName}");
                }
                if (structMethodFunc.Handle != IntPtr.Zero)
                {
                    var smArgs = new List<LLVMValueRef>();

                    // Pass pointer to self
                    LLVMValueRef selfPtr;
                    if (methodCall.Target is IdentifierExpression smTargetId && locals.TryGetValue(smTargetId.Name, out var idPtr))
                    {
                        selfPtr = idPtr;
                    }
                    else if (methodCall.Target is MemberAccessExpression memAccess &&
                             memAccess.Target is IdentifierExpression memTargetId &&
                             locals.TryGetValue(memTargetId.Name, out var basePtr) &&
                             varTypes.TryGetValue(memTargetId.Name, out var baseTypeName))
                    {
                        var baseStructType = ecs.GetComponentStructType(baseTypeName);
                        int offset = ecs.GetFieldOffset(baseTypeName, memAccess.MemberName);
                        selfPtr = builder.BuildStructGEP2(baseStructType, basePtr, (uint)offset, $"{memTargetId.Name}_{memAccess.MemberName}_ptr");
                    }
                    else
                    {
                        var stType = ecs.GetComponentStructType(targetType.Name);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, stType, "self_temp");
                        builder.BuildStore(targetVal, tempAlloca);
                        selfPtr = tempAlloca;
                    }
                    smArgs.Add(selfPtr);

                    foreach (var arg in methodCall.Arguments)
                    {
                        smArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    }

                    var smFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(structMethodFunc);
                    string smCallName = smFuncType.ReturnType == context.VoidType ? "" : $"{methodCall.MethodName}_call";
                    return builder.BuildCall2(smFuncType, structMethodFunc, smArgs.ToArray(), smCallName);
                }

                if (methodCall.MethodName == "set_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var entArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var insertFunc = _mapEmitter!.GetOrCreateInsert("string", "Entity", (t) => MapType(context, t, ecs));
                    var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                    builder.BuildCall2(ifType, insertFunc, new[] { nameMapPtr, nameArg, entArg }, "");
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                if (methodCall.MethodName == "get_by_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var getFunc = _mapEmitter!.GetOrCreateGet("string", "Entity", (t) => MapType(context, t, ecs));
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                    return builder.BuildCall2(gfType, getFunc, new[] { nameMapPtr, nameArg }, "ent_by_name");
                }

                if ((methodCall.MethodName is "find" or "find_entity") && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var containsFunc = _mapEmitter!.GetOrCreateContains("string", "Entity", (t) => MapType(context, t, ecs));
                    var getFunc = _mapEmitter!.GetOrCreateGet("string", "Entity", (t) => MapType(context, t, ecs));
                    var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);

                    var hasIt = builder.BuildCall2(cfType, containsFunc, new[] { nameMapPtr, nameArg }, "find_has_key");
                    var entLlvmType = MapType(context, "Entity", ecs);
                    var optStructType = context.GetStructType(new[] { context.Int32Type, entLlvmType }, false);
                    var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_find_res");

                    var foundBB = function.AppendBasicBlock("find_found");
                    var missingBB = function.AppendBasicBlock("find_missing");
                    var mergeBB = function.AppendBasicBlock("find_merge");

                    builder.BuildCondBr(hasIt, foundBB, missingBB);

                    builder.PositionAtEnd(foundBB);
                    var entVal = builder.BuildCall2(gfType, getFunc, new[] { nameMapPtr, nameArg }, "find_ent_val");
                    var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                    var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                    builder.BuildStore(entVal, val1);
                    builder.BuildBr(mergeBB);

                    builder.PositionAtEnd(missingBB);
                    var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                    var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                    builder.BuildStore(LLVMValueRef.CreateConstNull(entLlvmType), val0);
                    builder.BuildBr(mergeBB);

                    builder.PositionAtEnd(mergeBB);
                    return builder.BuildLoad2(optStructType, retAlloca, "opt_find_val");
                }

                if (methodCall.MethodName == "has_name" && ecs != null && ecs.NameIndexWorldOffset >= 0)
                {
                    var nameArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var nameMapPtr = builder.BuildStructGEP2(ecs.GetWorldStructType(), targetVal, (uint)ecs.NameIndexWorldOffset, "name_map_ptr");
                    var containsFunc = _mapEmitter!.GetOrCreateContains("string", "Entity", (t) => MapType(context, t, ecs));
                    var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                    return builder.BuildCall2(cfType, containsFunc, new[] { nameMapPtr, nameArg }, "has_name_val");
                }

                if (methodCall.MethodName == "reset_string_arena" && ecs != null && _arenaEmitter != null)
                {
                    var resetFunc = _arenaEmitter.GetOrCreateArenaReset(ecs);
                    var rfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(resetFunc);
                    var wPtrRaw = builder.BuildBitCast(targetVal, LLVMTypeRef.CreatePointer(context.Int8Type, 0), "w_raw");
                    builder.BuildCall2(rfType, resetFunc, new[] { wPtrRaw }, "");
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                if (methodCall.MethodName == "alloc_string" && ecs != null && _arenaEmitter != null)
                {
                    var capArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var cap64 = builder.BuildZExt(capArg, context.Int64Type, "cap_64");
                    var allocFunc = _arenaEmitter.GetOrCreateArenaAlloc(ecs);
                    var afType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(allocFunc);
                    var wPtrRaw = builder.BuildBitCast(targetVal, LLVMTypeRef.CreatePointer(context.Int8Type, 0), "w_raw");
                    return builder.BuildCall2(afType, allocFunc, new[] { wPtrRaw, cap64 }, "arena_str");
                }

                string mTargetName = $"world_{methodCall.MethodName}";
                if (methodCall.MethodName == "render_debug_overlay")
                {
                    mTargetName = "world_render_profiler";
                }

                bool isCommandsTarget = methodCall.Target is IdentifierExpression targetIdent &&
                                       varTypes.TryGetValue(targetIdent.Name, out var tType) &&
                                       tType == "Commands";

                if (isCommandsTarget)
                {
                    if (methodCall.MethodName == "spawn")
                    {
                        mTargetName = "world_cmd_spawn";
                    }
                    else if (methodCall.MethodName == "despawn")
                    {
                        mTargetName = "world_cmd_despawn";
                    }
                    else if ((methodCall.MethodName == "add" || methodCall.MethodName == "set") &&
                             methodCall.Arguments.Count == 2 &&
                             methodCall.Arguments[1] is CallExpression ctorCall)
                    {
                        mTargetName = $"world_cmd_set_{ctorCall.Callee}";
                        var ctorFunc = module.GetNamedFunction(mTargetName);
                        if (ctorFunc.Handle != IntPtr.Zero)
                        {
                            var ctorArgs = new List<LLVMValueRef>
                            {
                                targetVal,
                                CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc)
                            };
                            foreach (var arg in ctorCall.Arguments)
                            {
                                ctorArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc));
                            }
                            var ctorFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ctorFunc);
                            return builder.BuildCall2(ctorFuncType, ctorFunc, ctorArgs.ToArray(), "");
                        }
                    }
                    else
                    {
                        mTargetName = $"world_cmd_{methodCall.MethodName}";
                    }
                }
                else
                {
                    if ((methodCall.MethodName == "add" || methodCall.MethodName == "set") &&
                        methodCall.Arguments.Count == 2 &&
                        methodCall.Arguments[1] is CallExpression wCtorCall)
                    {
                        var cleanCallee = TypeSymbol.ToMonomorphizedIdentifier(wCtorCall.Callee);
                        var ctorFunc = module.GetNamedFunction($"world_set_{cleanCallee}");
                        if (ctorFunc.Handle == IntPtr.Zero)
                            ctorFunc = module.GetNamedFunction($"world_set_{wCtorCall.Callee}");
                        if (ctorFunc.Handle == IntPtr.Zero)
                            ctorFunc = module.GetNamedFunction($"world_add_{cleanCallee}");
                        if (ctorFunc.Handle == IntPtr.Zero)
                            ctorFunc = module.GetNamedFunction($"world_add_{wCtorCall.Callee}");

                        if (ctorFunc.Handle != IntPtr.Zero)
                        {
                            var ctorArgs = new List<LLVMValueRef>
                            {
                                targetVal,
                                CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc)
                            };
                            foreach (var arg in wCtorCall.Arguments)
                            {
                                ctorArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc));
                            }
                            var ctorFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ctorFunc);
                            return builder.BuildCall2(ctorFuncType, ctorFunc, ctorArgs.ToArray(), "");
                        }
                    }
                    else if (methodCall.MethodName == "emit" && methodCall.Arguments.Count == 1)
                    {
                        if (methodCall.Arguments[0] is CallExpression ctorCall)
                        {
                            mTargetName = $"world_emit_{ctorCall.Callee}";
                            var ctorFunc = module.GetNamedFunction(mTargetName);
                            if (ctorFunc.Handle != IntPtr.Zero)
                            {
                                var ctorArgs = new List<LLVMValueRef> { targetVal };
                                foreach (var arg in ctorCall.Arguments)
                                {
                                    ctorArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc));
                                }
                                var ctorFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(ctorFunc);
                                return builder.BuildCall2(ctorFuncType, ctorFunc, ctorArgs.ToArray(), "");
                            }
                        }
                    }
                }

                var mFunc = module.GetNamedFunction(mTargetName);
                if (mFunc.Handle == IntPtr.Zero)
                {
                    _diagnostics.ReportError($"Undefined ECS world method '{methodCall.MethodName}'.", methodCall.Span);
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                }

                var mArgs = new List<LLVMValueRef> { targetVal };
                foreach (var arg in methodCall.Arguments)
                {
                    mArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs!, putsType, putsFunc, printfType, printfFunc));
                }

                var mFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(mFunc);
                string mCallName = mFuncType.ReturnType == context.VoidType ? "" : $"{methodCall.MethodName}_call";
                return builder.BuildCall2(mFuncType, mFunc, mArgs.ToArray(), mCallName);

            case UnaryExpression un:
                var operand = CompileExpression(context, module, builder, function, un.Operand, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                return un.Operator switch
                {
                    UnaryOperator.Negate => operand.TypeOf == context.FloatType
                        ? builder.BuildFNeg(operand, "fneg_tmp")
                        : builder.BuildNeg(operand, "neg_tmp"),
                    UnaryOperator.LogicalNot => builder.BuildNot(operand, "not_tmp"),
                    _ => operand
                };

            case BinaryExpression bin:
                var leftType = _typeChecker.GetNodeType(bin.Left);
                var rightType = _typeChecker.GetNodeType(bin.Right);
                var left = CompileExpression(context, module, builder, function, bin.Left, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var right = CompileExpression(context, module, builder, function, bin.Right, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                // String operations
                if (leftType == TypeSymbol.String || rightType == TypeSymbol.String)
                {
                    if (bin.Operator == BinaryOperator.Add)
                    {
                        var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                        var leftStr = EmitToString(context, module, builder, left, leftType, i8PtrType, ecs, worldPtr);
                        var rightStr = EmitToString(context, module, builder, right, rightType, i8PtrType, ecs, worldPtr);
                        var concatFn = GetOrCreateStringConcatFunction(context, module, i8PtrType, ecs);
                        var concatFnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(concatFn);
                        return builder.BuildCall2(concatFnType, concatFn, new[] { worldPtr, leftStr, rightStr }, "str_add");
                    }
                    if (bin.Operator == BinaryOperator.Equal)
                    {
                        var eqFn = GetOrCreateStringEq(context, module, i8PtrType);
                        var eqFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, i8PtrType }, false);
                        return builder.BuildCall2(eqFnType, eqFn, new[] { left, right }, "str_eq");
                    }
                    if (bin.Operator == BinaryOperator.NotEqual)
                    {
                        var eqFn = GetOrCreateStringEq(context, module, i8PtrType);
                        var eqFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, i8PtrType }, false);
                        var eq = builder.BuildCall2(eqFnType, eqFn, new[] { left, right }, "str_eq");
                        return builder.BuildNot(eq, "str_neq");
                    }
                }

                if (leftType.IsOption || rightType.IsOption)
                {
                    LLVMValueRef optVal = leftType.IsOption ? left : right;
                    var optType = leftType.IsOption ? leftType : rightType;
                    var optStructType = MapType(context, optType.Name, ecs);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_cmp_tmp");
                    builder.BuildStore(optVal, tmpAlloca);
                    var tagSlot = builder.BuildStructGEP2(optStructType, tmpAlloca, 0, "tag_slot");
                    var tagVal = builder.BuildLoad2(context.Int32Type, tagSlot, "tag_val");

                    if (bin.Operator == BinaryOperator.Equal)
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "opt_is_none");
                    }
                    if (bin.Operator == BinaryOperator.NotEqual)
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "opt_is_some");
                    }
                }

                bool isDouble = left.TypeOf == context.DoubleType || right.TypeOf == context.DoubleType;
                bool isFloat = isDouble || left.TypeOf == context.FloatType || right.TypeOf == context.FloatType;

                if (isFloat)
                {
                    var targetFp = isDouble ? context.DoubleType : context.FloatType;
                    left = CoerceValue(builder, context, left, targetFp);
                    right = CoerceValue(builder, context, right, targetFp);
                }
                else if (left.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind && right.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
                {
                    uint leftWidth = left.TypeOf.IntWidth;
                    uint rightWidth = right.TypeOf.IntWidth;
                    if (leftWidth < rightWidth)
                    {
                        left = builder.BuildSExt(left, right.TypeOf, "sext_l");
                    }
                    else if (rightWidth < leftWidth)
                    {
                        right = builder.BuildSExt(right, left.TypeOf, "sext_r");
                    }
                }

                return bin.Operator switch
                {
                    BinaryOperator.Add => isFloat ? builder.BuildFAdd(left, right, "fadd") : builder.BuildAdd(left, right, "add"),
                    BinaryOperator.Subtract => isFloat ? builder.BuildFSub(left, right, "fsub") : builder.BuildSub(left, right, "sub"),
                    BinaryOperator.Multiply => isFloat ? builder.BuildFMul(left, right, "fmul") : builder.BuildMul(left, right, "mul"),
                    BinaryOperator.Divide => isFloat ? builder.BuildFDiv(left, right, "fdiv") : builder.BuildSDiv(left, right, "sdiv"),
                    BinaryOperator.Modulo => builder.BuildSRem(left, right, "srem"),

                    BinaryOperator.Equal => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOEQ, left, right, "feq")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, left, right, "eq"),
                    BinaryOperator.NotEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealONE, left, right, "fne")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, left, right, "ne"),
                    BinaryOperator.Less => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, left, right, "flt")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, left, right, "slt"),
                    BinaryOperator.LessOrEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLE, left, right, "fle")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, left, right, "sle"),
                    BinaryOperator.Greater => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, left, right, "fgt")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, left, right, "sgt"),
                    BinaryOperator.GreaterOrEqual => isFloat
                        ? builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGE, left, right, "fge")
                        : builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, left, right, "sge"),

                    BinaryOperator.LogicalAnd => builder.BuildAnd(left, right, "land"),
                    BinaryOperator.LogicalOr => builder.BuildOr(left, right, "lor"),
                    _ => left
                };

            case LambdaExpression lambda:
                return CompileLambdaExpression(context, module, builder, function, lambda, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case IndirectCallExpression ind:
                return CompileIndirectCallExpression(context, module, builder, function, ind, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

            case CallExpression call:
                if (TryCompileClosureVariableCall(context, module, builder, function, call, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var closureRes))
                {
                    return closureRes;
                }

                if (TryGenerateFileIoCall(context, module, builder, function, call, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var fileIoRes))
                {
                    return fileIoRes;
                }

                if (TryGenerateTimeCall(context, module, builder, function, call, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var timeRes))
                {
                    return timeRes;
                }

                if (TryGenerateTaggedUnionConstructorCall(context, module, builder, function, call, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var tuRes))
                {
                    return tuRes;
                }

                if (TryGenerateCollectionConstructorCall(context, builder, function, call, ecs, out var collRes))
                {
                    return collRes;
                }

                if (call.Callee is "println" or "print")
                {
                    bool addNewline = call.Callee == "println";
                    if (call.Arguments.Count > 0)
                    {
                        var arg = call.Arguments[0];
                        var argType = _typeChecker.GetNodeType(arg);
                        var val = CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        bool isValPointer = val.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind;
                        bool isValFloat = val.TypeOf.Kind == LLVMTypeKind.LLVMFloatTypeKind || val.TypeOf.Kind == LLVMTypeKind.LLVMDoubleTypeKind;

                        if (isValPointer && (argType == TypeSymbol.String || argType == TypeSymbol.Unknown))
                        {
                            if (call.Arguments.Count > 1)
                            {
                                var printfArgs = new List<LLVMValueRef> { val };
                                for (int i = 1; i < call.Arguments.Count; i++)
                                {
                                    var pArg = call.Arguments[i];
                                    var pArgType = _typeChecker.GetNodeType(pArg);
                                    var pVal = CompileExpression(context, module, builder, function, pArg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                                    if (pArgType.IsFloatingPoint || pVal.TypeOf.Kind == LLVMTypeKind.LLVMFloatTypeKind || pVal.TypeOf.Kind == LLVMTypeKind.LLVMDoubleTypeKind)
                                    {
                                        pVal = pVal.TypeOf == context.DoubleType ? pVal : builder.BuildFPExt(pVal, context.DoubleType, "f_to_d");
                                    }
                                    else if (pVal.TypeOf == context.Int1Type || pArgType == TypeSymbol.Bool)
                                    {
                                        pVal = builder.BuildZExt(pVal, context.Int32Type, "b_to_i32");
                                    }
                                    else if (pVal.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind && pVal.TypeOf.IntWidth < 32)
                                    {
                                        pVal = builder.BuildZExt(pVal, context.Int32Type, "int_to_i32");
                                    }
                                    printfArgs.Add(pVal);
                                }
                                var res = builder.BuildCall2(printfType, printfFunc, printfArgs.ToArray(), "printf_call");
                                if (addNewline)
                                {
                                    var nlStr = builder.BuildGlobalStringPtr("\n", "nl_s");
                                    builder.BuildCall2(printfType, printfFunc, new[] { nlStr }, "printf_nl");
                                }
                                return res;
                            }
                            else if (addNewline)
                            {
                                return builder.BuildCall2(putsType, putsFunc, new[] { val }, "puts_call");
                            }
                            else
                            {
                                var fmt = builder.BuildGlobalStringPtr("%s", "fmt_s");
                                return builder.BuildCall2(printfType, printfFunc, new[] { fmt, val }, "printf_call");
                            }
                        }
                        else if (isValFloat || argType.IsFloatingPoint)
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%f\n" : "%f", "fmt_f");
                            var doubleVal = val.TypeOf == context.DoubleType ? val : builder.BuildFPExt(val, context.DoubleType, "f_to_d");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, doubleVal }, "printf_call");
                        }
                        else if (val.TypeOf == context.Int1Type || argType == TypeSymbol.Bool)
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%d\n" : "%d", "fmt_b");
                            var i32Val = builder.BuildZExt(val, context.Int32Type, "b_to_i32");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, i32Val }, "printf_call");
                        }
                        else if (argType == TypeSymbol.StrView || (val.TypeOf.Kind == LLVMTypeKind.LLVMStructTypeKind && val.TypeOf.StructElementTypesCount == 2))
                        {
                            var vPtr = builder.BuildExtractValue(val, 0, "view_p");
                            var vLen = builder.BuildExtractValue(val, 1, "view_l");
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%.*s\n" : "%.*s", "fmt_str_view");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, vLen, vPtr }, "printf_call");
                        }
                        else if (isValPointer)
                        {
                            if (addNewline)
                            {
                                return builder.BuildCall2(putsType, putsFunc, new[] { val }, "puts_call");
                            }
                            else
                            {
                                var fmt = builder.BuildGlobalStringPtr("%s", "fmt_s");
                                return builder.BuildCall2(printfType, printfFunc, new[] { fmt, val }, "printf_call");
                            }
                        }
                        else
                        {
                            var fmt = builder.BuildGlobalStringPtr(addNewline ? "%d\n" : "%d", "fmt_d");
                            return builder.BuildCall2(printfType, printfFunc, new[] { fmt, val }, "printf_call");
                        }
                    }
                    else if (addNewline)
                    {
                        var emptyStr = builder.BuildGlobalStringPtr("", "empty_s");
                        return builder.BuildCall2(putsType, putsFunc, new[] { emptyStr }, "puts_call");
                    }
                    return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
                }
                else if (call.Callee == "wait_key")
                {
                    if (_options.NoWaitOnExit)
                    {
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
                    }

                    return EmitWaitKey(context, module, builder, "key_input");
                }
                else if (call.Callee == "readln")
                {
                    var getcharFunc = module.GetNamedFunction("getchar");
                    var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "read_input");
                }
                else if (call.Callee is "get_tick_count" or "time_ms")
                {
                    var f = module.GetNamedFunction("GetTickCount");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "tick_count");
                }
                else if (call.Callee == "to_string")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var argType = _typeChecker.GetNodeType(call.Arguments[0]);
                    return EmitToString(context, module, builder, arg, argType, i8PtrType, ecs, worldPtr);
                }
                else if (call.Callee == "str_concat")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    var arg1 = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var arg2 = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var t1 = _typeChecker.GetNodeType(call.Arguments[0]);
                    var t2 = _typeChecker.GetNodeType(call.Arguments[1]);
                    var s1 = EmitToString(context, module, builder, arg1, t1, i8PtrType, ecs, worldPtr);
                    var s2 = EmitToString(context, module, builder, arg2, t2, i8PtrType, ecs, worldPtr);
                    var concatFn = GetOrCreateStringConcatFunction(context, module, i8PtrType, ecs);
                    var concatFnType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(concatFn);
                    return builder.BuildCall2(concatFnType, concatFn, new[] { worldPtr, s1, s2 }, "str_concat");
                }
                else if (call.Callee is "str_len" or "string_length")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { arg }, "strlen_call");
                    return builder.BuildTrunc(len64, context.Int32Type, "len_i32");
                }
                else if (call.Callee is "sqrt" or "sin" or "cos" or "floor" or "ceil")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var fVal = EnsureFloat(context, builder, arg);
                    string cName = call.Callee switch
                    {
                        "sqrt" => "sqrtf",
                        "sin" => "sinf",
                        "cos" => "cosf",
                        "floor" => "floorf",
                        "ceil" => "ceilf",
                        _ => "sqrtf"
                    };
                    var fn = module.GetNamedFunction(cName);
                    var fnType = LLVMTypeRef.CreateFunction(context.FloatType, new[] { context.FloatType }, false);
                    return builder.BuildCall2(fnType, fn, new[] { fVal }, $"{call.Callee}_call");
                }
                else if (call.Callee == "abs")
                {
                    var arg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (arg.TypeOf == context.FloatType || arg.TypeOf == context.DoubleType)
                    {
                        var fVal = EnsureFloat(context, builder, arg);
                        var zero = LLVMValueRef.CreateConstReal(context.FloatType, 0.0);
                        var isNeg = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fVal, zero, "is_neg_f");
                        var negVal = builder.BuildFNeg(fVal, "fneg_val");
                        return builder.BuildSelect(isNeg, negVal, fVal, "abs_res_f");
                    }
                    else
                    {
                        var iVal = EnsureInt32(context, builder, arg);
                        var isNeg = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, iVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_neg");
                        var negVal = builder.BuildNeg(iVal, "neg_val");
                        return builder.BuildSelect(isNeg, negVal, iVal, "abs_res");
                    }
                }
                else if (call.Callee == "min")
                {
                    var a = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var bVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (a.TypeOf == context.FloatType || bVal.TypeOf == context.FloatType)
                    {
                        var fa = EnsureFloat(context, builder, a);
                        var fb = EnsureFloat(context, builder, bVal);
                        var cmp = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fa, fb, "min_cmp");
                        return builder.BuildSelect(cmp, fa, fb, "min_val");
                    }
                    else
                    {
                        var ia = EnsureInt32(context, builder, a);
                        var ib = EnsureInt32(context, builder, bVal);
                        var cmp = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, ia, ib, "min_cmp");
                        return builder.BuildSelect(cmp, ia, ib, "min_val");
                    }
                }
                else if (call.Callee == "max")
                {
                    var a = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var bVal = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (a.TypeOf == context.FloatType || bVal.TypeOf == context.FloatType)
                    {
                        var fa = EnsureFloat(context, builder, a);
                        var fb = EnsureFloat(context, builder, bVal);
                        var cmp = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, fa, fb, "max_cmp");
                        return builder.BuildSelect(cmp, fa, fb, "max_val");
                    }
                    else
                    {
                        var ia = EnsureInt32(context, builder, a);
                        var ib = EnsureInt32(context, builder, bVal);
                        var cmp = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, ia, ib, "max_cmp");
                        return builder.BuildSelect(cmp, ia, ib, "max_val");
                    }
                }
                else if (call.Callee == "clamp")
                {
                    var valArg = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var minArg = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var maxArg = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    if (valArg.TypeOf == context.FloatType || minArg.TypeOf == context.FloatType || maxArg.TypeOf == context.FloatType)
                    {
                        var fv = EnsureFloat(context, builder, valArg);
                        var fmin = EnsureFloat(context, builder, minArg);
                        var fmax = EnsureFloat(context, builder, maxArg);
                        var cmpMin = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOLT, fv, fmin, "cmp_min");
                        var clamp1 = builder.BuildSelect(cmpMin, fmin, fv, "clamp1");
                        var cmpMax = builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGT, clamp1, fmax, "cmp_max");
                        return builder.BuildSelect(cmpMax, fmax, clamp1, "clamp_res");
                    }
                    else
                    {
                        var iv = EnsureInt32(context, builder, valArg);
                        var imin = EnsureInt32(context, builder, minArg);
                        var imax = EnsureInt32(context, builder, maxArg);
                        var cmpMin = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, iv, imin, "cmp_min");
                        var clamp1 = builder.BuildSelect(cmpMin, imin, iv, "clamp1");
                        var cmpMax = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, clamp1, imax, "cmp_max");
                        return builder.BuildSelect(cmpMax, imax, clamp1, "clamp_res");
                    }
                }
                else if (call.Callee == "rand")
                {
                    var randFn = module.GetNamedFunction("rand");
                    var randFnType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(randFnType, randFn, Array.Empty<LLVMValueRef>(), "rand_val");
                }
                else if (call.Callee == "rand_range")
                {
                    var minVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var maxVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var randFn = module.GetNamedFunction("rand");
                    var randFnType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    var r = builder.BuildCall2(randFnType, randFn, Array.Empty<LLVMValueRef>(), "rand_r");
                    var diff = builder.BuildSub(maxVal, minVal, "diff");
                    var span = builder.BuildAdd(diff, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "span");
                    var rem = builder.BuildSRem(r, span, "rem");
                    return builder.BuildAdd(minVal, rem, "rand_in_range");
                }
                else if (call.Callee is "ecs::create_world" or "create_world")
                {
                    var createWorldFunc = module.GetNamedFunction("ecs_create_world");
                    var createWorldType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0), Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(createWorldType, createWorldFunc, Array.Empty<LLVMValueRef>(), "new_world");
                }
                if (TryGenerateRaylibCall(context, module, builder, function, call, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc, out var rlRes))
                {
                    return rlRes;
                }
                else if (_typeChecker.Structs.TryGetValue(call.Callee, out var stSym) ||
                         _typeChecker.Structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out stSym))
                {
                    var cleanCallee = TypeSymbol.ToMonomorphizedIdentifier(stSym.Name);
                    var stType = ecs.GetComponentStructType(stSym.Name);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, stType, $"tmp_{cleanCallee}");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        var argVal = CompileExpression(context, module, builder, function, call.Arguments[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fieldGEP = builder.BuildStructGEP2(stType, tmpAlloca, (uint)i, $"tmp_{cleanCallee}_{stSym.Fields[i].Name}");
                        builder.BuildStore(argVal, fieldGEP);
                    }
                    return builder.BuildLoad2(stType, tmpAlloca, $"st_val_{cleanCallee}");
                }
                else if (_typeChecker.Components.TryGetValue(call.Callee, out var compSym) ||
                         _typeChecker.Components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out compSym))
                {
                    var cleanCallee = TypeSymbol.ToMonomorphizedIdentifier(compSym.Name);
                    var stType = ecs.GetComponentStructType(compSym.Name);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, stType, $"tmp_{cleanCallee}");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        var argVal = CompileExpression(context, module, builder, function, call.Arguments[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fieldGEP = builder.BuildStructGEP2(stType, tmpAlloca, (uint)i, $"tmp_{cleanCallee}_{compSym.Fields[i].Name}");
                        builder.BuildStore(argVal, fieldGEP);
                    }
                    return builder.BuildLoad2(stType, tmpAlloca, $"st_val_{cleanCallee}");
                }
                else
                {
                    // Generic function call or ECS call (world_spawn, world_set_*, pipeline_*)
                    string funcName = call.Callee;
                    if (call.Callee.Contains("::"))
                    {
                        funcName = call.Callee.Replace("::", "_");
                    }
                    else if (call.Callee.StartsWith("raw_net_"))
                    {
                        funcName = $"ecs_{call.Callee.Substring(4)}";
                    }
                    else if (call.Callee.StartsWith("net_") && !_typeChecker.Functions.ContainsKey(call.Callee))
                    {
                        funcName = $"ecs_{call.Callee}";
                    }
                    else if (_typeChecker.Pipelines.Any(p => p.Name == call.Callee))
                    {
                        funcName = $"pipeline_{call.Callee}";
                    }
                    else if (_typeChecker.Systems.ContainsKey(call.Callee))
                    {
                        funcName = $"system_{call.Callee}";
                    }

                    if (funcName.Contains("<"))
                    {
                        funcName = TypeSymbol.ToMonomorphizedIdentifier(funcName);
                    }

                    var targetFunc = module.GetNamedFunction(funcName);
                    if (targetFunc.Handle == IntPtr.Zero)
                    {
                        // Check if registered in _typeChecker.Functions with an alias or monomorphized name
                        if (_typeChecker.Functions.TryGetValue(call.Callee, out var fnDecl))
                        {
                            targetFunc = module.GetNamedFunction(fnDecl.Name);
                            if (targetFunc.Handle == IntPtr.Zero)
                            {
                                targetFunc = module.GetNamedFunction(TypeSymbol.ToMonomorphizedIdentifier(fnDecl.Name));
                            }
                        }
                    }

                    if (targetFunc.Handle == IntPtr.Zero)
                    {
                        _diagnostics.ReportError($"Undefined function or system '{funcName}'.", call.Span);
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    var argValues = call.Arguments.Select(a => CompileExpression(context, module, builder, function, a, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)).ToArray();
                    for (int i = 0; i < argValues.Length && i < (int)targetFunc.ParamsCount; i++)
                    {
                        var expectedParamType = targetFunc.GetParam((uint)i).TypeOf;
                        if (expectedParamType == context.Int64Type && argValues[i].TypeOf == context.Int32Type)
                        {
                            argValues[i] = builder.BuildZExt(argValues[i], context.Int64Type, "zext_i64");
                        }
                    }

                    var targetFuncType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(targetFunc);
                    string callName = targetFuncType.ReturnType == context.VoidType ? "" : $"{call.Callee}_call";
                    return builder.BuildCall2(targetFuncType, targetFunc, argValues, callName);
                }
        }

        return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);
    }

    private unsafe LLVMValueRef EmitConstantLiteral(LLVMContextRef context, LLVMBuilderRef builder, ConstSymbol constSym)
    {
        if (constSym.Type == TypeSymbol.F32)
        {
            float f = Convert.ToSingle(constSym.Value, System.Globalization.CultureInfo.InvariantCulture);
            return LLVMValueRef.CreateConstReal(context.FloatType, f);
        }
        if (constSym.Type == TypeSymbol.F64)
        {
            double d = Convert.ToDouble(constSym.Value, System.Globalization.CultureInfo.InvariantCulture);
            return LLVMValueRef.CreateConstReal(context.DoubleType, d);
        }
        if (constSym.Type == TypeSymbol.Bool)
        {
            bool b = Convert.ToBoolean(constSym.Value);
            return LLVMValueRef.CreateConstInt(context.Int1Type, b ? 1UL : 0UL, false);
        }
        if (constSym.Type == TypeSymbol.String)
        {
            return builder.BuildGlobalStringPtr((string)constSym.Value, "const_str");
        }
        if (constSym.Type == TypeSymbol.I64 || constSym.Type.Name == "u64")
        {
            long l = Convert.ToInt64(constSym.Value);
            return LLVMValueRef.CreateConstInt(context.Int64Type, (ulong)l, false);
        }
        long iVal = Convert.ToInt64(constSym.Value);
        return LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)iVal, false);
    }
}
