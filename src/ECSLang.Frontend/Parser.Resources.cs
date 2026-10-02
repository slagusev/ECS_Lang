using System.Collections.Generic;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private ExpressionNode ParseResourceGetOrMethodCall(
        ExpressionNode target,
        string memberName,
        IReadOnlyList<ExpressionNode> args,
        SourceSpan span)
    {
        if (memberName.StartsWith("get_") && args.Count == 0)
        {
            string resName = memberName.Substring(4);
            if (!string.IsNullOrEmpty(resName))
            {
                return new ResourceGetExpressionNode(target, resName, span);
            }
        }

        return new MethodCallExpression(target, memberName, args, span);
    }
}
