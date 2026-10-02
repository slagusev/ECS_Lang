using System;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckTimeCall(CallExpression call, out TypeSymbol returnType)
    {
        switch (call.Callee)
        {
            case "stopwatch_start":
            case "sys_time_ticks":
                if (call.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Function 'stopwatch_start' expects 0 arguments.", call.Span);
                }
                returnType = TypeSymbol.I64;
                return true;

            case "stopwatch_ms":
                if (call.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Function 'stopwatch_ms' expects exactly 1 argument (start_ticks: i64).", call.Span);
                }
                else
                {
                    var argType = CheckExpression(call.Arguments[0]);
                    if (argType != TypeSymbol.I64 && argType != TypeSymbol.I32)
                    {
                        _diagnostics.ReportError($"Function 'stopwatch_ms' expects 'i64' start ticks, but got '{argType.Name}'.", call.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.F32;
                return true;

            default:
                returnType = TypeSymbol.Unknown;
                return false;
        }
    }
}
