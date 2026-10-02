using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private ExpressionNode ParseCastExpression(ExpressionNode expr)
    {
        Advance(); // as
        var targetType = ParseTypeAnnotation();
        return new CastExpressionNode(expr, targetType, expr.Span);
    }
}
