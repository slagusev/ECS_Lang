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

            case IdentifierExpression ident:
                if (locals.TryGetValue(ident.Name, out var varPtr))
                {
                    string typeName = varTypes.TryGetValue(ident.Name, out var vt) ? vt : _typeChecker.GetNodeType(ident).Name;
                    var llvmType = MapType(context, typeName, ecs);
                    return builder.BuildLoad2(llvmType, varPtr, ident.Name);
                }
                if (ident.Name == "None" || (ident.Name.StartsWith("Option<") && ident.Name.EndsWith("::None")))
                {
                    var optType = _typeChecker.GetNodeType(ident);
                    var optLlvmType = MapType(context, optType.Name, ecs);
                    var tmpAlloca = CreateEntryBlockAlloca(context, function, optLlvmType, "tmp_none");
                    var tagSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 0, "opt_tag");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot);
                    if (optType.TryGetOptionInfo(out var innerSym))
                    {
                        var valSlot = builder.BuildStructGEP2(optLlvmType, tmpAlloca, 1, "opt_val");
                        builder.BuildStore(LLVMValueRef.CreateConstNull(MapType(context, innerSym.Name, ecs)), valSlot);
                    }
                    return builder.BuildLoad2(optLlvmType, tmpAlloca, "none_val");
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
                if (mem.Target is IdentifierExpression targetId && locals.TryGetValue(targetId.Name, out var structPtr))
                {
                    if (varTypes.TryGetValue(targetId.Name, out var structTypeName) &&
                        (_typeChecker.Structs.ContainsKey(structTypeName) ||
                         _typeChecker.Structs.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(structTypeName)) ||
                         _typeChecker.Components.ContainsKey(structTypeName) ||
                         _typeChecker.Components.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(structTypeName)) ||
                         _typeChecker.Resources.ContainsKey(structTypeName) ||
                         _typeChecker.Events.ContainsKey(structTypeName)))
                    {
                        var structType = ecs.GetComponentStructType(structTypeName);
                        int offset = ecs.GetFieldOffset(structTypeName, mem.MemberName);
                        var fieldGEP = builder.BuildStructGEP2(structType, structPtr, (uint)offset, $"{targetId.Name}_{mem.MemberName}");

                        string? fieldTypeName = null;
                        if (_typeChecker.Structs.TryGetValue(structTypeName, out var sSym) ||
                            _typeChecker.Structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(structTypeName), out sSym))
                        {
                            var f = sSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                            if (f != null) fieldTypeName = f.Type.Name;
                        }
                        if (fieldTypeName == null && (_typeChecker.Components.TryGetValue(structTypeName, out var cSym) ||
                            _typeChecker.Components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(structTypeName), out cSym)))
                        {
                            var f = cSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                            if (f != null) fieldTypeName = f.Type.Name;
                        }
                        if (fieldTypeName == null && _typeChecker.Resources.TryGetValue(structTypeName, out var rSym))
                        {
                            var f = rSym.Fields.FirstOrDefault(x => x.Name == mem.MemberName);
                            if (f != null) fieldTypeName = f.Type.Name;
                        }
                        if (fieldTypeName == null && _typeChecker.Events.TryGetValue(structTypeName, out var evSym))
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
                }
                return LLVMValueRef.CreateConstInt(context.Int32Type, 0, false);

            case ArrayLiteralExpression arrLit:
                var arrTypeSym = _typeChecker.GetNodeType(arrLit);
                if (arrTypeSym.IsDynamicArray)
                {
                    arrTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, arrTypeSym.Name, ecs);
                    var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_arr");

                    int elemCount = arrLit.Elements.Count;
                    if (elemCount == 0)
                    {
                        var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
                        var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                        var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                        builder.BuildStore(dataZero, p0);
                        var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                        builder.BuildStore(zeroInt, p1);
                        var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                        builder.BuildStore(zeroInt, p2);
                    }
                    else
                    {
                        ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                        ulong totalBytes = (ulong)elemCount * elemSize;
                        var mallocFunc = module.GetNamedFunction("malloc");
                        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                        var rawBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, totalBytes) }, "dyn_buf");
                        var typedBuf = builder.BuildBitCast(rawBuf, LLVMTypeRef.CreatePointer(dynElemType, 0), "dyn_buf_typed");

                        for (int i = 0; i < elemCount; i++)
                        {
                            var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                            var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                            var elemGEP = builder.BuildInBoundsGEP2(dynElemType, typedBuf, new[] { idxVal }, $"dyn_elem_{i}");
                            builder.BuildStore(elemVal, elemGEP);
                        }

                        var countVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)elemCount);
                        var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                        builder.BuildStore(typedBuf, p0);
                        var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                        builder.BuildStore(countVal, p1);
                        var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                        builder.BuildStore(countVal, p2);
                    }

                    return builder.BuildLoad2(dynArrStructType, dynArrAlloca, "dyn_arr_val");
                }
                else
                {
                    var llvmArrType = MapType(context, arrTypeSym.Name, ecs);
                    var arrAlloca = CreateEntryBlockAlloca(context, function, llvmArrType, "arr_lit");
                    var zeroConst = LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    for (int i = 0; i < arrLit.Elements.Count; i++)
                    {
                        var elemVal = CompileExpression(context, module, builder, function, arrLit.Elements[i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var idxVal = LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i);
                        var elemGEP = builder.BuildInBoundsGEP2(llvmArrType, arrAlloca, new[] { zeroConst, idxVal }, $"arr_elem_{i}");
                        builder.BuildStore(elemVal, elemGEP);
                    }
                    return builder.BuildLoad2(llvmArrType, arrAlloca, "arr_lit_val");
                }

            case IndexExpression idxExpr:
                var elemType = MapType(context, _typeChecker.GetNodeType(idxExpr).Name, ecs);
                var indexVal = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                var zeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                var idxTargetType = _typeChecker.GetNodeType(idxExpr.Target);
                if (idxTargetType.IsMap)
                {
                    idxTargetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                    var mapKeyVal = CompileExpression(context, module, builder, function, idxExpr.Index, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mapStructType = MapType(context, idxTargetType.Name, ecs);

                    LLVMValueRef mapStructPtr;
                    if (idxExpr.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                    var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                    return builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, mapKeyVal }, "map_get_idx");
                }

                if (idxTargetType.IsDynamicArray)
                {
                    idxTargetType.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, idxTargetType.Name, ecs);
                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                    LLVMValueRef dynStructPtr;
                    if (idxExpr.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                    var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "dyn_data_ptr");
                    var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { indexVal }, "dyn_elem_gep");
                    return builder.BuildLoad2(dynElemType, elemGEP, "dyn_elem_val");
                }

                if (idxExpr.Target is IdentifierExpression idxTargetId && locals.TryGetValue(idxTargetId.Name, out var targetArrPtr))
                {
                    var arrTypeName = varTypes[idxTargetId.Name];
                    var arrType = MapType(context, arrTypeName, ecs);
                    var elemGEP = builder.BuildInBoundsGEP2(arrType, targetArrPtr, new[] { zeroIdx, indexVal }, $"{idxTargetId.Name}_idx_gep");
                    return builder.BuildLoad2(elemType, elemGEP, $"{idxTargetId.Name}_elem_val");
                }
                else if (idxExpr.Target is MemberAccessExpression memAccess && memAccess.Target is IdentifierExpression memTargetId && locals.TryGetValue(memTargetId.Name, out var structPtr2))
                {
                    if (varTypes.TryGetValue(memTargetId.Name, out var structTypeName))
                    {
                        var structType = ecs.GetComponentStructType(structTypeName);
                        int offset = ecs.GetFieldOffset(structTypeName, memAccess.MemberName);
                        var fieldGEP = builder.BuildStructGEP2(structType, structPtr2, (uint)offset, $"{memTargetId.Name}_{memAccess.MemberName}");
                        var memType = _typeChecker.GetMemberType(structTypeName, memAccess.MemberName, memAccess.Span);
                        var fieldArrType = MapType(context, memType.Name, ecs);
                        var elemGEP = builder.BuildInBoundsGEP2(fieldArrType, fieldGEP, new[] { zeroIdx, indexVal }, $"{memAccess.MemberName}_idx_gep");
                        return builder.BuildLoad2(elemType, elemGEP, $"{memAccess.MemberName}_elem_val");
                    }
                }

                // Fallback: evaluate target expression into temporary
                var targetValExpr = CompileExpression(context, module, builder, function, idxExpr.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var tmpArrAlloca = CreateEntryBlockAlloca(context, function, targetValExpr.TypeOf, "tmp_idx_arr");
                builder.BuildStore(targetValExpr, tmpArrAlloca);
                var fallbackElemGEP = builder.BuildInBoundsGEP2(targetValExpr.TypeOf, tmpArrAlloca, new[] { zeroIdx, indexVal }, "tmp_elem_gep");
                return builder.BuildLoad2(elemType, fallbackElemGEP, "tmp_elem_val");

            case MethodCallExpression methodCall:
                var targetType = _typeChecker.GetNodeType(methodCall.Target);

                if (targetType.IsDynamicArray)
                {
                    targetType.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, targetType.Name, ecs);
                    var elemPtrType = LLVMTypeRef.CreatePointer(dynElemType, 0);

                    LLVMValueRef dynStructPtr;
                    if (methodCall.Target is IdentifierExpression dynTargetId && locals.TryGetValue(dynTargetId.Name, out var idPtr))
                    {
                        dynStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "dyn_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        dynStructPtr = tempAlloca;
                    }

                    if (methodCall.MethodName is "len" or "length" or "capacity")
                    {
                        uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                        var fieldSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, fieldIdx, $"dyn_{methodCall.MethodName}_slot");
                        return builder.BuildLoad2(context.Int32Type, fieldSlot, $"dyn_{methodCall.MethodName}_val");
                    }

                    if (methodCall.MethodName == "clear")
                    {
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), lenSlot);
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "push")
                    {
                        var itemVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var capSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 2, "dyn_cap_slot");

                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var curCap = builder.BuildLoad2(context.Int32Type, capSlot, "cur_cap");

                        var isFull = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curLen, curCap, "is_full");

                        var growBB = function.AppendBasicBlock("dyn_grow");
                        var insertBB = function.AppendBasicBlock("dyn_insert");

                        builder.BuildCondBr(isFull, growBB, insertBB);

                        // growBB:
                        builder.PositionAtEnd(growBB);
                        var isCapZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_cap_zero");
                        var doubleCap = builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(context.Int32Type, 2), "double_cap");
                        var newCap = builder.BuildSelect(isCapZero, LLVMValueRef.CreateConstInt(context.Int32Type, 4), doubleCap, "new_cap");
                        builder.BuildStore(newCap, capSlot);

                        // realloc(data, newCap * sizeof(elem))
                        ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                        var newCap64 = builder.BuildZExt(newCap, context.Int64Type, "new_cap_64");
                        var newSizeBytes = builder.BuildMul(newCap64, LLVMValueRef.CreateConstInt(context.Int64Type, elemSize), "new_size_bytes");

                        var oldData = builder.BuildLoad2(elemPtrType, dataSlot, "old_data");
                        var oldDataRaw = builder.BuildBitCast(oldData, i8PtrType, "old_data_raw");

                        var reallocFunc = module.GetNamedFunction("realloc");
                        var reallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { i8PtrType, context.Int64Type }, false);
                        var newDataRaw = builder.BuildCall2(reallocType, reallocFunc, new[] { oldDataRaw, newSizeBytes }, "new_data_raw");
                        var newDataTyped = builder.BuildBitCast(newDataRaw, elemPtrType, "new_data_typed");
                        builder.BuildStore(newDataTyped, dataSlot);

                        builder.BuildBr(insertBB);

                        // insertBB:
                        builder.PositionAtEnd(insertBB);
                        var curDataAfter = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data_after");
                        var curLenAfter = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len_after");
                        var insertGEP = builder.BuildInBoundsGEP2(dynElemType, curDataAfter, new[] { curLenAfter }, "insert_elem_gep");
                        builder.BuildStore(itemVal, insertGEP);

                        var nextLen = builder.BuildAdd(curLenAfter, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "next_len");
                        builder.BuildStore(nextLen, lenSlot);

                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "pop")
                    {
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");

                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var newLen = builder.BuildSub(curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_len");
                        builder.BuildStore(newLen, lenSlot);

                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");
                        var elemGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { newLen }, "pop_elem_gep");
                        return builder.BuildLoad2(dynElemType, elemGEP, "pop_val");
                    }

                    if (methodCall.MethodName == "get")
                    {
                        var idxVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        idxVal = EnsureInt32(context, builder, idxVal, "arr_get_idx");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var dataPtr = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var geZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "ge_zero");
                        var ltLen = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "lt_len");
                        var inBounds = builder.BuildAnd(geZero, ltLen, "in_bounds");

                        var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, dynElemType }, false);
                        var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_arr_get_res");

                        var inBB = function.AppendBasicBlock("arr_get_in");
                        var outBB = function.AppendBasicBlock("arr_get_out");
                        var mergeBB = function.AppendBasicBlock("arr_get_merge");

                        builder.BuildCondBr(inBounds, inBB, outBB);

                        builder.PositionAtEnd(inBB);
                        var elemGEP = builder.BuildInBoundsGEP2(dynElemType, dataPtr, new[] { idxVal }, "elem_gep");
                        var elemVal = builder.BuildLoad2(dynElemType, elemGEP, "elem_val");
                        var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                        var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                        builder.BuildStore(elemVal, val1);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(outBB);
                        var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                        var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                        builder.BuildStore(LLVMValueRef.CreateConstNull(dynElemType), val0);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(optStructType, retAlloca, "opt_arr_val");
                    }

                    if (methodCall.MethodName == "for_each")
                    {
                        var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fnRaw = builder.BuildExtractValue(closureVal, 0, "fe_fn_raw");
                        var envPtr = builder.BuildExtractValue(closureVal, 1, "fe_env_ptr");

                        var feFnType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, dynElemType }, false);
                        var feTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(feFnType, 0), "fe_typed_fn");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "fe_idx");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                        var condBB = function.AppendBasicBlock("fe_cond");
                        var bodyBB = function.AppendBasicBlock("fe_body");
                        var exitBB = function.AppendBasicBlock("fe_exit");

                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(condBB);
                        var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "fe_i");
                        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "fe_has_more");
                        builder.BuildCondBr(hasMore, bodyBB, exitBB);

                        builder.PositionAtEnd(bodyBB);
                        var elemGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "fe_elem_gep");
                        var elemVal = builder.BuildLoad2(dynElemType, elemGEP, "fe_elem_val");
                        builder.BuildCall2(feFnType, feTypedFn, new[] { envPtr, elemVal }, "");
                        var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "fe_next_i");
                        builder.BuildStore(nextIdx, idxAlloca);
                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(exitBB);
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "map")
                    {
                        var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fnRaw = builder.BuildExtractValue(closureVal, 0, "map_fn_raw");
                        var envPtr = builder.BuildExtractValue(closureVal, 1, "map_env_ptr");

                        var retTypeSym = _typeChecker.GetNodeType(methodCall);
                        retTypeSym.TryGetDynamicArrayElement(out var resElemSym);
                        var resElemType = MapType(context, resElemSym.Name, ecs);
                        var resArrStructType = MapType(context, retTypeSym.Name, ecs);
                        var resElemPtrType = LLVMTypeRef.CreatePointer(resElemType, 0);

                        var mapFnType = LLVMTypeRef.CreateFunction(resElemType, new[] { i8PtrType, dynElemType }, false);
                        var mapTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(mapFnType, 0), "map_typed_fn");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var resAlloca = CreateEntryBlockAlloca(context, function, resArrStructType, "map_res_arr");
                        var rDataSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 0, "r_data");
                        var rLenSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 1, "r_len");
                        var rCapSlot = builder.BuildStructGEP2(resArrStructType, resAlloca, 2, "r_cap");

                        ulong resElemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, resElemType));
                        var curLen64 = builder.BuildZExt(curLen, context.Int64Type, "cur_len64");
                        var sizeBytes = builder.BuildMul(curLen64, LLVMValueRef.CreateConstInt(context.Int64Type, resElemSize), "size_bytes");
                        var isZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_zero");
                        var allocBytes = builder.BuildSelect(isZero, LLVMValueRef.CreateConstInt(context.Int64Type, 16), sizeBytes, "alloc_bytes");

                        var mallocFunc = module.GetNamedFunction("malloc");
                        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                        var newMem = builder.BuildCall2(mallocType, mallocFunc, new[] { allocBytes }, "map_new_mem");
                        var newTypedMem = builder.BuildBitCast(newMem, resElemPtrType, "new_typed_mem");

                        builder.BuildStore(newTypedMem, rDataSlot);
                        builder.BuildStore(curLen, rLenSlot);
                        builder.BuildStore(curLen, rCapSlot);

                        var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "map_idx");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                        var condBB = function.AppendBasicBlock("map_cond");
                        var bodyBB = function.AppendBasicBlock("map_body");
                        var exitBB = function.AppendBasicBlock("map_exit");

                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(condBB);
                        var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "map_i");
                        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "map_has_more");
                        builder.BuildCondBr(hasMore, bodyBB, exitBB);

                        builder.PositionAtEnd(bodyBB);
                        var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                        var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                        var mappedVal = builder.BuildCall2(mapFnType, mapTypedFn, new[] { envPtr, inVal }, "mapped_val");

                        var outGEP = builder.BuildInBoundsGEP2(resElemType, newTypedMem, new[] { idxVal }, "out_gep");
                        builder.BuildStore(mappedVal, outGEP);

                        var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "map_next_i");
                        builder.BuildStore(nextIdx, idxAlloca);
                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(exitBB);
                        return builder.BuildLoad2(resArrStructType, resAlloca, "map_res");
                    }

                    if (methodCall.MethodName == "filter")
                    {
                        var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fnRaw = builder.BuildExtractValue(closureVal, 0, "filter_fn_raw");
                        var envPtr = builder.BuildExtractValue(closureVal, 1, "filter_env_ptr");

                        var filterFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                        var filterTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(filterFnType, 0), "filter_typed_fn");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var resAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "filter_res_arr");
                        var rDataSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 0, "r_data");
                        var rLenSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 1, "r_len");
                        var rCapSlot = builder.BuildStructGEP2(dynArrStructType, resAlloca, 2, "r_cap");

                        ulong elemSize = Math.Max(1, LlvmApi.ABISizeOfType(_dataLayout, dynElemType));
                        var curLen64 = builder.BuildZExt(curLen, context.Int64Type, "cur_len64");
                        var sizeBytes = builder.BuildMul(curLen64, LLVMValueRef.CreateConstInt(context.Int64Type, elemSize), "size_bytes");
                        var isZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curLen, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_zero");
                        var allocBytes = builder.BuildSelect(isZero, LLVMValueRef.CreateConstInt(context.Int64Type, 16), sizeBytes, "alloc_bytes");

                        var mallocFunc = module.GetNamedFunction("malloc");
                        var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                        var newMem = builder.BuildCall2(mallocType, mallocFunc, new[] { allocBytes }, "filter_mem");
                        var newTypedMem = builder.BuildBitCast(newMem, elemPtrType, "filter_typed_mem");

                        builder.BuildStore(newTypedMem, rDataSlot);
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), rLenSlot);
                        builder.BuildStore(curLen, rCapSlot);

                        var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "filter_idx");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                        var condBB = function.AppendBasicBlock("filter_cond");
                        var bodyBB = function.AppendBasicBlock("filter_body");
                        var checkBB = function.AppendBasicBlock("filter_check");
                        var nextBB = function.AppendBasicBlock("filter_next");
                        var exitBB = function.AppendBasicBlock("filter_exit");

                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(condBB);
                        var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "filter_i");
                        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "filter_has_more");
                        builder.BuildCondBr(hasMore, bodyBB, exitBB);

                        builder.PositionAtEnd(bodyBB);
                        var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                        var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                        var pass = builder.BuildCall2(filterFnType, filterTypedFn, new[] { envPtr, inVal }, "filter_pass");
                        builder.BuildCondBr(pass, checkBB, nextBB);

                        builder.PositionAtEnd(checkBB);
                        var curOutLen = builder.BuildLoad2(context.Int32Type, rLenSlot, "out_len");
                        var outGEP = builder.BuildInBoundsGEP2(dynElemType, newTypedMem, new[] { curOutLen }, "out_gep");
                        builder.BuildStore(inVal, outGEP);
                        var newOutLen = builder.BuildAdd(curOutLen, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "new_out_len");
                        builder.BuildStore(newOutLen, rLenSlot);
                        builder.BuildBr(nextBB);

                        builder.PositionAtEnd(nextBB);
                        var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "filter_next_i");
                        builder.BuildStore(nextIdx, idxAlloca);
                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(exitBB);
                        return builder.BuildLoad2(dynArrStructType, resAlloca, "filter_res");
                    }

                    if (methodCall.MethodName is "any" or "all")
                    {
                        bool isAny = methodCall.MethodName == "any";
                        var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fnRaw = builder.BuildExtractValue(closureVal, 0, "pred_fn_raw");
                        var envPtr = builder.BuildExtractValue(closureVal, 1, "pred_env_ptr");

                        var predFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                        var predTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(predFnType, 0), "pred_typed_fn");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var retAlloca = CreateEntryBlockAlloca(context, function, context.Int1Type, "pred_res");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, isAny ? 0UL : 1UL), retAlloca);

                        var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "pred_idx");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                        var condBB = function.AppendBasicBlock("pred_cond");
                        var bodyBB = function.AppendBasicBlock("pred_body");
                        var earlyBB = function.AppendBasicBlock("pred_early");
                        var nextBB = function.AppendBasicBlock("pred_next");
                        var exitBB = function.AppendBasicBlock("pred_exit");

                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(condBB);
                        var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "pred_i");
                        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "pred_has_more");
                        builder.BuildCondBr(hasMore, bodyBB, exitBB);

                        builder.PositionAtEnd(bodyBB);
                        var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                        var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                        var pass = builder.BuildCall2(predFnType, predTypedFn, new[] { envPtr, inVal }, "pred_pass");

                        if (isAny)
                        {
                            builder.BuildCondBr(pass, earlyBB, nextBB);
                        }
                        else
                        {
                            builder.BuildCondBr(pass, nextBB, earlyBB);
                        }

                        builder.PositionAtEnd(earlyBB);
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int1Type, isAny ? 1UL : 0UL), retAlloca);
                        builder.BuildBr(exitBB);

                        builder.PositionAtEnd(nextBB);
                        var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "pred_next_i");
                        builder.BuildStore(nextIdx, idxAlloca);
                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(exitBB);
                        return builder.BuildLoad2(context.Int1Type, retAlloca, "pred_final");
                    }

                    if (methodCall.MethodName == "find")
                    {
                        var closureVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var fnRaw = builder.BuildExtractValue(closureVal, 0, "find_fn_raw");
                        var envPtr = builder.BuildExtractValue(closureVal, 1, "find_env_ptr");

                        var findFnType = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType, dynElemType }, false);
                        var findTypedFn = builder.BuildBitCast(fnRaw, LLVMTypeRef.CreatePointer(findFnType, 0), "find_typed_fn");

                        var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, dynElemType }, false);
                        var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "find_opt_res");

                        var lenSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 1, "dyn_len_slot");
                        var curLen = builder.BuildLoad2(context.Int32Type, lenSlot, "cur_len");
                        var dataSlot = builder.BuildStructGEP2(dynArrStructType, dynStructPtr, 0, "dyn_data_slot");
                        var curData = builder.BuildLoad2(elemPtrType, dataSlot, "cur_data");

                        var idxAlloca = CreateEntryBlockAlloca(context, function, context.Int32Type, "find_idx");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), idxAlloca);

                        var condBB = function.AppendBasicBlock("find_cond");
                        var bodyBB = function.AppendBasicBlock("find_body");
                        var foundBB = function.AppendBasicBlock("find_found");
                        var nextBB = function.AppendBasicBlock("find_next");
                        var notFoundBB = function.AppendBasicBlock("find_not_found");
                        var exitBB = function.AppendBasicBlock("find_exit");

                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(condBB);
                        var idxVal = builder.BuildLoad2(context.Int32Type, idxAlloca, "find_i");
                        var hasMore = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, idxVal, curLen, "find_has_more");
                        builder.BuildCondBr(hasMore, bodyBB, notFoundBB);

                        builder.PositionAtEnd(bodyBB);
                        var inGEP = builder.BuildInBoundsGEP2(dynElemType, curData, new[] { idxVal }, "in_gep");
                        var inVal = builder.BuildLoad2(dynElemType, inGEP, "in_val");
                        var pass = builder.BuildCall2(findFnType, findTypedFn, new[] { envPtr, inVal }, "find_pass");
                        builder.BuildCondBr(pass, foundBB, nextBB);

                        builder.PositionAtEnd(foundBB);
                        var tagSlot1 = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag1");
                        var valSlot1 = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val1");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot1);
                        builder.BuildStore(inVal, valSlot1);
                        builder.BuildBr(exitBB);

                        builder.PositionAtEnd(nextBB);
                        var nextIdx = builder.BuildAdd(idxVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "find_next_i");
                        builder.BuildStore(nextIdx, idxAlloca);
                        builder.BuildBr(condBB);

                        builder.PositionAtEnd(notFoundBB);
                        var tagSlot0 = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag0");
                        var valSlot0 = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val0");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot0);
                        builder.BuildStore(LLVMValueRef.CreateConstNull(dynElemType), valSlot0);
                        builder.BuildBr(exitBB);

                        builder.PositionAtEnd(exitBB);
                        return builder.BuildLoad2(optStructType, optAlloca, "find_opt_val");
                    }
                }

                if (targetType.IsMap)
                {
                    targetType.TryGetMapInfo(out var mapKeySym, out var mapValSym);
                    var mapStructType = MapType(context, targetType.Name, ecs);

                    LLVMValueRef mapStructPtr;
                    if (methodCall.Target is IdentifierExpression mapTargetId && locals.TryGetValue(mapTargetId.Name, out var idPtr))
                    {
                        mapStructPtr = idPtr;
                    }
                    else
                    {
                        var tv = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var tempAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_tmp");
                        builder.BuildStore(tv, tempAlloca);
                        mapStructPtr = tempAlloca;
                    }

                    if (methodCall.MethodName is "len" or "length" or "count" or "capacity")
                    {
                        uint fieldIdx = methodCall.MethodName == "capacity" ? 2u : 1u;
                        var fieldSlot = builder.BuildStructGEP2(mapStructType, mapStructPtr, fieldIdx, $"map_{methodCall.MethodName}_slot");
                        return builder.BuildLoad2(context.Int32Type, fieldSlot, $"map_{methodCall.MethodName}_val");
                    }

                    if (methodCall.MethodName is "insert" or "put")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var vArg = CompileExpression(context, module, builder, function, methodCall.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var insertFunc = _mapEmitter!.GetOrCreateInsert(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var ifType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(insertFunc);
                        builder.BuildCall2(ifType, insertFunc, new[] { mapStructPtr, kArg, vArg }, "");
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }

                    if (methodCall.MethodName == "get")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);
                        return builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "map_get_val");
                    }

                    if (methodCall.MethodName is "find" or "get_opt")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var getFunc = _mapEmitter!.GetOrCreateGet(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                        var gfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(getFunc);

                        var hasIt = builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_has_key");
                        var valLlvmType = MapType(context, mapValSym.Name, ecs);
                        var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, valLlvmType }, false);
                        var retAlloca = CreateEntryBlockAlloca(context, function, optStructType, "opt_map_find_res");

                        var foundBB = function.AppendBasicBlock("map_find_found");
                        var notFoundBB = function.AppendBasicBlock("map_find_not_found");
                        var mergeBB = function.AppendBasicBlock("map_find_merge");

                        builder.BuildCondBr(hasIt, foundBB, notFoundBB);

                        builder.PositionAtEnd(foundBB);
                        var valRes = builder.BuildCall2(gfType, getFunc, new[] { mapStructPtr, kArg }, "found_val");
                        var tag1 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag1");
                        var val1 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val1");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tag1);
                        builder.BuildStore(valRes, val1);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(notFoundBB);
                        var tag0 = builder.BuildStructGEP2(optStructType, retAlloca, 0, "tag0");
                        var val0 = builder.BuildStructGEP2(optStructType, retAlloca, 1, "val0");
                        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tag0);
                        builder.BuildStore(LLVMValueRef.CreateConstNull(valLlvmType), val0);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(optStructType, retAlloca, "opt_map_val");
                    }

                    if (methodCall.MethodName is "contains" or "has")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var containsFunc = _mapEmitter!.GetOrCreateContains(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(containsFunc);
                        return builder.BuildCall2(cfType, containsFunc, new[] { mapStructPtr, kArg }, "map_contains_val");
                    }

                    if (methodCall.MethodName == "remove")
                    {
                        var kArg = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var removeFunc = _mapEmitter!.GetOrCreateRemove(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var rfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(removeFunc);
                        return builder.BuildCall2(rfType, removeFunc, new[] { mapStructPtr, kArg }, "map_remove_val");
                    }

                    if (methodCall.MethodName == "clear")
                    {
                        var clearFunc = _mapEmitter!.GetOrCreateClear(mapKeySym.Name, mapValSym.Name, (t) => MapType(context, t, ecs));
                        var cfType = (LLVMTypeRef)LlvmApi.GlobalGetValueType(clearFunc);
                        builder.BuildCall2(cfType, clearFunc, new[] { mapStructPtr }, "");
                        return LLVMValueRef.CreateConstInt(context.Int32Type, 0);
                    }
                }

                var targetVal = CompileExpression(context, module, builder, function, methodCall.Target, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);

                if (methodCall.MethodName == "to_string")
                {
                    var worldPtr = FindWorldPointer(context, builder, ecs, locals, varTypes);
                    return EmitToString(context, module, builder, targetVal, targetType, i8PtrType, ecs, worldPtr);
                }

                if (methodCall.MethodName is "len" or "length")
                {
                    var strlenFunc = module.GetNamedFunction("strlen");
                    var strlenType = LLVMTypeRef.CreateFunction(context.Int64Type, new[] { i8PtrType }, false);
                    var len64 = builder.BuildCall2(strlenType, strlenFunc, new[] { targetVal }, "slen64");
                    return builder.BuildTrunc(len64, context.Int32Type, "slen32");
                }

                if (targetType.IsOption)
                {
                    targetType.TryGetOptionInfo(out var optElemType);
                    var elemValType = MapType(context, optElemType.Name, ecs);
                    var optStructType = MapType(context, targetType.Name, ecs);

                    var tagVal = builder.BuildExtractValue(targetVal, 0, "opt_tag");

                    if (methodCall.MethodName == "is_some")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_some");
                    }

                    if (methodCall.MethodName == "is_none")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_none");
                    }

                    if (methodCall.MethodName == "unwrap")
                    {
                        var isSome = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "opt_is_some");
                        var okBB = function.AppendBasicBlock("opt_unwrap_ok");
                        var failBB = function.AppendBasicBlock("opt_unwrap_panic");

                        builder.BuildCondBr(isSome, okBB, failBB);

                        builder.PositionAtEnd(failBB);
                        var panicMsg = builder.BuildGlobalStringPtr("Panic: called Option.unwrap() on None", "panic_opt_unwrap");
                        builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                        var exitFunc = module.GetNamedFunction("exit");
                        var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                        if (exitFunc.Handle == IntPtr.Zero)
                        {
                            exitFunc = module.AddFunction("exit", exitType);
                        }
                        builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                        builder.BuildUnreachable();

                        builder.PositionAtEnd(okBB);
                        return builder.BuildExtractValue(targetVal, 1, "opt_unwrapped");
                    }

                    if (methodCall.MethodName == "unwrap_or")
                    {
                        var defVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var isSome = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "opt_is_some");
                        var okBB = function.AppendBasicBlock("opt_unwrapor_ok");
                        var defBB = function.AppendBasicBlock("opt_unwrapor_def");
                        var mergeBB = function.AppendBasicBlock("opt_unwrapor_merge");

                        var retAlloca = CreateEntryBlockAlloca(context, function, elemValType, "unwrap_or_ret");

                        builder.BuildCondBr(isSome, okBB, defBB);

                        builder.PositionAtEnd(okBB);
                        var someVal = builder.BuildExtractValue(targetVal, 1, "opt_unwrapped_val");
                        builder.BuildStore(someVal, retAlloca);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(defBB);
                        builder.BuildStore(defVal, retAlloca);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(elemValType, retAlloca, "unwrap_or_val");
                    }
                }

                if (targetType.IsResult)
                {
                    targetType.TryGetResultInfo(out var okType, out var errType);
                    var okLlvmType = MapType(context, okType.Name, ecs);
                    var errLlvmType = MapType(context, errType.Name, ecs);
                    var resStructType = MapType(context, targetType.Name, ecs);

                    var tagVal = builder.BuildExtractValue(targetVal, 0, "res_tag");

                    if (methodCall.MethodName == "is_ok")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "is_ok");
                    }

                    if (methodCall.MethodName == "is_err")
                    {
                        return builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "is_err");
                    }

                    if (methodCall.MethodName == "unwrap")
                    {
                        var isOk = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "res_is_ok");
                        var okBB = function.AppendBasicBlock("res_unwrap_ok");
                        var failBB = function.AppendBasicBlock("res_unwrap_panic");

                        builder.BuildCondBr(isOk, okBB, failBB);

                        builder.PositionAtEnd(failBB);
                        var panicMsg = builder.BuildGlobalStringPtr("Panic: called Result.unwrap() on Err", "panic_res_unwrap");
                        builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                        var exitFunc = module.GetNamedFunction("exit");
                        var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                        if (exitFunc.Handle == IntPtr.Zero)
                        {
                            exitFunc = module.AddFunction("exit", exitType);
                        }
                        builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                        builder.BuildUnreachable();

                        builder.PositionAtEnd(okBB);
                        return builder.BuildExtractValue(targetVal, 1, "res_unwrapped_ok");
                    }

                    if (methodCall.MethodName == "unwrap_err")
                    {
                        var isErr = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 1), "res_is_err");
                        var errBB = function.AppendBasicBlock("res_unwrap_err");
                        var failBB = function.AppendBasicBlock("res_unwrap_err_panic");

                        builder.BuildCondBr(isErr, errBB, failBB);

                        builder.PositionAtEnd(failBB);
                        var panicMsg = builder.BuildGlobalStringPtr("Panic: called Result.unwrap_err() on Ok", "panic_res_unwrap_err");
                        builder.BuildCall2(putsType, putsFunc, new[] { panicMsg }, "");
                        var exitFunc = module.GetNamedFunction("exit");
                        var exitType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                        if (exitFunc.Handle == IntPtr.Zero)
                        {
                            exitFunc = module.AddFunction("exit", exitType);
                        }
                        builder.BuildCall2(exitType, exitFunc, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "");
                        builder.BuildUnreachable();

                        builder.PositionAtEnd(errBB);
                        return builder.BuildExtractValue(targetVal, 2, "res_unwrapped_err");
                    }

                    if (methodCall.MethodName == "unwrap_or")
                    {
                        var defVal = CompileExpression(context, module, builder, function, methodCall.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var isOk = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, tagVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "res_is_ok");
                        var okBB = function.AppendBasicBlock("res_unwrapor_ok");
                        var defBB = function.AppendBasicBlock("res_unwrapor_def");
                        var mergeBB = function.AppendBasicBlock("res_unwrapor_merge");

                        var retAlloca = CreateEntryBlockAlloca(context, function, okLlvmType, "res_unwrap_or_ret");

                        builder.BuildCondBr(isOk, okBB, defBB);

                        builder.PositionAtEnd(okBB);
                        var okVal = builder.BuildExtractValue(targetVal, 1, "res_unwrapped_ok");
                        builder.BuildStore(okVal, retAlloca);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(defBB);
                        builder.BuildStore(defVal, retAlloca);
                        builder.BuildBr(mergeBB);

                        builder.PositionAtEnd(mergeBB);
                        return builder.BuildLoad2(okLlvmType, retAlloca, "res_unwrap_or_val");
                    }
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
                    var optStructType = LLVMTypeRef.CreateStruct(new[] { context.Int32Type, entLlvmType }, false);
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
                var lambdaTypeSym = _typeChecker.GetNodeType(lambda);
                lambdaTypeSym.TryGetFunctionInfo(out var lParams, out var lRet);

                string lambdaName = $"__lambda_{_lambdaCounter++}";
                var lambdaRetLlvmType = MapType(context, lRet?.Name ?? "void", ecs);

                var lambdaFnParamTypes = new List<LLVMTypeRef> { i8PtrType }; // env pointer
                foreach (var p in lambda.Parameters)
                {
                    var pTypeSym = p.TypeName != null ? TypeSymbol.FromName(p.TypeName) : TypeSymbol.I32;
                    lambdaFnParamTypes.Add(MapType(context, pTypeSym.Name, ecs));
                }

                var lambdaFnType = LLVMTypeRef.CreateFunction(lambdaRetLlvmType, lambdaFnParamTypes.ToArray(), false);
                var lambdaFunc = module.AddFunction(lambdaName, lambdaFnType);

                // Build Lambda Body
                var lambdaEntry = lambdaFunc.AppendBasicBlock("entry");
                var lambdaBuilder = context.CreateBuilder();
                lambdaBuilder.PositionAtEnd(lambdaEntry);

                var lambdaLocals = new Dictionary<string, LLVMValueRef>();
                var lambdaVarTypes = new Dictionary<string, string>();

                // 1. Environment unpacking (if captures exist)
                LLVMTypeRef envStructType = LLVMTypeRef.CreateStruct(Array.Empty<LLVMTypeRef>(), false);
                if (lambda.Captures.Count > 0)
                {
                    var envFieldTypes = new List<LLVMTypeRef>();
                    for (int i = 0; i < lambda.Captures.Count; i++)
                    {
                        var capName = lambda.Captures[i];
                        string capTypeName = varTypes.TryGetValue(capName, out var ct) ? ct : "i32";
                        var capLlvmType = MapType(context, capTypeName, ecs);
                        envFieldTypes.Add(LLVMTypeRef.CreatePointer(capLlvmType, 0));
                    }

                    envStructType = LLVMTypeRef.CreateStruct(envFieldTypes.ToArray(), false);
                    var envStructPtrType = LLVMTypeRef.CreatePointer(envStructType, 0);

                    var envParam = lambdaFunc.GetParam(0);
                    var typedEnv = lambdaBuilder.BuildBitCast(envParam, envStructPtrType, "env_typed");

                    for (int i = 0; i < lambda.Captures.Count; i++)
                    {
                        var capName = lambda.Captures[i];
                        string capTypeName = varTypes.TryGetValue(capName, out var ct) ? ct : "i32";
                        var gep = lambdaBuilder.BuildStructGEP2(envStructType, typedEnv, (uint)i, $"{capName}_slot");
                        var capPtr = lambdaBuilder.BuildLoad2(envFieldTypes[i], gep, $"{capName}_ptr");
                        lambdaLocals[capName] = capPtr;
                        lambdaVarTypes[capName] = capTypeName;
                    }
                }

                // 2. Setup Lambda parameters
                for (int i = 0; i < lambda.Parameters.Count; i++)
                {
                    var p = lambda.Parameters[i];
                    var pVal = lambdaFunc.GetParam((uint)(1 + i));
                    var pType = lambdaFnParamTypes[1 + i];
                    var pAlloca = CreateEntryBlockAlloca(context, lambdaFunc, pType, p.Name);
                    lambdaBuilder.BuildStore(pVal, pAlloca);
                    lambdaLocals[p.Name] = pAlloca;
                    lambdaVarTypes[p.Name] = p.TypeName ?? "i32";
                }

                // 3. Compile lambda body
                CompileBlock(context, module, lambdaBuilder, lambdaFunc, lambda.Body, lambdaLocals, lambdaVarTypes, ecs, putsType, putsFunc, printfType, printfFunc, isMain: false, hasWaitKey: false);
                if (lambdaBuilder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    if (lambdaRetLlvmType == context.VoidType)
                    {
                        lambdaBuilder.BuildRetVoid();
                    }
                    else
                    {
                        lambdaBuilder.BuildRet(LLVMValueRef.CreateConstNull(lambdaRetLlvmType));
                    }
                }
                lambdaBuilder.Dispose();

                // 4. In caller function: instantiate closure fat pointer { ptr fn, ptr env }
                var closureStructType = LLVMTypeRef.CreateStruct(new[] { i8PtrType, i8PtrType }, false);
                var closureAlloca = CreateEntryBlockAlloca(context, function, closureStructType, "closure_tmp");
                var fnSlot = builder.BuildStructGEP2(closureStructType, closureAlloca, 0, "fn_slot");
                var envSlot = builder.BuildStructGEP2(closureStructType, closureAlloca, 1, "env_slot");

                var rawFn = builder.BuildBitCast(lambdaFunc, i8PtrType, "raw_lambda_fn");
                builder.BuildStore(rawFn, fnSlot);

                if (lambda.Captures.Count > 0)
                {
                    var callerEnvAlloca = CreateEntryBlockAlloca(context, function, envStructType, "closure_env");
                    for (int i = 0; i < lambda.Captures.Count; i++)
                    {
                        var capName = lambda.Captures[i];
                        if (locals.TryGetValue(capName, out var capLocalPtr))
                        {
                            var gep = builder.BuildStructGEP2(envStructType, callerEnvAlloca, (uint)i, $"{capName}_env_gep");
                            builder.BuildStore(capLocalPtr, gep);
                        }
                    }
                    var rawEnv = builder.BuildBitCast(callerEnvAlloca, i8PtrType, "raw_env");
                    builder.BuildStore(rawEnv, envSlot);
                }
                else
                {
                    builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), envSlot);
                }

                return builder.BuildLoad2(closureStructType, closureAlloca, "closure_val");

            case IndirectCallExpression ind:
                var indTargetVal = CompileExpression(context, module, builder, function, ind.Callee, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                var indFnTypeSym = _typeChecker.GetNodeType(ind.Callee);
                indFnTypeSym.TryGetFunctionInfo(out var indParamTypes, out var indRetTypeSym);

                var indRetLlvm = MapType(context, indRetTypeSym?.Name ?? "void", ecs);
                var indParamLlvm = new List<LLVMTypeRef> { i8PtrType };
                if (indParamTypes != null)
                {
                    foreach (var pt in indParamTypes)
                    {
                        indParamLlvm.Add(MapType(context, pt.Name, ecs));
                    }
                }

                var indSignature = LLVMTypeRef.CreateFunction(indRetLlvm, indParamLlvm.ToArray(), false);
                var indSigPtr = LLVMTypeRef.CreatePointer(indSignature, 0);

                var indFnRaw = builder.BuildExtractValue(indTargetVal, 0, "ind_fn_raw");
                var indEnvPtr = builder.BuildExtractValue(indTargetVal, 1, "ind_env_ptr");
                var indTypedFn = builder.BuildBitCast(indFnRaw, indSigPtr, "ind_typed_fn");

                var indCallArgs = new List<LLVMValueRef> { indEnvPtr };
                foreach (var arg in ind.Arguments)
                {
                    indCallArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                }

                string indCallName = indRetLlvm == context.VoidType ? "" : "ind_call";
                return builder.BuildCall2(indSignature, indTypedFn, indCallArgs.ToArray(), indCallName);

            case CallExpression call:
                if (locals.TryGetValue(call.Callee, out var closureLocalPtr) && varTypes.TryGetValue(call.Callee, out var cTypeName) && (cTypeName.StartsWith("fn(") || cTypeName.StartsWith("closure(") || TypeSymbol.FromName(cTypeName).IsFunction))
                {
                    var cTypeSym = TypeSymbol.FromName(cTypeName);
                    cTypeSym.TryGetFunctionInfo(out var cParamTypes, out var cRetTypeSym);

                    var cRetLlvm = MapType(context, cRetTypeSym?.Name ?? "void", ecs);
                    var cParamLlvm = new List<LLVMTypeRef> { i8PtrType };
                    if (cParamTypes != null)
                    {
                        foreach (var pt in cParamTypes)
                        {
                            cParamLlvm.Add(MapType(context, pt.Name, ecs));
                        }
                    }

                    var cSignature = LLVMTypeRef.CreateFunction(cRetLlvm, cParamLlvm.ToArray(), false);
                    var cSigPtr = LLVMTypeRef.CreatePointer(cSignature, 0);

                    var closureVal = builder.BuildLoad2(LLVMTypeRef.CreateStruct(new[] { i8PtrType, i8PtrType }, false), closureLocalPtr, $"{call.Callee}_val");
                    var cFnRaw = builder.BuildExtractValue(closureVal, 0, "c_fn_raw");
                    var cEnvPtr = builder.BuildExtractValue(closureVal, 1, "c_env_ptr");
                    var cTypedFn = builder.BuildBitCast(cFnRaw, cSigPtr, "c_typed_fn");

                    var cCallArgs = new List<LLVMValueRef> { cEnvPtr };
                    foreach (var arg in call.Arguments)
                    {
                        cCallArgs.Add(CompileExpression(context, module, builder, function, arg, locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    }

                    string cCallName = cRetLlvm == context.VoidType ? "" : $"{call.Callee}_call";
                    return builder.BuildCall2(cSignature, cTypedFn, cCallArgs.ToArray(), cCallName);
                }

                if (call.Callee == "Some" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::Some")))
                {
                    var optType = _typeChecker.GetNodeType(call);
                    optType.TryGetOptionInfo(out var elemSym);
                    var elemLlvmType = MapType(context, elemSym.Name, ecs);
                    var optStructType = MapType(context, optType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "some_init");

                    var tagSlot = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot); // 1 = Some
                    var valSlot = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val_slot");
                    builder.BuildStore(argVal, valSlot);

                    return builder.BuildLoad2(optStructType, optAlloca, "some_val");
                }

                if (call.Callee == "None" || (call.Callee.StartsWith("Option<") && call.Callee.EndsWith("::None")))
                {
                    var optType = _typeChecker.GetNodeType(call);
                    optType.TryGetOptionInfo(out var elemSym);
                    var elemLlvmType = MapType(context, elemSym.Name, ecs);
                    var optStructType = MapType(context, optType.Name, ecs);

                    var optAlloca = CreateEntryBlockAlloca(context, function, optStructType, "none_init");

                    var tagSlot = builder.BuildStructGEP2(optStructType, optAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot); // 0 = None
                    var valSlot = builder.BuildStructGEP2(optStructType, optAlloca, 1, "val_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(elemLlvmType), valSlot);

                    return builder.BuildLoad2(optStructType, optAlloca, "none_val");
                }

                if (call.Callee == "Ok" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Ok")))
                {
                    var resType = _typeChecker.GetNodeType(call);
                    resType.TryGetResultInfo(out var okSym, out var errSym);
                    var okLlvmType = MapType(context, okSym.Name, ecs);
                    var errLlvmType = MapType(context, errSym.Name, ecs);
                    var resStructType = MapType(context, resType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "ok_init");

                    var tagSlot = builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 0), tagSlot); // 0 = Ok
                    var okSlot = builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_slot");
                    builder.BuildStore(argVal, okSlot);
                    var errSlot = builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(errLlvmType), errSlot);

                    return builder.BuildLoad2(resStructType, resAlloca, "ok_val");
                }

                if (call.Callee == "Err" || (call.Callee.StartsWith("Result<") && call.Callee.EndsWith("::Err")))
                {
                    var resType = _typeChecker.GetNodeType(call);
                    resType.TryGetResultInfo(out var okSym, out var errSym);
                    var okLlvmType = MapType(context, okSym.Name, ecs);
                    var errLlvmType = MapType(context, errSym.Name, ecs);
                    var resStructType = MapType(context, resType.Name, ecs);

                    var argVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var resAlloca = CreateEntryBlockAlloca(context, function, resStructType, "err_init");

                    var tagSlot = builder.BuildStructGEP2(resStructType, resAlloca, 0, "tag_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int32Type, 1), tagSlot); // 1 = Err
                    var okSlot = builder.BuildStructGEP2(resStructType, resAlloca, 1, "ok_slot");
                    builder.BuildStore(LLVMValueRef.CreateConstNull(okLlvmType), okSlot);
                    var errSlot = builder.BuildStructGEP2(resStructType, resAlloca, 2, "err_slot");
                    builder.BuildStore(argVal, errSlot);

                    return builder.BuildLoad2(resStructType, resAlloca, "err_val");
                }

                if (call.Callee.StartsWith("Vec<") || call.Callee.StartsWith("List<"))
                {
                    var dynTypeSym = TypeSymbol.FromName(call.Callee);
                    dynTypeSym.TryGetDynamicArrayElement(out var dynElemSym);
                    var dynElemType = MapType(context, dynElemSym.Name, ecs);
                    var dynArrStructType = MapType(context, dynTypeSym.Name, ecs);
                    var dynArrAlloca = CreateEntryBlockAlloca(context, function, dynArrStructType, "vec_init");

                    var dataZero = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(dynElemType, 0));
                    var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    var p0 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 0, "dyn_data_slot");
                    builder.BuildStore(dataZero, p0);
                    var p1 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 1, "dyn_len_slot");
                    builder.BuildStore(zeroInt, p1);
                    var p2 = builder.BuildStructGEP2(dynArrStructType, dynArrAlloca, 2, "dyn_cap_slot");
                    builder.BuildStore(zeroInt, p2);

                    return builder.BuildLoad2(dynArrStructType, dynArrAlloca, "vec_val");
                }

                if (call.Callee.StartsWith("Map<") || call.Callee.StartsWith("HashMap<"))
                {
                    var mapTypeSym = TypeSymbol.FromName(call.Callee);
                    mapTypeSym.TryGetMapInfo(out var kSym, out var vSym);
                    var mapStructType = MapType(context, mapTypeSym.Name, ecs);
                    var entryStructType = _mapEmitter!.GetEntryType(kSym.Name, vSym.Name, (t) => MapType(context, t, ecs));
                    var mapAlloca = CreateEntryBlockAlloca(context, function, mapStructType, "map_init");

                    var nullEntries = LLVMValueRef.CreateConstPointerNull(LLVMTypeRef.CreatePointer(entryStructType, 0));
                    var zeroInt = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    var p0 = builder.BuildStructGEP2(mapStructType, mapAlloca, 0, "map_entries_slot");
                    builder.BuildStore(nullEntries, p0);
                    var p1 = builder.BuildStructGEP2(mapStructType, mapAlloca, 1, "map_count_slot");
                    builder.BuildStore(zeroInt, p1);
                    var p2 = builder.BuildStructGEP2(mapStructType, mapAlloca, 2, "map_cap_slot");
                    builder.BuildStore(zeroInt, p2);

                    return builder.BuildLoad2(mapStructType, mapAlloca, "map_val");
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
                    if (_options.Target.IsWindows)
                    {
                        var getchFunc = module.GetNamedFunction("_getch");
                        var getchType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                        return builder.BuildCall2(getchType, getchFunc, Array.Empty<LLVMValueRef>(), "key_input");
                    }
                    else
                    {
                        var getcharFunc = module.GetNamedFunction("getchar");
                        var getcharType = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                        return builder.BuildCall2(getcharType, getcharFunc, Array.Empty<LLVMValueRef>(), "key_input");
                    }
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
                else if (call.Callee is "init_window" or "rl_init_window")
                {
                    var f = module.GetNamedFunction("InitWindow");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, i8PtrType }, false);
                    var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var h = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var title = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { w, h, title }, "");
                }
                else if (call.Callee is "is_window_ready" or "rl_is_window_ready")
                {
                    var f = module.GetNamedFunction("IsWindowReady");
                    var ft = LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
                    var raw = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "win_ready_raw");
                    return builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, raw, LLVMValueRef.CreateConstInt(context.Int8Type, 0), "win_ready");
                }
                else if (call.Callee is "window_should_close" or "rl_window_should_close")
                {
                    var f = module.GetNamedFunction("WindowShouldClose");
                    var ft = LLVMTypeRef.CreateFunction(context.Int8Type, Array.Empty<LLVMTypeRef>(), false);
                    var raw = builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "should_close_raw");
                    return builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, raw, LLVMValueRef.CreateConstInt(context.Int8Type, 0), "should_close");
                }
                else if (call.Callee is "render_profiler" or "render_debug_overlay" or "ecs::render_profiler")
                {
                    var f = module.GetNamedFunction("world_render_profiler");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { LLVMTypeRef.CreatePointer(ecs.GetWorldStructType(), 0) }, false);
                    var w = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { w }, "");
                }
                else if (call.Callee is "close_window" or "rl_close_window")
                {
                    var f = module.GetNamedFunction("CloseWindow");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "set_target_fps" or "rl_set_target_fps")
                {
                    var f = module.GetNamedFunction("SetTargetFPS");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                    var fps = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { fps }, "");
                }
                else if (call.Callee is "get_fps" or "rl_get_fps")
                {
                    var f = module.GetNamedFunction("GetFPS");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "fps");
                }
                else if (call.Callee is "get_frame_time" or "rl_get_frame_time")
                {
                    var f = module.GetNamedFunction("GetFrameTime");
                    var ft = LLVMTypeRef.CreateFunction(context.FloatType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "frame_time");
                }
                else if (call.Callee is "get_time" or "rl_get_time")
                {
                    var f = module.GetNamedFunction("GetTime");
                    var ft = LLVMTypeRef.CreateFunction(context.DoubleType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "cur_time");
                }
                else if (call.Callee is "begin_drawing" or "rl_begin_drawing")
                {
                    var f = module.GetNamedFunction("BeginDrawing");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                    });
                }
                else if (call.Callee is "end_drawing" or "rl_end_drawing")
                {
                    var f = module.GetNamedFunction("EndDrawing");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                    });
                }
                else if (call.Callee is "clear_background" or "rl_clear_background")
                {
                    var f = module.GetNamedFunction("ClearBackground");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type }, false);
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 3)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { colorVal }, "");
                    });
                }
                else if (call.Callee is "draw_rectangle" or "rl_draw_rectangle")
                {
                    var f = module.GetNamedFunction("DrawRectangle");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var x = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var w = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var h = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { x, y, w, h, colorVal }, "");
                    });
                }
                else if (call.Callee is "draw_circle" or "rl_draw_circle")
                {
                    var f = module.GetNamedFunction("DrawCircle");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.FloatType, context.Int32Type }, false);
                    var cx = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var cy = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var rad = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 6)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { cx, cy, rad, colorVal }, "");
                    });
                }
                else if (call.Callee is "draw_text" or "rl_draw_text")
                {
                    var f = module.GetNamedFunction("DrawText");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var txt = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var x = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var sz = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { txt, x, y, sz, colorVal }, "");
                    });
                }
                else if (call.Callee is "draw_line" or "rl_draw_line")
                {
                    var f = module.GetNamedFunction("DrawLine");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    var x1 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y1 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var x2 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var y2 = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef colorVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        colorVal = PackRgbaColor(context, builder, r, g, b);
                    }
                    else
                    {
                        colorVal = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { x1, y1, x2, y2, colorVal }, "");
                    });
                }
                else if (call.Callee is "is_key_down" or "rl_is_key_down")
                {
                    var f = module.GetNamedFunction("IsKeyDown");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_down");
                }
                else if (call.Callee is "is_key_pressed" or "rl_is_key_pressed")
                {
                    var f = module.GetNamedFunction("IsKeyPressed");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_pressed");
                }
                else if (call.Callee is "is_key_released" or "rl_is_key_released")
                {
                    var f = module.GetNamedFunction("IsKeyReleased");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_rel");
                }
                else if (call.Callee is "is_key_up" or "rl_is_key_up")
                {
                    var f = module.GetNamedFunction("IsKeyUp");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var k = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { k }, "key_up");
                }
                else if (call.Callee is "get_mouse_x" or "rl_get_mouse_x")
                {
                    var f = module.GetNamedFunction("GetMouseX");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_x");
                }
                else if (call.Callee is "get_mouse_y" or "rl_get_mouse_y")
                {
                    var f = module.GetNamedFunction("GetMouseY");
                    var ft = LLVMTypeRef.CreateFunction(context.Int32Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "mouse_y");
                }
                else if (call.Callee is "is_mouse_button_down" or "rl_is_mouse_button_down")
                {
                    var f = module.GetNamedFunction("IsMouseButtonDown");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { btn }, "btn_down");
                }
                else if (call.Callee is "is_mouse_button_pressed" or "rl_is_mouse_button_pressed")
                {
                    var f = module.GetNamedFunction("IsMouseButtonPressed");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { context.Int32Type }, false);
                    var btn = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    return builder.BuildCall2(ft, f, new[] { btn }, "btn_pressed");
                }
                else if (call.Callee == "rl_color")
                {
                    var r = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var g = CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var b = CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var a = call.Arguments.Count > 3 ? CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc) : null;
                    return PackRgbaColor(context, builder, r, g, b, a);
                }
                else if (call.Callee is "load_texture" or "rl_load_texture")
                {
                    var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mallocFunc = module.GetNamedFunction("malloc");
                    var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                    var texBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 32) }, "tex_buf");

                    var loadTexFunc = module.GetNamedFunction("LoadTexture");
                    var loadTexType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
                    builder.BuildCall2(loadTexType, loadTexFunc, new[] { texBuf, pathVal }, "");

                    return builder.BuildPtrToInt(texBuf, context.Int64Type, "tex_handle");
                }
                else if (call.Callee is "get_texture_width" or "rl_get_texture_width")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var wPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "tex_w_ptr");
                    return builder.BuildLoad2(context.Int32Type, wPtr, "tex_w");
                }
                else if (call.Callee is "get_texture_height" or "rl_get_texture_height")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var hPtr = builder.BuildInBoundsGEP2(context.Int32Type, texPtr, new[] { LLVMValueRef.CreateConstInt(context.Int32Type, 2) }, "tex_h_ptr");
                    return builder.BuildLoad2(context.Int32Type, hPtr, "tex_h");
                }
                else if (call.Callee is "draw_texture" or "rl_draw_texture")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var posX = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var posY = EnsureInt32(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    LLVMValueRef tintVal;
                    if (call.Arguments.Count >= 7)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var a = CompileExpression(context, module, builder, function, call.Arguments[6], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        tintVal = PackRgbaColor(context, builder, r, g, b, a);
                    }
                    else if (call.Arguments.Count == 4)
                    {
                        tintVal = CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    else
                    {
                        tintVal = LLVMValueRef.CreateConstInt(context.Int32Type, 0xFFFFFFFF);
                    }
                    var f = module.GetNamedFunction("DrawTexture");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.Int32Type, context.Int32Type, context.Int32Type }, false);
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { texPtr, posX, posY, tintVal }, "");
                    });
                }
                else if (call.Callee is "draw_texture_pro" or "rl_draw_texture_pro")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");

                    var rectType = LLVMTypeRef.CreateArray(context.FloatType, 4);
                    var srcRect = CreateEntryBlockAlloca(context, function, rectType, "src_rect");
                    var dstRect = CreateEntryBlockAlloca(context, function, rectType, "dst_rect");
                    var rectZeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    // source rect: args 1, 2, 3, 4
                    for (int i = 0; i < 4; i++)
                    {
                        var v = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1 + i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                        var slot = builder.BuildInBoundsGEP2(rectType, srcRect, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"src_{i}");
                        builder.BuildStore(v, slot);
                    }

                    // dest rect: args 5, 6, 7, 8
                    for (int i = 0; i < 4; i++)
                    {
                        var v = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[5 + i], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                        var slot = builder.BuildInBoundsGEP2(rectType, dstRect, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"dst_{i}");
                        builder.BuildStore(v, slot);
                    }

                    // origin: args 9, 10
                    var ox = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[9], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var oy = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[10], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var vec2Type = LLVMTypeRef.CreateArray(context.FloatType, 2);
                    var originAlloca = CreateEntryBlockAlloca(context, function, vec2Type, "origin_alloca");
                    builder.BuildStore(ox, builder.BuildInBoundsGEP2(vec2Type, originAlloca, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 0) }, "ox_slot"));
                    builder.BuildStore(oy, builder.BuildInBoundsGEP2(vec2Type, originAlloca, new[] { rectZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, 1) }, "oy_slot"));
                    var originI64 = builder.BuildLoad2(context.Int64Type, builder.BuildBitCast(originAlloca, LLVMTypeRef.CreatePointer(context.Int64Type, 0), "orig_i64_ptr"), "orig_i64");

                    // rotation: arg 11
                    var rot = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[11], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));

                    // tint: arg 12 (or 12..15 for r, g, b, a)
                    LLVMValueRef tintVal;
                    if (call.Arguments.Count >= 16)
                    {
                        var r = CompileExpression(context, module, builder, function, call.Arguments[12], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var g = CompileExpression(context, module, builder, function, call.Arguments[13], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var b = CompileExpression(context, module, builder, function, call.Arguments[14], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        var a = CompileExpression(context, module, builder, function, call.Arguments[15], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                        tintVal = PackRgbaColor(context, builder, r, g, b, a);
                    }
                    else if (call.Arguments.Count > 12)
                    {
                        tintVal = CompileExpression(context, module, builder, function, call.Arguments[12], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    }
                    else
                    {
                        tintVal = LLVMValueRef.CreateConstInt(context.Int32Type, 0xFFFFFFFF);
                    }

                    var f = module.GetNamedFunction("DrawTexturePro");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType, i8PtrType, context.Int64Type, context.FloatType, context.Int32Type }, false);
                    var srcPtr = builder.BuildBitCast(srcRect, i8PtrType, "src_ptr");
                    var dstPtr = builder.BuildBitCast(dstRect, i8PtrType, "dst_ptr");
                    return EmitGuardedRaylibVoidCall(context, module, builder, function, () => {
                        builder.BuildCall2(ft, f, new[] { texPtr, srcPtr, dstPtr, originI64, rot, tintVal }, "");
                    });
                }
                else if (call.Callee is "unload_texture" or "rl_unload_texture")
                {
                    var texHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var texPtr = builder.BuildIntToPtr(texHandle, i8PtrType, "tex_ptr");
                    var f = module.GetNamedFunction("UnloadTexture");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    builder.BuildCall2(ft, f, new[] { texPtr }, "");
                    var freeFunc = module.GetNamedFunction("free");
                    var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(freeType, freeFunc, new[] { texPtr }, "");
                }
                else if (call.Callee is "init_audio_device" or "rl_init_audio_device")
                {
                    var f = module.GetNamedFunction("InitAudioDevice");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "close_audio_device" or "rl_close_audio_device")
                {
                    var f = module.GetNamedFunction("CloseAudioDevice");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
                }
                else if (call.Callee is "is_audio_device_ready" or "rl_is_audio_device_ready")
                {
                    var f = module.GetNamedFunction("IsAudioDeviceReady");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "audio_ready");
                }
                else if (call.Callee is "load_sound" or "rl_load_sound")
                {
                    var pathVal = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var mallocFunc = module.GetNamedFunction("malloc");
                    var mallocType = LLVMTypeRef.CreateFunction(i8PtrType, new[] { context.Int64Type }, false);
                    var sndBuf = builder.BuildCall2(mallocType, mallocFunc, new[] { LLVMValueRef.CreateConstInt(context.Int64Type, 64) }, "snd_buf");

                    var loadSndFunc = module.GetNamedFunction("LoadSound");
                    var loadSndType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, i8PtrType }, false);
                    builder.BuildCall2(loadSndType, loadSndFunc, new[] { sndBuf, pathVal }, "");

                    return builder.BuildPtrToInt(sndBuf, context.Int64Type, "snd_handle");
                }
                else if (call.Callee is "play_sound" or "rl_play_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("PlaySound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "stop_sound" or "rl_stop_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("StopSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "pause_sound" or "rl_pause_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("PauseSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "resume_sound" or "rl_resume_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("ResumeSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                }
                else if (call.Callee is "is_sound_playing" or "rl_is_sound_playing")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("IsSoundPlaying");
                    var ft = LLVMTypeRef.CreateFunction(context.Int1Type, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr }, "snd_playing");
                }
                else if (call.Callee is "set_sound_volume" or "rl_set_sound_volume")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var vol = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var f = module.GetNamedFunction("SetSoundVolume");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType, context.FloatType }, false);
                    return builder.BuildCall2(ft, f, new[] { sndPtr, vol }, "");
                }
                else if (call.Callee is "unload_sound" or "rl_unload_sound")
                {
                    var sndHandle = CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc);
                    var sndPtr = builder.BuildIntToPtr(sndHandle, i8PtrType, "snd_ptr");
                    var f = module.GetNamedFunction("UnloadSound");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    builder.BuildCall2(ft, f, new[] { sndPtr }, "");
                    var freeFunc = module.GetNamedFunction("free");
                    var freeType = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(freeType, freeFunc, new[] { sndPtr }, "");
                }
                else if (call.Callee is "begin_mode_2d" or "rl_begin_mode_2d")
                {
                    // Camera2D: Vector2 offset (0..1), Vector2 target (2..3), float rotation (4), float zoom (5)
                    var camType = LLVMTypeRef.CreateArray(context.FloatType, 6);
                    var camAlloca = CreateEntryBlockAlloca(context, function, camType, "cam_alloca");
                    var camZeroIdx = LLVMValueRef.CreateConstInt(context.Int32Type, 0);

                    // offset.x, offset.y
                    var ox = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[0], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var oy = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[1], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    // target.x, target.y
                    var tx = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[2], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    var ty = EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[3], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc));
                    // rotation, zoom
                    var rot = call.Arguments.Count > 4 ? EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[4], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)) : LLVMValueRef.CreateConstReal(context.FloatType, 0.0);
                    var zoom = call.Arguments.Count > 5 ? EnsureFloat(context, builder, CompileExpression(context, module, builder, function, call.Arguments[5], locals, varTypes, ecs, putsType, putsFunc, printfType, printfFunc)) : LLVMValueRef.CreateConstReal(context.FloatType, 1.0);

                    var vals = new[] { ox, oy, tx, ty, rot, zoom };
                    for (int i = 0; i < 6; i++)
                    {
                        var slot = builder.BuildInBoundsGEP2(camType, camAlloca, new[] { camZeroIdx, LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)i) }, $"cam_{i}");
                        builder.BuildStore(vals[i], slot);
                    }

                    var camPtr = builder.BuildBitCast(camAlloca, i8PtrType, "cam_ptr");
                    var f = module.GetNamedFunction("BeginMode2D");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, new[] { i8PtrType }, false);
                    return builder.BuildCall2(ft, f, new[] { camPtr }, "");
                }
                else if (call.Callee is "end_mode_2d" or "rl_end_mode_2d")
                {
                    var f = module.GetNamedFunction("EndMode2D");
                    var ft = LLVMTypeRef.CreateFunction(context.VoidType, Array.Empty<LLVMTypeRef>(), false);
                    return builder.BuildCall2(ft, f, Array.Empty<LLVMValueRef>(), "");
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
                    else if (call.Callee.StartsWith("net_"))
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


}
