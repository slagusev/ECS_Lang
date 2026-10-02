using System;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckStrViewMethod(MethodCallExpression methodCall, TypeSymbol targetType, out TypeSymbol returnType)
    {
        if (targetType != TypeSymbol.StrView)
        {
            returnType = TypeSymbol.Unknown;
            return false;
        }

        switch (methodCall.MethodName)
        {
            case "len" or "length":
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' on str_view expects 0 arguments.", methodCall.Span);
                }
                returnType = TypeSymbol.I32;
                return true;

            case "view_substring" or "substring":
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' on str_view expects exactly 2 arguments (start: i32, length: i32).", methodCall.Span);
                }
                else
                {
                    var startType = CheckExpression(methodCall.Arguments[0]);
                    if (!startType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects integer 'start', but got '{startType.Name}'.", methodCall.Arguments[0].Span);
                    }
                    var lenType = CheckExpression(methodCall.Arguments[1]);
                    if (!lenType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects integer 'length', but got '{lenType.Name}'.", methodCall.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.StrView;
                return true;

            case "contains":
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'contains' on str_view expects exactly 1 argument.", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String && argType != TypeSymbol.StrView && argType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Method 'contains' expects 'string' or 'str_view' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "starts_with":
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'starts_with' on str_view expects exactly 1 argument.", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String && argType != TypeSymbol.StrView && argType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Method 'starts_with' expects 'string' or 'str_view' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "to_string":
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'to_string' on str_view expects 0 arguments.", methodCall.Span);
                }
                returnType = TypeSymbol.String;
                return true;

            default:
                returnType = TypeSymbol.Unknown;
                return false;
        }
    }
}
