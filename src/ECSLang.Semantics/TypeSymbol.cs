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
    public static readonly TypeSymbol Unknown = new("<unknown>");

    public bool IsNumeric => this == I32 || this == I64 || this == U32 || this == U64 || this == F32 || this == F64;
    public bool IsFloatingPoint => this == F32 || this == F64;
    public bool IsInteger => this == I32 || this == I64 || this == U32 || this == U64;
    public bool IsArray => Name.StartsWith("[") && Name.EndsWith("]");

    public bool TryGetArrayInfo(out TypeSymbol elementType, out int length)
    {
        if (IsArray)
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

    public static TypeSymbol CreateArray(TypeSymbol elem, int length) => new($"[{elem.Name}; {length}]", IsPrimitive: false);

    public static TypeSymbol FromName(string? name) => name switch
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
        "void" => Void,
        null => Unknown,
        _ => new TypeSymbol(name, IsPrimitive: false)
    };
}
