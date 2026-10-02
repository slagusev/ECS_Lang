using System;
using System.Collections.Generic;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private static readonly HashSet<string> FileIoBuiltinNames = new(StringComparer.Ordinal)
    {
        "file_read_text",
        "file_write_text",
        "file_append_text",
        "file_exists"
    };

    public static bool IsFileIoBuiltin(string name) => FileIoBuiltinNames.Contains(name);
}
