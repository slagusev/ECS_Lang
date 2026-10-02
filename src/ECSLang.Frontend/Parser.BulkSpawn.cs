using System.Collections.Generic;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private ExpressionNode ParseBulkSpawnOrMethodCall(
        ExpressionNode target,
        string memberName,
        IReadOnlyList<ExpressionNode> args,
        SourceSpan span)
    {
        if (memberName == "spawn_with")
        {
            return new BulkSpawnExpressionNode(target, args, span);
        }

        return ParseResourceGetOrMethodCall(target, memberName, args, span);
    }
}
