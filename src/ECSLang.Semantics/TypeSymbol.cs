namespace ECSLang.Semantics;

public sealed record TypeSymbol(string Name, bool IsPrimitive = true)
{
    public static readonly TypeSymbol I32 = new("i32");
    public static readonly TypeSymbol I64 = new("i64");
    public static readonly TypeSymbol U32 = new("u32");
    public static readonly TypeSymbol U64 = new("u64");
    public static readonly TypeSymbol F32 = new("f32");
    public static readonly TypeSymbol F64 = new("f64");
    public static readonly TypeSymbol Bool = new("bool");
    public static readonly TypeSymbol String = new("string");
    public static readonly TypeSymbol Void = new("void");
    public static readonly TypeSymbol World = new("World", IsPrimitive: false);
    public static readonly TypeSymbol Commands = new("Commands", IsPrimitive: false);
    public static readonly TypeSymbol Entity = new("Entity", IsPrimitive: true);
    public static readonly TypeSymbol Unknown = new("<unknown>");

    public bool IsNumeric => this == I32 || this == I64 || this == U32 || this == U64 || this == F32 || this == F64;
    public bool IsFloatingPoint => this == F32 || this == F64;
    public bool IsInteger => this == I32 || this == I64 || this == U32 || this == U64 || this == Entity;
    public bool IsArray => Name.StartsWith("[") && Name.EndsWith("]");
    public bool IsFixedArray => IsArray && Name.Contains(";");
    public bool IsDynamicArray => IsArray && !Name.Contains(";");
    public bool IsMap => Name.StartsWith("Map<") && Name.EndsWith(">");
    public bool IsOption => Name.StartsWith("Option<") && Name.EndsWith(">");
    public bool IsResult => Name.StartsWith("Result<") && Name.EndsWith(">");
    public bool IsFunction => Name.StartsWith("fn(") || Name.StartsWith("closure(");
    public bool IsGenericInstantiation => Name.Contains("<") && Name.EndsWith(">");

    public bool TryGetGenericInfo(out string baseName, out IReadOnlyList<TypeSymbol> typeArguments)
    {
        if (IsGenericInstantiation)
        {
            int openBracket = Name.IndexOf('<');
            baseName = Name.Substring(0, openBracket).Trim();
            var inner = Name.Substring(openBracket + 1, Name.Length - openBracket - 2);
            var args = new List<TypeSymbol>();
            int depth = 0;
            int lastStart = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<' || inner[i] == '[') depth++;
                else if (inner[i] == '>' || inner[i] == ']') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    args.Add(FromName(inner.Substring(lastStart, i - lastStart).Trim()));
                    lastStart = i + 1;
                }
            }
            if (lastStart < inner.Length)
            {
                args.Add(FromName(inner.Substring(lastStart).Trim()));
            }
            typeArguments = args;
            return true;
        }
        baseName = Name;
        typeArguments = Array.Empty<TypeSymbol>();
        return false;
    }

    public static string ToMonomorphizedIdentifier(string typeName)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in typeName)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
            else if (ch == '<' || ch == '>' || ch == ',' || ch == '[' || ch == ']' || ch == ' ')
                sb.Append('_');
        }
        var result = sb.ToString();
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        return result.Trim('_');
    }

    public bool TryGetOptionInfo(out TypeSymbol valueType)
    {
        if (IsOption)
        {
            var inner = Name.Substring(7, Name.Length - 8).Trim();
            valueType = FromName(inner);
            return true;
        }
        valueType = Unknown;
        return false;
    }

    public bool TryGetResultInfo(out TypeSymbol okType, out TypeSymbol errType)
    {
        if (IsResult)
        {
            var inner = Name.Substring(7, Name.Length - 8);
            int depth = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<' || inner[i] == '[') depth++;
                else if (inner[i] == '>' || inner[i] == ']') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    okType = FromName(inner.Substring(0, i).Trim());
                    errType = FromName(inner.Substring(i + 1).Trim());
                    return true;
                }
            }
        }
        okType = Unknown;
        errType = Unknown;
        return false;
    }

    public bool TryGetArrayInfo(out TypeSymbol elementType, out int length)
    {
        if (IsFixedArray)
        {
            var inner = Name.Substring(1, Name.Length - 2);
            var parts = inner.Split(';');
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int len))
            {
                elementType = FromName(parts[0].Trim());
                length = len;
                return true;
            }
        }
        elementType = Unknown;
        length = 0;
        return false;
    }

    public bool TryGetDynamicArrayElement(out TypeSymbol elementType)
    {
        if (IsDynamicArray)
        {
            var inner = Name.Substring(1, Name.Length - 2).Trim();
            elementType = FromName(inner);
            return true;
        }
        elementType = Unknown;
        return false;
    }

    public bool TryGetMapInfo(out TypeSymbol keyType, out TypeSymbol valueType)
    {
        if (IsMap)
        {
            var inner = Name.Substring(4, Name.Length - 5);
            int depth = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<' || inner[i] == '[') depth++;
                else if (inner[i] == '>' || inner[i] == ']') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    keyType = FromName(inner.Substring(0, i).Trim());
                    valueType = FromName(inner.Substring(i + 1).Trim());
                    return true;
                }
            }
        }
        keyType = Unknown;
        valueType = Unknown;
        return false;
    }

    public bool TryGetFunctionInfo(out List<TypeSymbol> paramTypes, out TypeSymbol returnType)
    {
        if (IsFunction)
        {
            int colonIdx = -1;
            int depth = 0;
            int parenClose = -1;
            for (int i = 2; i < Name.Length; i++)
            {
                if (Name[i] == '(' || Name[i] == '<' || Name[i] == '[') depth++;
                else if (Name[i] == ')' || Name[i] == '>' || Name[i] == ']')
                {
                    depth--;
                    if (depth == 0 && Name[i] == ')' && parenClose == -1)
                    {
                        parenClose = i;
                    }
                }
                else if (Name[i] == ':' && depth == 0 && parenClose != -1)
                {
                    colonIdx = i;
                    break;
                }
            }

            if (parenClose != -1)
            {
                var paramsStr = Name.Substring(3, parenClose - 3).Trim();
                paramTypes = new List<TypeSymbol>();
                if (!string.IsNullOrEmpty(paramsStr))
                {
                    depth = 0;
                    int start = 0;
                    for (int i = 0; i < paramsStr.Length; i++)
                    {
                        if (paramsStr[i] == '<' || paramsStr[i] == '[' || paramsStr[i] == '(') depth++;
                        else if (paramsStr[i] == '>' || paramsStr[i] == ']' || paramsStr[i] == ')') depth--;
                        else if (paramsStr[i] == ',' && depth == 0)
                        {
                            paramTypes.Add(FromName(paramsStr.Substring(start, i - start).Trim()));
                            start = i + 1;
                        }
                    }
                    paramTypes.Add(FromName(paramsStr.Substring(start).Trim()));
                }

                if (colonIdx != -1)
                {
                    returnType = FromName(Name.Substring(colonIdx + 1).Trim());
                }
                else
                {
                    returnType = Void;
                }
                return true;
            }
        }
        paramTypes = new List<TypeSymbol>();
        returnType = Unknown;
        return false;
    }

    public static TypeSymbol CreateArray(TypeSymbol elem, int length) => new($"[{elem.Name}; {length}]", IsPrimitive: false);

    public static TypeSymbol CreateDynamicArray(TypeSymbol elem) => new($"[{elem.Name}]", IsPrimitive: false);

    public static TypeSymbol CreateMap(TypeSymbol key, TypeSymbol val) => new($"Map<{key.Name}, {val.Name}>", IsPrimitive: false);

    public static TypeSymbol CreateOption(TypeSymbol val) => new($"Option<{val.Name}>", IsPrimitive: false);

    public static TypeSymbol CreateResult(TypeSymbol ok, TypeSymbol err) => new($"Result<{ok.Name}, {err.Name}>", IsPrimitive: false);

    public static TypeSymbol CreateFunction(IReadOnlyList<TypeSymbol> paramTypes, TypeSymbol returnType)
    {
        var pStr = string.Join(", ", paramTypes.Select(p => p.Name));
        return new TypeSymbol($"fn({pStr}): {returnType.Name}", IsPrimitive: false);
    }

    public static TypeSymbol FromName(string? name)
    {
        if (name == null) return Unknown;
        if (name.StartsWith("fn("))
        {
            var dummy = new TypeSymbol(name);
            if (dummy.TryGetFunctionInfo(out var pTypes, out var rType))
            {
                return CreateFunction(pTypes, rType);
            }
        }
        if (name.StartsWith("Vec<") && name.EndsWith(">"))
        {
            var inner = name.Substring(4, name.Length - 5).Trim();
            return CreateDynamicArray(FromName(inner));
        }
        if (name.StartsWith("List<") && name.EndsWith(">"))
        {
            var inner = name.Substring(5, name.Length - 6).Trim();
            return CreateDynamicArray(FromName(inner));
        }
        if (name.StartsWith("Option<") && name.EndsWith(">"))
        {
            var inner = name.Substring(7, name.Length - 8).Trim();
            return CreateOption(FromName(inner));
        }
        if (name.StartsWith("Result<") && name.EndsWith(">"))
        {
            var inner = name.Substring(7, name.Length - 8);
            int depth = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<' || inner[i] == '[') depth++;
                else if (inner[i] == '>' || inner[i] == ']') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    var ok = FromName(inner.Substring(0, i).Trim());
                    var err = FromName(inner.Substring(i + 1).Trim());
                    return CreateResult(ok, err);
                }
            }
        }
        if ((name.StartsWith("HashMap<") && name.EndsWith(">")) || (name.StartsWith("Map<") && name.EndsWith(">")))
        {
            int prefixLen = name.StartsWith("HashMap<") ? 8 : 4;
            var inner = name.Substring(prefixLen, name.Length - prefixLen - 1);
            int depth = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<' || inner[i] == '[') depth++;
                else if (inner[i] == '>' || inner[i] == ']') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    var k = FromName(inner.Substring(0, i).Trim());
                    var v = FromName(inner.Substring(i + 1).Trim());
                    return CreateMap(k, v);
                }
            }
        }
        return name switch
        {
            "i32" or "int" => I32,
            "i64" => I64,
            "u32" => U32,
            "u64" => U64,
            "f32" or "float" => F32,
            "f64" or "double" => F64,
            "bool" => Bool,
            "string" or "str" => String,
            "World" or "world" => World,
            "Commands" or "commands" => Commands,
            "Entity" or "entity" => Entity,
            "void" => Void,
            _ => new TypeSymbol(name, IsPrimitive: false)
        };
    }
}
