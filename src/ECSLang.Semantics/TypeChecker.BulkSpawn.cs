using System;
using System.Collections.Generic;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    public TypeSymbol CheckBulkSpawnExpression(BulkSpawnExpressionNode node)
    {
        var targetType = CheckExpression(node.Target);
        if (targetType != TypeSymbol.World && targetType != TypeSymbol.Commands && targetType != TypeSymbol.Unknown)
        {
            _diagnostics.ReportError($"Target of 'spawn_with' must be 'World' or 'Commands', but got '{targetType.Name}'.", node.Span);
        }

        var seenComponents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var compExpr in node.Components)
        {
            if (compExpr is CallExpression call)
            {
                string calleeName = call.Callee;
                if (!_components.TryGetValue(calleeName, out var compSym) &&
                    !_components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(calleeName), out compSym))
                {
                    _diagnostics.ReportError($"'{calleeName}' in 'spawn_with' is not a registered component.", call.Span);
                    continue;
                }

                if (!seenComponents.Add(compSym.Name))
                {
                    _diagnostics.ReportError($"Duplicate component '{compSym.Name}' in 'spawn_with'.", call.Span);
                }

                if (call.Arguments.Count != compSym.Fields.Count)
                {
                    _diagnostics.ReportError($"Component '{calleeName}' constructor expects {compSym.Fields.Count} arguments, but got {call.Arguments.Count}.", call.Span);
                }
                else
                {
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        var argType = CheckExpression(call.Arguments[i]);
                        var expectedType = compSym.Fields[i].Type;
                        if (!AreTypesCompatible(expectedType, argType))
                        {
                            _diagnostics.ReportError($"Argument {i + 1} of '{calleeName}' expects type '{expectedType.Name}', but got '{argType.Name}'.", call.Arguments[i].Span);
                        }
                    }
                }

                _nodeTypes[call] = TypeSymbol.FromName(compSym.Name);
            }
            else
            {
                var exprType = CheckExpression(compExpr);
                if (!_components.ContainsKey(exprType.Name) &&
                    !_components.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(exprType.Name)))
                {
                    _diagnostics.ReportError($"Argument to 'spawn_with' must be a component constructor call or component instance, but got '{exprType.Name}'.", compExpr.Span);
                }
                else
                {
                    if (!seenComponents.Add(exprType.Name))
                    {
                        _diagnostics.ReportError($"Duplicate component '{exprType.Name}' in 'spawn_with'.", compExpr.Span);
                    }
                }
            }
        }

        _nodeTypes[node] = TypeSymbol.Entity;
        return TypeSymbol.Entity;
    }
}
