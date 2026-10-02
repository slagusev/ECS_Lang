using System;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckStringMethod(MethodCallExpression methodCall, TypeSymbol targetType, out TypeSymbol returnType)
    {
        if (targetType != TypeSymbol.String)
        {
            returnType = TypeSymbol.Unknown;
            return false;
        }

        switch (methodCall.MethodName)
        {
            case "len" or "length":
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                returnType = TypeSymbol.I32;
                return true;

            case "contains":
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'contains' on string expects exactly 1 argument (sub: string).", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Method 'contains' expects 'string' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "starts_with":
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'starts_with' on string expects exactly 1 argument (prefix: string).", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Method 'starts_with' expects 'string' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "ends_with":
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'ends_with' on string expects exactly 1 argument (suffix: string).", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Method 'ends_with' expects 'string' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "index_of":
                if (methodCall.Arguments.Count != 1 && methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Method 'index_of' on string expects 1 or 2 arguments: (sub: string) or (sub: string, start_index: i32).", methodCall.Span);
                }
                else
                {
                    var argType = CheckExpression(methodCall.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Method 'index_of' expects 'string' argument, but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                    if (methodCall.Arguments.Count == 2)
                    {
                        var startType = CheckExpression(methodCall.Arguments[1]);
                        if (!startType.IsInteger)
                        {
                            _diagnostics.ReportError($"Method 'index_of' expects integer 'start_index', but got '{startType.Name}'.", methodCall.Arguments[1].Span);
                        }
                    }
                }
                returnType = TypeSymbol.I32;
                return true;

            case "view_substring":
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Method 'view_substring' on string expects exactly 2 arguments (start: i32, length: i32).", methodCall.Span);
                }
                else
                {
                    var startType = CheckExpression(methodCall.Arguments[0]);
                    if (!startType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method 'view_substring' expects integer 'start', but got '{startType.Name}'.", methodCall.Arguments[0].Span);
                    }
                    var lenType = CheckExpression(methodCall.Arguments[1]);
                    if (!lenType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method 'view_substring' expects integer 'length', but got '{lenType.Name}'.", methodCall.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.StrView;
                return true;

            case "substring":
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Method 'substring' on string expects exactly 2 arguments (start: i32, length: i32).", methodCall.Span);
                }
                else
                {
                    var startType = CheckExpression(methodCall.Arguments[0]);
                    if (!startType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method 'substring' expects integer 'start', but got '{startType.Name}'.", methodCall.Arguments[0].Span);
                    }
                    var lenType = CheckExpression(methodCall.Arguments[1]);
                    if (!lenType.IsInteger)
                    {
                        _diagnostics.ReportError($"Method 'substring' expects integer 'length', but got '{lenType.Name}'.", methodCall.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.String;
                return true;

            default:
                returnType = TypeSymbol.Unknown;
                return false;
        }
    }
}
