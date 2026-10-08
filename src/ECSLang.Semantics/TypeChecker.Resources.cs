using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private TypeSymbol CheckResourceGetExpression(ResourceGetExpressionNode resGet)
    {
        if (resGet.Target != null)
        {
            var targetType = CheckExpression(resGet.Target);
            if (targetType != TypeSymbol.World && targetType.Name != "World" && targetType != TypeSymbol.Unknown)
            {
                // Target is not a World instance: fallback to regular method call (e.g. pair.get_first())
                var fallbackCall = new MethodCallExpression(resGet.Target, $"get_{resGet.ResourceName}", System.Array.Empty<ExpressionNode>(), resGet.Span);
                var callType = CheckMethodCall(fallbackCall);
                _nodeTypes[resGet] = callType;
                return callType;
            }
        }

        if (!_resources.TryGetValue(resGet.ResourceName, out var resSym))
        {
            _diagnostics.ReportError($"Resource '{resGet.ResourceName}' is not declared in the project.", resGet.Span);
            return TypeSymbol.Unknown;
        }

        var resType = TypeSymbol.FromName(resGet.ResourceName);
        _nodeTypes[resGet] = resType;
        return resType;
    }
}
