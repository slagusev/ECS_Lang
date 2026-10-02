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
                _diagnostics.ReportError($"Cannot get resource '{resGet.ResourceName}' from non-World type '{targetType.Name}'.", resGet.Span);
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
