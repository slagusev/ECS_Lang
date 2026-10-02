using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private ExpressionNode ParseErrorPropagationExpression(ExpressionNode expr)
    {
        var questionTok = Advance(); // consume '?'
        var span = new SourceSpan(expr.Span.FilePath, expr.Span.Line, expr.Span.Column, expr.Span.Length + questionTok.Span.Length);
        return new ErrorPropagationExpressionNode(expr, span);
    }
}
