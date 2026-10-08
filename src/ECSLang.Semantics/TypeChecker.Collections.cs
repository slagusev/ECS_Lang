using System;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private TypeSymbol CheckArrayLiteral(ArrayLiteralExpression arrLit)
    {
        if (arrLit.Elements.Count == 0)
        {
            return TypeSymbol.CreateArray(TypeSymbol.Unknown, 0);
        }

        var firstType = CheckExpression(arrLit.Elements[0]);
        if (firstType.ContainsCapturingClosure())
        {
            _diagnostics.ReportError("Capturing closure cannot be stored in an array. Closures with stack-allocated captures cannot escape their enclosing frame.", arrLit.Elements[0].Span);
        }
        for (int i = 1; i < arrLit.Elements.Count; i++)
        {
            var elemType = CheckExpression(arrLit.Elements[i]);
            if (elemType.ContainsCapturingClosure())
            {
                _diagnostics.ReportError("Capturing closure cannot be stored in an array. Closures with stack-allocated captures cannot escape their enclosing frame.", arrLit.Elements[i].Span);
            }
            if (firstType != TypeSymbol.Unknown && elemType != TypeSymbol.Unknown && firstType != elemType)
            {
                _diagnostics.ReportError($"Array element at index {i} has type '{elemType.Name}', expected '{firstType.Name}'.", arrLit.Elements[i].Span);
            }
        }

        return TypeSymbol.CreateArray(firstType, arrLit.Elements.Count);
    }

    private TypeSymbol CheckIndexExpression(IndexExpression idxExpr)
    {
        var targetType = CheckExpression(idxExpr.Target);
        var indexType = CheckExpression(idxExpr.Index);

        if (targetType.TryGetMapInfo(out var mapKeyType, out var mapValType))
        {
            if (!AreTypesCompatible(mapKeyType, indexType))
            {
                _diagnostics.ReportError($"Map key of type '{targetType.Name}' expects '{mapKeyType.Name}', but got '{indexType.Name}'.", idxExpr.Index.Span);
            }
            return mapValType;
        }

        if (!indexType.IsInteger)
        {
            _diagnostics.ReportError("Array index must be an integer.", idxExpr.Index.Span);
        }

        if (targetType.TryGetArrayInfo(out var elemType, out _))
        {
            return elemType;
        }

        if (targetType.TryGetDynamicArrayElement(out var dynElem))
        {
            return dynElem;
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' is not indexable.", idxExpr.Span);
        return TypeSymbol.Unknown;
    }

    private bool TryCheckCollectionConstructor(CallExpression call, out TypeSymbol? result)
    {
        if (call.Callee.StartsWith("Vec<") || call.Callee.StartsWith("List<") ||
            call.Callee.StartsWith("Map<") || call.Callee.StartsWith("HashMap<"))
        {
            var collType = TypeSymbol.FromName(call.Callee);
            if (collType.ContainsCapturingClosure())
            {
                _diagnostics.ReportError($"Capturing closure cannot be used as type argument in collection '{call.Callee}'. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Span);
            }
            result = collType;
            return true;
        }

        result = null;
        return false;
    }

    private bool TryCheckCollectionMethodCall(MethodCallExpression methodCall, TypeSymbol targetType, out TypeSymbol? result)
    {
        if (targetType.IsDynamicArray)
        {
            targetType.TryGetDynamicArrayElement(out var elemType);
            if (methodCall.MethodName == "push")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'push' expects exactly 1 argument.", methodCall.Span);
                }
                else
                {
                    var argType = GetNodeType(methodCall.Arguments[0]);
                    if (argType.ContainsCapturingClosure())
                    {
                        _diagnostics.ReportError("Capturing closure cannot be stored in a collection. Closures with stack-allocated captures cannot escape their enclosing frame.", methodCall.Arguments[0].Span);
                    }
                    else if (elemType != TypeSymbol.Unknown && argType != TypeSymbol.Unknown && !AreTypesCompatible(elemType, argType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'push' expects type '{elemType.Name}', but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                result = TypeSymbol.Void;
                return true;
            }

            if (methodCall.MethodName == "pop")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'pop' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = elemType;
                result = elemType;
                return true;
            }

            if (methodCall.MethodName == "get")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'get' expects 1 argument (index i32).", methodCall.Span);
                }
                else
                {
                    var idxType = GetNodeType(methodCall.Arguments[0]);
                    if (!idxType.IsInteger && idxType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of 'get' expects integer index, but got '{idxType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                var optRet = TypeSymbol.CreateOption(elemType);
                _nodeTypes[methodCall] = optRet;
                result = optRet;
                return true;
            }

            if (methodCall.MethodName is "len" or "length" or "capacity")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.I32;
                result = TypeSymbol.I32;
                return true;
            }

            if (methodCall.MethodName == "clear")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'clear' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                result = TypeSymbol.Void;
                return true;
            }

            if (methodCall.MethodName == "for_each")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'for_each' expects 1 argument (closure/function).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out _))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Closure for 'for_each' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                result = TypeSymbol.Void;
                return true;
            }

            if (methodCall.MethodName == "map")
            {
                TypeSymbol mappedElem = TypeSymbol.Unknown;
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'map' expects 1 argument (closure/function).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out var rType))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Closure for 'map' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                        if (rType.ContainsCapturingClosure())
                        {
                            _diagnostics.ReportError("Capturing closure cannot be used as an element of a collection returned by 'map'. Closures with stack-allocated captures cannot escape their enclosing frame.", methodCall.Arguments[0].Span);
                        }
                        mappedElem = rType;
                    }
                }
                var resType = TypeSymbol.CreateDynamicArray(mappedElem != TypeSymbol.Unknown ? mappedElem : elemType);
                _nodeTypes[methodCall] = resType;
                result = resType;
                return true;
            }

            if (methodCall.MethodName == "filter")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'filter' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out _))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for 'filter' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = targetType;
                result = targetType;
                return true;
            }

            if (methodCall.MethodName is "any" or "all")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out _))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for '{methodCall.MethodName}' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                result = TypeSymbol.Bool;
                return true;
            }

            if (methodCall.MethodName == "find")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'find' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out _))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for 'find' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                var optRet = TypeSymbol.CreateOption(elemType);
                _nodeTypes[methodCall] = optRet;
                result = optRet;
                return true;
            }
        }

        if (targetType.IsMap)
        {
            targetType.TryGetMapInfo(out var keyType, out var valType);

            if (methodCall.MethodName is "insert" or "put")
            {
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 2 arguments (key, value).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    var vArg = GetNodeType(methodCall.Arguments[1]);
                    if (kArg.ContainsCapturingClosure() || vArg.ContainsCapturingClosure())
                    {
                        _diagnostics.ReportError("Capturing closure cannot be stored in a map. Closures with stack-allocated captures cannot escape their enclosing frame.", methodCall.Span);
                    }
                    else
                    {
                        if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                        {
                            _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                        }
                        if (valType != TypeSymbol.Unknown && vArg != TypeSymbol.Unknown && !AreTypesCompatible(valType, vArg))
                        {
                            _diagnostics.ReportError($"Argument 2 of '{methodCall.MethodName}' expects value type '{valType.Name}', but got '{vArg.Name}'.", methodCall.Arguments[1].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                result = TypeSymbol.Void;
                return true;
            }

            if (methodCall.MethodName == "get")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'get' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'get' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = valType;
                result = valType;
                return true;
            }

            if (methodCall.MethodName is "find" or "get_opt")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                var optRet = TypeSymbol.CreateOption(valType);
                _nodeTypes[methodCall] = optRet;
                result = optRet;
                return true;
            }

            if (methodCall.MethodName is "contains" or "has")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                result = TypeSymbol.Bool;
                return true;
            }

            if (methodCall.MethodName == "remove")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'remove' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'remove' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                result = TypeSymbol.Bool;
                return true;
            }

            if (methodCall.MethodName is "len" or "length" or "count" or "capacity")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.I32;
                result = TypeSymbol.I32;
                return true;
            }

            if (methodCall.MethodName == "clear")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'clear' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                result = TypeSymbol.Void;
                return true;
            }
        }

        result = null;
        return false;
    }
}
