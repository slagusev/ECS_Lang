using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed record ComponentFieldSymbol(string Name, TypeSymbol Type, SourceSpan Span);

public sealed record ComponentSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
)
{
    public TypeSymbol Type => TypeSymbol.FromName(Name);
}

public sealed record ResourceSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
)
{
    public TypeSymbol Type => TypeSymbol.FromName(Name);
}

public sealed record StructSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
)
{
    public TypeSymbol Type => TypeSymbol.FromName(Name);
}

public sealed record EventSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
)
{
    public TypeSymbol Type => TypeSymbol.FromName(Name);
}

public sealed record EnumMemberSymbol(string Name, int Value, SourceSpan Span);

public sealed record EnumSymbol(
    string Name,
    IReadOnlyDictionary<string, EnumMemberSymbol> Members,
    SourceSpan Span
);

public sealed record QueryParamSymbol(
    bool IsMutable,
    string ParameterName,
    TypeSymbol Type,
    bool IsResource,
    SourceSpan Span
);

public sealed record SystemSymbol(
    string Name,
    IReadOnlyList<QueryParamSymbol> QueryParams,
    IReadOnlyList<QueryParamSymbol> ReadParams,
    IReadOnlyList<QueryFilter> Filters,
    SourceSpan Span
)
{
    public SystemSymbol(
        string name,
        IReadOnlyList<QueryParamSymbol> queryParams,
        IReadOnlyList<QueryParamSymbol> readParams,
        SourceSpan span)
        : this(name, queryParams, readParams, Array.Empty<QueryFilter>(), span)
    {
    }

    public bool IsEventSystem => ReadParams.Count > 0;

    public IReadOnlySet<string> MutComponents => QueryParams
        .Where(q => !q.IsResource && q.Type != TypeSymbol.Entity && q.Type != TypeSymbol.Commands && q.IsMutable)
        .Select(q => q.Type.Name)
        .ToHashSet();

    public IReadOnlySet<string> ConstComponents => QueryParams
        .Where(q => !q.IsResource && q.Type != TypeSymbol.Entity && q.Type != TypeSymbol.Commands && !q.IsMutable)
        .Select(q => q.Type.Name)
        .ToHashSet();

    public IReadOnlySet<string> MutResources => QueryParams.Concat(ReadParams)
        .Where(q => q.IsResource && q.IsMutable)
        .Select(q => q.Type.Name)
        .ToHashSet();

    public IReadOnlySet<string> ConstResources => QueryParams.Concat(ReadParams)
        .Where(q => q.IsResource && !q.IsMutable)
        .Select(q => q.Type.Name)
        .ToHashSet();

    public bool HasConflictWith(SystemSymbol other, out string reason)
    {
        // 1. Check mutable component conflicts
        foreach (var comp in MutComponents)
        {
            if (other.MutComponents.Contains(comp))
            {
                reason = $"write-write conflict on component '{comp}'";
                return true;
            }
            if (other.ConstComponents.Contains(comp))
            {
                reason = $"write-read conflict on component '{comp}'";
                return true;
            }
        }

        foreach (var comp in ConstComponents)
        {
            if (other.MutComponents.Contains(comp))
            {
                reason = $"read-write conflict on component '{comp}'";
                return true;
            }
        }

        // 2. Check resource conflicts
        foreach (var res in MutResources)
        {
            if (other.MutResources.Contains(res))
            {
                reason = $"write-write conflict on resource '{res}'";
                return true;
            }
            if (other.ConstResources.Contains(res))
            {
                reason = $"write-read conflict on resource '{res}'";
                return true;
            }
        }

        foreach (var res in ConstResources)
        {
            if (other.MutResources.Contains(res))
            {
                reason = $"read-write conflict on resource '{res}'";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}
