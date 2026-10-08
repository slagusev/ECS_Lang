using System;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckTaggedUnionConstructor(CallExpression call, out TypeSymbol? result)
    {
        if (call.Callee.StartsWith("Option<") && call.Callee.Contains("::"))
        {
            var optTypeName = call.Callee.Substring(0, call.Callee.IndexOf("::", StringComparison.Ordinal));
            result = TypeSymbol.FromName(optTypeName);
            return true;
        }

        if (call.Callee.StartsWith("Result<") && call.Callee.Contains("::"))
        {
            var resTypeName = call.Callee.Substring(0, call.Callee.IndexOf("::", StringComparison.Ordinal));
            result = TypeSymbol.FromName(resTypeName);
            return true;
        }

        if (call.Callee == "Some")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Some' expects 1 argument.", call.Span);
                result = TypeSymbol.CreateOption(TypeSymbol.Unknown);
                return true;
            }
            var argType = GetNodeType(call.Arguments[0]);
            result = TypeSymbol.CreateOption(argType);
            return true;
        }

        if (call.Callee == "None")
        {
            result = new TypeSymbol("None", IsPrimitive: false);
            return true;
        }

        if (call.Callee is "Ok" or "Result::Ok")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Ok' expects 1 argument.", call.Span);
                result = TypeSymbol.CreateResult(TypeSymbol.Unknown, TypeSymbol.String);
                return true;
            }
            var argType = GetNodeType(call.Arguments[0]);
            result = TypeSymbol.CreateResult(argType, TypeSymbol.String);
            return true;
        }

        if (call.Callee is "Err" or "Result::Err")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Err' expects 1 argument.", call.Span);
                result = TypeSymbol.CreateResult(TypeSymbol.Unknown, TypeSymbol.String);
                return true;
            }
            var argType = GetNodeType(call.Arguments[0]);
            result = TypeSymbol.CreateResult(TypeSymbol.Unknown, argType);
            return true;
        }

        result = null;
        return false;
    }

    private bool TryCheckTaggedUnionMethodCall(MethodCallExpression methodCall, TypeSymbol targetType, out TypeSymbol? result)
    {
        if (targetType.IsOption)
        {
            targetType.TryGetOptionInfo(out var optElem);
            if (methodCall.MethodName is "is_some" or "is_none")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                result = TypeSymbol.Bool;
                return true;
            }

            if (methodCall.MethodName == "unwrap")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = optElem;
                result = optElem;
                return true;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'unwrap_or' expects 1 argument (default value).", methodCall.Span);
                }
                else
                {
                    var defType = GetNodeType(methodCall.Arguments[0]);
                    if (optElem != TypeSymbol.Unknown && defType != TypeSymbol.Unknown && !AreTypesCompatible(optElem, defType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'unwrap_or' expects type '{optElem.Name}', but got '{defType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = optElem;
                result = optElem;
                return true;
            }
        }

        if (targetType.IsResult)
        {
            targetType.TryGetResultInfo(out var okType, out var errType);
            if (methodCall.MethodName is "is_ok" or "is_err")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                result = TypeSymbol.Bool;
                return true;
            }

            if (methodCall.MethodName == "unwrap")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = okType;
                result = okType;
                return true;
            }

            if (methodCall.MethodName == "unwrap_err")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap_err' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = errType;
                result = errType;
                return true;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'unwrap_or' expects 1 argument (default value).", methodCall.Span);
                }
                else
                {
                    var defType = GetNodeType(methodCall.Arguments[0]);
                    if (okType != TypeSymbol.Unknown && defType != TypeSymbol.Unknown && !AreTypesCompatible(okType, defType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'unwrap_or' expects type '{okType.Name}', but got '{defType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = okType;
                result = okType;
                return true;
            }
        }

        result = null;
        return false;
    }
}
