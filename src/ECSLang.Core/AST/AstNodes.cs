using ECSLang.Core;

namespace ECSLang.Core.AST;

public abstract record AstNode(SourceSpan Span);

public sealed record ProgramNode(IReadOnlyList<DeclarationNode> Declarations, SourceSpan Span) : AstNode(Span);

public abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);

public sealed record FieldDefinition(string Name, string TypeName, SourceSpan Span) : AstNode(Span);

public sealed record ComponentDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record ResourceDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record StructDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record EventDeclaration(
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record QueryParameter(
    bool IsMutable,
    string Name,
    string TypeName,
    SourceSpan Span
) : AstNode(Span);

public sealed record SystemDeclaration(
    string Name,
    IReadOnlyList<QueryParameter> QueryParams,
    IReadOnlyList<QueryParameter> ReadParams,
    BlockStatement Body,
    SourceSpan Span
) : DeclarationNode(Span)
{
    public bool IsEventSystem => ReadParams.Count > 0;
}

public sealed record FunctionDeclaration(
    string Name,
    IReadOnlyList<FunctionParameter> Parameters,
    string? ReturnType,
    BlockStatement Body,
    SourceSpan Span
) : DeclarationNode(Span);

public sealed record FunctionParameter(string Name, string TypeName, SourceSpan Span) : AstNode(Span);

public abstract record StageAction(SourceSpan Span) : AstNode(Span);

public sealed record SystemCallAction(string SystemName, SourceSpan Span) : StageAction(Span);

public sealed record ParallelAction(IReadOnlyList<SystemCallAction> Systems, SourceSpan Span) : StageAction(Span);

public sealed record SyncAction(SourceSpan Span) : StageAction(Span);

public sealed record SortHierarchyAction(SourceSpan Span) : StageAction(Span);

public sealed record SwapEventsAction(SourceSpan Span) : StageAction(Span);

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

public sealed record ReturnStatement(ExpressionNode? Value, SourceSpan Span) : StatementNode(Span);

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

public sealed record IdentifierExpression(string Name, SourceSpan Span) : ExpressionNode(Span);

public sealed record StringLiteralExpression(string Value, SourceSpan Span) : ExpressionNode(Span);

public sealed record NumberLiteralExpression(string RawValue, bool IsFloatingPoint, SourceSpan Span) : ExpressionNode(Span);

public sealed record BooleanLiteralExpression(bool Value, SourceSpan Span) : ExpressionNode(Span);
