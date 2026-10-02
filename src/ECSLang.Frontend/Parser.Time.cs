using System;
using System.Collections.Generic;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private static readonly HashSet<string> TimeBuiltinNames = new(StringComparer.Ordinal)
    {
        "stopwatch_start",
        "stopwatch_ms"
    };

    public static bool IsTimeBuiltin(string name) => TimeBuiltinNames.Contains(name);
}
