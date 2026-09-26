using ECSLang.Core;

namespace ECSLang.Frontend;

public enum TokenType
{
    // Special
    EndOfFile,
    BadToken,

    // Literals & Identifiers
    Identifier,
    StringLiteral,
    NumberLiteral,
    True,
    False,

    // Keywords
    Fn,
    Return,
    Component,
    System,
    Query,
    Resource,
    Pipeline,
    Stage,
    Parallel,
    Sync,
    SortHierarchy,
    Let,
    Mut,
    If,
    Else,
    While,
    For,
    In,
    Struct,
    Event,
    Read,
    SwapEvents,
    Enum,
    Match,
    ApplyCommands,

    // Punctuation & Delimiters
    OpenParen,    // (
    CloseParen,   // )
    OpenBrace,    // {
    CloseBrace,   // }
    OpenBracket,  // [
    CloseBracket, // ]
    Colon,        // :
    ColonColon,   // ::
    Semicolon,    // ;
    Comma,        // ,
    Dot,          // .
    DotDot,       // ..
    FatArrow,     // =>
    Underscore,   // _

    // Operators
    Plus,         // +
    Minus,        // -
    Star,         // *
    Slash,        // /
    Percent,      // %
    Equal,        // =
    PlusEqual,    // +=
    MinusEqual,   // -=
    StarEqual,    // *=
    SlashEqual,   // /=
    EqualEqual,   // ==
    BangEqual,    // !=
    Less,         // <
    LessEqual,    // <=
    Greater,      // >
    GreaterEqual, // >=
    AmpAmp,       // &&
    PipePipe,     // ||
    Bang,         // !
}

public sealed record Token(TokenType Type, string Text, SourceSpan Span)
{
    public override string ToString() => $"{Type} '{Text}' at {Span}";
}
