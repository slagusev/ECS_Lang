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
    public static readonly TypeSymbol Unknown = new("<unknown>");

    public bool IsNumeric => this == I32 || this == I64 || this == U32 || this == U64 || this == F32 || this == F64;
    public bool IsFloatingPoint => this == F32 || this == F64;
    public bool IsInteger => this == I32 || this == I64 || this == U32 || this == U64;

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
        "void" => Void,
        null => Unknown,
        _ => new TypeSymbol(name, IsPrimitive: false)
    };
}
