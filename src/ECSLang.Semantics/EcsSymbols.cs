using ECSLang.Core;

namespace ECSLang.Semantics;

public sealed record ComponentFieldSymbol(string Name, TypeSymbol Type, SourceSpan Span);

public sealed record ComponentSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
);

public sealed record ResourceSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
    SourceSpan Span
);

public sealed record StructSymbol(
    string Name,
    IReadOnlyList<ComponentFieldSymbol> Fields,
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
    SourceSpan Span
);
