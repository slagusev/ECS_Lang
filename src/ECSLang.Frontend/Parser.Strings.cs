using System;
using System.Collections.Generic;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private static readonly HashSet<string> StringMethodNames = new(StringComparer.Ordinal)
    {
        "len",
        "length",
        "contains",
        "starts_with",
        "ends_with",
        "index_of",
        "substring"
    };

    public static bool IsStringMethod(string name) => StringMethodNames.Contains(name);
}
