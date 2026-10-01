using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    // Expression parsing with precedence
    public ExpressionNode ParseExpression()
    {
        return ParseBinaryExpression(0);
    }

    private ExpressionNode ParseBinaryExpression(int parentPrecedence)
    {
        var left = ParseUnaryExpression();

        while (true)
        {
            int precedence = GetBinaryOperatorPrecedence(Current.Type);
            if (precedence == 0 || precedence <= parentPrecedence)
                break;

            var opToken = Advance();
            var op = TokenToBinaryOp(opToken.Type);
            var right = ParseBinaryExpression(precedence);
            left = new BinaryExpression(left, op, right, left.Span);
        }

        return left;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        if (Check(TokenType.Minus))
        {
            var opTok = Advance();
            var operand = ParseUnaryExpression();
            return new UnaryExpression(UnaryOperator.Negate, operand, opTok.Span);
        }

        if (Check(TokenType.Bang))
        {
            var opTok = Advance();
            var operand = ParseUnaryExpression();
            return new UnaryExpression(UnaryOperator.LogicalNot, operand, opTok.Span);
        }

        return ParsePostfixExpression();
    }

    private ExpressionNode ParsePostfixExpression()
    {
        var expr = ParsePrimaryExpression();

        while (true)
        {
            if (Check(TokenType.Dot))
            {
                Advance();
                var memberTok = Match(TokenType.Identifier, "Expected member name after '.'.");
                if (Check(TokenType.OpenParen))
                {
                    Advance();
                    var args = new List<ExpressionNode>();
                    if (!Check(TokenType.CloseParen))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                    }
                    Match(TokenType.CloseParen, "Expected ')' after argument list.");
                    expr = new MethodCallExpression(expr, memberTok.Text, args, expr.Span);
                }
                else
                {
                    expr = new MemberAccessExpression(expr, memberTok.Text, expr.Span);
                }
            }
            else if (Check(TokenType.OpenBracket))
            {
                Advance(); // [
                var indexExpr = ParseExpression();
                Match(TokenType.CloseBracket, "Expected ']' after array index.");
                expr = new IndexExpression(expr, indexExpr, expr.Span);
            }
            else if (Check(TokenType.OpenParen) && !(expr is CallExpression))
            {
                Advance(); // (
                var args = new List<ExpressionNode>();
                if (!Check(TokenType.CloseParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                }
                Match(TokenType.CloseParen, "Expected ')' after argument list.");
                expr = new IndirectCallExpression(expr, args, expr.Span);
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private ExpressionNode ParseLambdaExpression()
    {
        var startSpan = Current.Span;
        var parameters = new List<LambdaParameter>();

        if (Check(TokenType.PipePipe))
        {
            Advance(); // ||
        }
        else if (Check(TokenType.Pipe))
        {
            Advance(); // |
            if (!Check(TokenType.Pipe))
            {
                do
                {
                    var paramName = Match(TokenType.Identifier, "Expected parameter name in lambda.");
                    string? paramType = null;
                    if (Check(TokenType.Colon))
                    {
                        Advance(); // :
                        paramType = ParseTypeAnnotation();
                    }
                    parameters.Add(new LambdaParameter(paramName.Text, paramType, paramName.Span));
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            }
            Match(TokenType.Pipe, "Expected '|' to close lambda parameters.");
        }

        string? returnType = null;
        if (Check(TokenType.Arrow) || Check(TokenType.Colon))
        {
            Advance();
            returnType = ParseTypeAnnotation();
        }

        BlockStatement body;
        if (Check(TokenType.OpenBrace))
        {
            body = ParseBlockStatement();
        }
        else
        {
            var expr = ParseExpression();
            var retStmt = new ReturnStatement(expr, expr.Span);
            body = new BlockStatement(new[] { retStmt }, expr.Span);
        }

        return new LambdaExpression(parameters, returnType, body, startSpan);
    }

    private ExpressionNode ParsePrimaryExpression()
    {
        var token = Current;

        if (token.Type == TokenType.Pipe || token.Type == TokenType.PipePipe)
        {
            return ParseLambdaExpression();
        }

        if (token.Type == TokenType.OpenBracket)
        {
            var openTok = Advance();
            var elements = new List<ExpressionNode>();
            if (!Check(TokenType.CloseBracket))
            {
                do
                {
                    elements.Add(ParseExpression());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            }
            Match(TokenType.CloseBracket, "Expected ']' to close array literal.");
            return new ArrayLiteralExpression(elements, openTok.Span);
        }

        if (token.Type == TokenType.OpenParen)
        {
            Advance();
            var expr = ParseExpression();
            Match(TokenType.CloseParen, "Expected ')' after parenthesized expression.");
            return expr;
        }

        if (token.Type == TokenType.Underscore)
        {
            Advance();
            return new WildcardExpression(token.Span);
        }

        if (token.Type == TokenType.True)
        {
            Advance();
            return new BooleanLiteralExpression(true, token.Span);
        }

        if (token.Type == TokenType.False)
        {
            Advance();
            return new BooleanLiteralExpression(false, token.Span);
        }

        if (token.Type == TokenType.StringLiteral)
        {
            Advance();
            return new StringLiteralExpression(token.Text, token.Span);
        }

        if (token.Type == TokenType.InterpolatedString)
        {
            Advance();
            return ParseInterpolatedString(token.Text, token.Span);
        }

        if (token.Type == TokenType.NumberLiteral)
        {
            Advance();
            bool isFloat = token.Text.Contains('.');
            return new NumberLiteralExpression(token.Text, isFloat, token.Span);
        }

        if (token.Type == TokenType.Identifier)
        {
            Advance();
            string identName = token.Text;

            if (Check(TokenType.Less) && IsGenericInstantiationLookahead())
            {
                Advance(); // <
                var typeArgs = new List<string>();
                do
                {
                    typeArgs.Add(ParseTypeAnnotation());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                Match(TokenType.Greater, "Expected '>' to close generic type arguments.");

                string genTypeName = $"{identName}<{string.Join(", ", typeArgs)}>";

                if (Check(TokenType.ColonColon))
                {
                    Advance(); // ::
                    var variantTok = Match(TokenType.Identifier, "Expected identifier after '::'.");
                    string qualified = $"{genTypeName}::{variantTok.Text}";
                    if (Check(TokenType.OpenParen))
                    {
                        Advance(); // (
                        var genArgs = new List<ExpressionNode>();
                        if (!Check(TokenType.CloseParen))
                        {
                            do
                            {
                                genArgs.Add(ParseExpression());
                            } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                        }
                        Match(TokenType.CloseParen, "Expected ')' after argument list.");
                        return new CallExpression(qualified, genArgs, token.Span);
                    }
                    return new IdentifierExpression(qualified, token.Span);
                }

                if (Check(TokenType.OpenParen))
                {
                    Advance(); // (
                    var genArgs = new List<ExpressionNode>();
                    if (!Check(TokenType.CloseParen))
                    {
                        do
                        {
                            genArgs.Add(ParseExpression());
                        } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                    }
                    Match(TokenType.CloseParen, "Expected ')' after argument list.");
                    return new CallExpression(genTypeName, genArgs, token.Span);
                }

                return new IdentifierExpression(genTypeName, token.Span);
            }

            while (Check(TokenType.ColonColon))
            {
                Advance();
                var nextTok = Match(TokenType.Identifier, "Expected identifier after '::'.");
                identName += "::" + nextTok.Text;
            }

            // Function call: name(arg1, arg2)
            if (Check(TokenType.OpenParen))
            {
                Advance();
                var args = new List<ExpressionNode>();
                if (!Check(TokenType.CloseParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                }
                Match(TokenType.CloseParen, "Expected ')' after argument list.");
                return new CallExpression(identName, args, token.Span);
            }

            return new IdentifierExpression(identName, token.Span);
        }

        _diagnostics.ReportError($"Unexpected expression token '{token.Text}'.", token.Span);
        Advance();
        return new IdentifierExpression("<error>", token.Span);
    }

    private static int GetBinaryOperatorPrecedence(TokenType type) => type switch
    {
        TokenType.PipePipe => 1,
        TokenType.AmpAmp => 2,
        TokenType.EqualEqual or TokenType.BangEqual => 3,
        TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual => 4,
        TokenType.Plus or TokenType.Minus => 5,
        TokenType.Star or TokenType.Slash or TokenType.Percent => 6,
        _ => 0
    };

    private ExpressionNode ParseInterpolatedString(string text, SourceSpan span)
    {
        var parts = new List<ExpressionNode>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            int braceOpen = text.IndexOf('{', i);
            if (braceOpen < 0)
            {
                parts.Add(new StringLiteralExpression(text.Substring(i), span));
                break;
            }

            if (braceOpen > i)
            {
                parts.Add(new StringLiteralExpression(text.Substring(i, braceOpen - i), span));
            }

            int braceClose = text.IndexOf('}', braceOpen + 1);
            if (braceClose < 0)
            {
                _diagnostics.ReportError("Unclosed '{' in interpolated string.", span);
                break;
            }

            string exprText = text.Substring(braceOpen + 1, braceClose - braceOpen - 1).Trim();
            if (exprText.Length > 0)
            {
                var subLexer = new Lexer(exprText, span.FilePath, _diagnostics);
                var subTokens = subLexer.TokenizeAll();
                var subParser = new Parser(subTokens, _diagnostics);
                var exprNode = subParser.ParseExpression();
                parts.Add(new CallExpression("to_string", new[] { exprNode }, span));
            }

            i = braceClose + 1;
        }

        if (parts.Count == 0)
        {
            return new StringLiteralExpression("", span);
        }

        ExpressionNode result = parts[0];
        for (int p = 1; p < parts.Count; p++)
        {
            result = new BinaryExpression(result, BinaryOperator.Add, parts[p], span);
        }

        return result;
    }

    private static BinaryOperator TokenToBinaryOp(TokenType type) => type switch
    {
        TokenType.Plus => BinaryOperator.Add,
        TokenType.Minus => BinaryOperator.Subtract,
        TokenType.Star => BinaryOperator.Multiply,
        TokenType.Slash => BinaryOperator.Divide,
        TokenType.Percent => BinaryOperator.Modulo,
        TokenType.EqualEqual => BinaryOperator.Equal,
        TokenType.BangEqual => BinaryOperator.NotEqual,
        TokenType.Less => BinaryOperator.Less,
        TokenType.LessEqual => BinaryOperator.LessOrEqual,
        TokenType.Greater => BinaryOperator.Greater,
        TokenType.GreaterEqual => BinaryOperator.GreaterOrEqual,
        TokenType.AmpAmp => BinaryOperator.LogicalAnd,
        TokenType.PipePipe => BinaryOperator.LogicalOr,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unexpected binary token {type}")
    };

    private bool IsGenericInstantiationLookahead()
    {
        if (!Check(TokenType.Less))
            return false;

        int offset = 1;
        int depth = 1;

        while (true)
        {
            var tok = Peek(offset);
            if (tok.Type == TokenType.EndOfFile || tok.Type == TokenType.Semicolon)
                return false;

            if (tok.Type == TokenType.Less)
            {
                depth++;
            }
            else if (tok.Type == TokenType.Greater)
            {
                depth--;
                if (depth == 0)
                {
                    var nextTok = Peek(offset + 1);
                    return nextTok.Type == TokenType.OpenParen || 
                           nextTok.Type == TokenType.ColonColon ||
                           nextTok.Type == TokenType.OpenBrace;
                }
            }
            else if (tok.Type != TokenType.Identifier &&
                     tok.Type != TokenType.Comma &&
                     tok.Type != TokenType.OpenBracket &&
                     tok.Type != TokenType.CloseBracket &&
                     tok.Type != TokenType.Colon &&
                     tok.Type != TokenType.Arrow)
            {
                return false;
            }

            offset++;
        }
    }
}
