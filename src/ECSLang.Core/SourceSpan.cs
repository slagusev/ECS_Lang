namespace ECSLang.Core;

public readonly record struct SourceSpan(string FilePath, int Line, int Column, int Length)
{
    public static readonly SourceSpan None = new("<unknown>", 0, 0, 0);

    public override string ToString() => $"{FilePath}({Line},{Column})";
}
