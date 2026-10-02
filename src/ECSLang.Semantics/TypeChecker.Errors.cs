using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private TypeSymbol CheckErrorPropagationExpression(ErrorPropagationExpressionNode tryNode)
    {
        var exprType = CheckExpression(tryNode.Expr);

        // 1. Must be Result<T, E> or Option<T>
        if (exprType.TryGetResultInfo(out var okType, out var errType))
        {
            bool isMain = _currentFunctionName == "main";
            if (!isMain)
            {
                if (_currentExpectedReturnType == null || !_currentExpectedReturnType.TryGetResultInfo(out _, out var fnErrType))
                {
                    _diagnostics.ReportError(
                        $"Operator '?' can only be used in a function that returns 'Result' (found: '{_currentExpectedReturnType?.Name ?? "void"}').",
                        tryNode.Span);
                    _nodeTypes[tryNode] = okType;
                    return okType;
                }

                if (!AreTypesCompatible(fnErrType, errType))
                {
                    _diagnostics.ReportError(
                        $"Mismatched error types in operator '?': expression error type '{errType.Name}' does not match enclosing function error type '{fnErrType.Name}'.",
                        tryNode.Span);
                }
            }

            _nodeTypes[tryNode] = okType;
            return okType;
        }
        else if (exprType.TryGetOptionInfo(out var valueType))
        {
            bool isMain = _currentFunctionName == "main";
            if (!isMain)
            {
                if (_currentExpectedReturnType == null || !_currentExpectedReturnType.IsOption)
                {
                    _diagnostics.ReportError(
                        $"Operator '?' on 'Option' can only be used in a function that returns 'Option' (found: '{_currentExpectedReturnType?.Name ?? "void"}').",
                        tryNode.Span);
                    _nodeTypes[tryNode] = valueType;
                    return valueType;
                }
            }

            _nodeTypes[tryNode] = valueType;
            return valueType;
        }
        else
        {
            _diagnostics.ReportError(
                $"Operator '?' can only be applied to 'Result<T, E>' or 'Option<T>', but found '{exprType.Name}'.",
                tryNode.Span);
            _nodeTypes[tryNode] = TypeSymbol.Unknown;
            return TypeSymbol.Unknown;
        }
    }
}
