using ECSLang.Core;

namespace ECSLang.Core.AST;

public abstract record AstNode(SourceSpan Span);

public sealed record ProgramNode(IReadOnlyList<DeclarationNode> Declarations, SourceSpan Span) : AstNode(Span);

public abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);

public sealed record ImportDirective(string ModulePath, SourceSpan Span) : DeclarationNode(Span);

public sealed record TypeParameter(string Name, string? ConstraintTrait = null, SourceSpan Span = default) : AstNode(Span);

public sealed record TraitMethodDeclaration(
    string Name,
    IReadOnlyList<FunctionParameter> Parameters,
    string? ReturnType,
    SourceSpan Span
) : AstNode(Span);

public sealed record TraitDeclaration(
    string Name,
    IReadOnlyList<TraitMethodDeclaration> Methods,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record FieldDefinition(string Name, string TypeName, SourceSpan Span) : AstNode(Span);

public sealed record ComponentDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span,
    IReadOnlyList<TypeParameter>? TypeParameters = null
) : DeclarationNode(Span);

public sealed record ResourceDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record StructDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span,
    IReadOnlyList<TypeParameter>? TypeParameters = null
) : DeclarationNode(Span);

public sealed record EventDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record EnumMemberDefinition(
    string Name,
    int? Value,
    SourceSpan Span
) : AstNode(Span);

public sealed record EnumDeclaration(
    string Name,
    IReadOnlyList<EnumMemberDefinition> Members,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record ConstDeclaration(
    string Name,
    string TypeName,
    ExpressionNode Initializer,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record QueryParameter(
    bool IsMutable,
    string Name,
    string TypeName,
    SourceSpan Span
) : AstNode(Span);

public enum QueryFilterKind
{
    With,
    Without
}

public sealed record QueryFilter(
    QueryFilterKind Kind,
    string ComponentName,
    SourceSpan Span
) : AstNode(Span);

public sealed record SystemDeclaration(
    string Name,
    IReadOnlyList<QueryParameter> QueryParams,
    IReadOnlyList<QueryParameter> ReadParams,
    IReadOnlyList<QueryFilter> Filters,
    BlockStatement Body,
    SourceSpan Span,
    IReadOnlyList<TypeParameter>? TypeParameters = null
) : DeclarationNode(Span)
{
    public bool IsEventSystem => ReadParams.Count > 0;

    public SystemDeclaration(
        string name,
        IReadOnlyList<QueryParameter> queryParams,
        IReadOnlyList<QueryParameter> readParams,
        BlockStatement body,
        SourceSpan span)
        : this(name, queryParams, readParams, Array.Empty<QueryFilter>(), body, span, null)
    {
    }
}

public sealed record FunctionDeclaration(
    string Name,
    IReadOnlyList<FunctionParameter> Parameters,
    string? ReturnType,
    BlockStatement Body,
    SourceSpan Span,
    IReadOnlyList<TypeParameter>? TypeParameters = null
) : DeclarationNode(Span);

public sealed record FunctionParameter(string Name, string TypeName, SourceSpan Span, bool IsMutable = false) : AstNode(Span);

public sealed record ImplDeclaration(
    string StructName,
    IReadOnlyList<FunctionDeclaration> Methods,
    SourceSpan Span,
    string? TraitName = null,
    IReadOnlyList<TypeParameter>? TypeParameters = null
) : DeclarationNode(Span);

public abstract record StageAction(SourceSpan Span) : AstNode(Span);

public sealed record SystemCallAction(string SystemName, SourceSpan Span) : StageAction(Span);

public sealed record ParallelAction(IReadOnlyList<SystemCallAction> Systems, SourceSpan Span) : StageAction(Span);

public sealed record ParallelAutoBlockNode(IReadOnlyList<SystemCallAction> Systems, SourceSpan Span) : StageAction(Span);

public sealed record SyncAction(SourceSpan Span) : StageAction(Span);

public sealed record SortHierarchyAction(SourceSpan Span) : StageAction(Span);

public sealed record SwapEventsAction(SourceSpan Span) : StageAction(Span);

public sealed record ApplyCommandsAction(SourceSpan Span) : StageAction(Span);

public sealed record StageDefinition(
    string Name,
    IReadOnlyList<StageAction> Actions,
    SourceSpan Span
) : AstNode(Span);

public sealed record PipelineDeclaration(
    string Name,
    IReadOnlyList<StageDefinition> Stages,
    SourceSpan Span
) : DeclarationNode(Span);

// Statements
public abstract record StatementNode(SourceSpan Span) : AstNode(Span);

public sealed record BlockStatement(IReadOnlyList<StatementNode> Statements, SourceSpan Span) : StatementNode(Span);

public sealed record VariableDeclarationStatement(
    string Name,
    bool IsMutable,
    string? TypeName,
    ExpressionNode Initializer,
    SourceSpan Span
) : StatementNode(Span);

public sealed record AssignmentStatement(
    string TargetName,
    string? MemberName,
    ExpressionNode? Index,
    AssignmentOperator Op,
    ExpressionNode Value,
    SourceSpan Span
) : StatementNode(Span);

public enum AssignmentOperator
{
    Assign,       // =
    PlusAssign,   // +=
    MinusAssign,  // -=
    MulAssign,    // *=
    DivAssign     // /=
}

public sealed record IfStatement(
    ExpressionNode Condition,
    BlockStatement ThenBranch,
    StatementNode? ElseBranch, // BlockStatement or another IfStatement
    SourceSpan Span
) : StatementNode(Span);

public sealed record WhileStatement(
    ExpressionNode Condition,
    BlockStatement Body,
    SourceSpan Span
) : StatementNode(Span);

public sealed record ForStatement(
    string VariableName,
    ExpressionNode Start,
    ExpressionNode End,
    BlockStatement Body,
    SourceSpan Span
) : StatementNode(Span);

public sealed record MatchArm(
    ExpressionNode Pattern,
    BlockStatement Body,
    SourceSpan Span
) : AstNode(Span);

public sealed record MatchStatement(
    ExpressionNode Scrutinee,
    IReadOnlyList<MatchArm> Arms,
    SourceSpan Span
) : StatementNode(Span);

public sealed record ReturnStatement(ExpressionNode? Value, SourceSpan Span) : StatementNode(Span);

public sealed record BreakStatementNode(SourceSpan Span) : StatementNode(Span);

public sealed record ContinueStatementNode(SourceSpan Span) : StatementNode(Span);

public sealed record ExpressionStatement(ExpressionNode Expression, SourceSpan Span) : StatementNode(Span);

// Expressions
public abstract record ExpressionNode(SourceSpan Span) : AstNode(Span);

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Modulo,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    LogicalAnd, LogicalOr
}

public enum UnaryOperator
{
    Negate,
    LogicalNot
}

public sealed record BinaryExpression(
    ExpressionNode Left,
    BinaryOperator Operator,
    ExpressionNode Right,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record UnaryExpression(
    UnaryOperator Operator,
    ExpressionNode Operand,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record CallExpression(
    string Callee,
    IReadOnlyList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record MemberAccessExpression(
    ExpressionNode Target,
    string MemberName,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record MethodCallExpression(
    ExpressionNode Target,
    string MethodName,
    IReadOnlyList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record ResourceGetExpressionNode(
    ExpressionNode Target,
    string ResourceName,
    SourceSpan Span
) : ExpressionNode(Span)
{
    public ResourceGetExpressionNode(string resourceName, SourceSpan span)
        : this(new IdentifierExpression("world", span), resourceName, span)
    {
    }
}

public sealed record IdentifierExpression(string Name, SourceSpan Span) : ExpressionNode(Span);

public sealed record StringLiteralExpression(string Value, SourceSpan Span) : ExpressionNode(Span);

public sealed record NumberLiteralExpression(string RawValue, bool IsFloatingPoint, SourceSpan Span) : ExpressionNode(Span);

public sealed record BooleanLiteralExpression(bool Value, SourceSpan Span) : ExpressionNode(Span);

public sealed record ArrayLiteralExpression(
    IReadOnlyList<ExpressionNode> Elements,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record IndexExpression(
    ExpressionNode Target,
    ExpressionNode Index,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record WildcardExpression(SourceSpan Span) : ExpressionNode(Span);

public sealed record LambdaParameter(string Name, string? TypeName, SourceSpan Span) : AstNode(Span);

public sealed record LambdaExpression(
    IReadOnlyList<LambdaParameter> Parameters,
    string? ReturnType,
    BlockStatement Body,
    List<string> Captures,
    SourceSpan Span
) : ExpressionNode(Span)
{
    public LambdaExpression(
        IReadOnlyList<LambdaParameter> parameters,
        string? returnType,
        BlockStatement body,
        SourceSpan span)
        : this(parameters, returnType, body, new List<string>(), span)
    {
    }
}

public sealed record IndirectCallExpression(
    ExpressionNode Callee,
    IReadOnlyList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record CastExpressionNode(
    ExpressionNode Expr,
    string TargetTypeName,
    SourceSpan Span
) : ExpressionNode(Span);

public sealed record ErrorPropagationExpressionNode(
    ExpressionNode Expr,
    SourceSpan Span
) : ExpressionNode(Span);
