using System.Text;
using ECSLang.Core;

namespace ECSLang.Frontend;

public sealed class Lexer
{
    private readonly string _text;
    private readonly string _filePath;
    private readonly DiagnosticsBag _diagnostics;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["fn"] = TokenType.Fn,
        ["return"] = TokenType.Return,
        ["component"] = TokenType.Component,
        ["system"] = TokenType.System,
        ["query"] = TokenType.Query,
        ["resource"] = TokenType.Resource,
        ["pipeline"] = TokenType.Pipeline,
        ["stage"] = TokenType.Stage,
        ["parallel"] = TokenType.Parallel,
        ["sync"] = TokenType.Sync,
        ["sort_hierarchy"] = TokenType.SortHierarchy,
        ["let"] = TokenType.Let,
        ["mut"] = TokenType.Mut,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["while"] = TokenType.While,
        ["for"] = TokenType.For,
        ["in"] = TokenType.In,
        ["struct"] = TokenType.Struct,
        ["event"] = TokenType.Event,
        ["read"] = TokenType.Read,
        ["swap_events"] = TokenType.SwapEvents,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
    };

    public Lexer(string text, string filePath, DiagnosticsBag diagnostics)
    {
        _text = text;
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

    private char Current => _position < _text.Length ? _text[_position] : '\0';
    private char Peek(int offset) => (_position + offset < _text.Length) ? _text[_position + offset] : '\0';

    private void Advance()
    {
        if (_position < _text.Length)
        {
            if (_text[_position] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }
    }

    public List<Token> TokenizeAll()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var token = NextToken();
            tokens.Add(token);
            if (token.Type == TokenType.EndOfFile)
                break;
        }
        return tokens;
    }

    public Token NextToken()
    {
        while (true)
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            // Single line comment: //
            if (Current == '/' && Peek(1) == '/')
            {
                Advance();
                Advance();
                while (Current != '\0' && Current != '\n')
                    Advance();
                continue;
            }

            // Multi line comment: /* ... */
            if (Current == '/' && Peek(1) == '*')
            {
                Advance();
                Advance();
                while (Current != '\0' && !(Current == '*' && Peek(1) == '/'))
                    Advance();
                if (Current == '*' && Peek(1) == '/')
                {
                    Advance();
                    Advance();
                }
                continue;
            }

            break;
        }

        int startPos = _position;
        int startLine = _line;
        int startCol = _column;

        if (Current == '\0')
        {
            return new Token(TokenType.EndOfFile, "", new SourceSpan(_filePath, startLine, startCol, 0));
        }

        // Numbers (integers or floats)
        if (char.IsAsciiDigit(Current))
        {
            var sb = new StringBuilder();
            while (char.IsAsciiDigit(Current))
            {
                sb.Append(Current);
                Advance();
            }

            if (Current == '.' && char.IsAsciiDigit(Peek(1)))
            {
                sb.Append(Current);
                Advance();
                while (char.IsAsciiDigit(Current))
                {
                    sb.Append(Current);
                    Advance();
                }
            }

            string text = sb.ToString();
            return new Token(TokenType.NumberLiteral, text, new SourceSpan(_filePath, startLine, startCol, text.Length));
        }

        // Identifiers & Keywords
        if (char.IsLetter(Current) || Current == '_')
        {
            var sb = new StringBuilder();
            while (char.IsLetterOrDigit(Current) || Current == '_')
            {
                sb.Append(Current);
                Advance();
            }

            string ident = sb.ToString();
            var type = Keywords.TryGetValue(ident, out var kwType) ? kwType : TokenType.Identifier;
            return new Token(type, ident, new SourceSpan(_filePath, startLine, startCol, ident.Length));
        }

        // String literals: "hello world"
        if (Current == '"')
        {
            Advance();
            var sb = new StringBuilder();
            while (Current != '\0' && Current != '"')
            {
                if (Current == '\\')
                {
                    Advance();
                    sb.Append(Current switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '"' => '"',
                        '0' => '\0',
                        _ => Current
                    });
                }
                else
                {
                    sb.Append(Current);
                }
                Advance();
            }

            if (Current == '"')
            {
                Advance();
            }
            else
            {
                _diagnostics.ReportError("Unterminated string literal.", new SourceSpan(_filePath, startLine, startCol, _position - startPos));
            }

            string content = sb.ToString();
            return new Token(TokenType.StringLiteral, content, new SourceSpan(_filePath, startLine, startCol, _position - startPos));
        }

        // Two-character operators
        char ch = Current;
        char next = Peek(1);

        if (ch == '=' && next == '=') { Advance(); Advance(); return new Token(TokenType.EqualEqual, "==", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '!' && next == '=') { Advance(); Advance(); return new Token(TokenType.BangEqual, "!=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '<' && next == '=') { Advance(); Advance(); return new Token(TokenType.LessEqual, "<=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '>' && next == '=') { Advance(); Advance(); return new Token(TokenType.GreaterEqual, ">=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '+' && next == '=') { Advance(); Advance(); return new Token(TokenType.PlusEqual, "+=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '-' && next == '=') { Advance(); Advance(); return new Token(TokenType.MinusEqual, "-=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '*' && next == '=') { Advance(); Advance(); return new Token(TokenType.StarEqual, "*=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '/' && next == '=') { Advance(); Advance(); return new Token(TokenType.SlashEqual, "/=", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '&' && next == '&') { Advance(); Advance(); return new Token(TokenType.AmpAmp, "&&", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '|' && next == '|') { Advance(); Advance(); return new Token(TokenType.PipePipe, "||", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == ':' && next == ':') { Advance(); Advance(); return new Token(TokenType.ColonColon, "::", new SourceSpan(_filePath, startLine, startCol, 2)); }
        if (ch == '.' && next == '.') { Advance(); Advance(); return new Token(TokenType.DotDot, "..", new SourceSpan(_filePath, startLine, startCol, 2)); }

        // Single-character tokens
        Advance();
        var tokenType = ch switch
        {
            '(' => TokenType.OpenParen,
            ')' => TokenType.CloseParen,
            '{' => TokenType.OpenBrace,
            '}' => TokenType.CloseBrace,
            ':' => TokenType.Colon,
            ';' => TokenType.Semicolon,
            ',' => TokenType.Comma,
            '.' => TokenType.Dot,
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Star,
            '/' => TokenType.Slash,
            '%' => TokenType.Percent,
            '=' => TokenType.Equal,
            '<' => TokenType.Less,
            '>' => TokenType.Greater,
            '!' => TokenType.Bang,
            _ => TokenType.BadToken
        };

        if (tokenType == TokenType.BadToken)
        {
            _diagnostics.ReportError($"Unexpected character '{ch}'.", new SourceSpan(_filePath, startLine, startCol, 1));
        }

        return new Token(tokenType, ch.ToString(), new SourceSpan(_filePath, startLine, startCol, 1));
    }
}
