using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    public BlockStatement ParseBlockStatement()
    {
        var openBrace = Match(TokenType.OpenBrace, "Expected '{' to start block.");
        var statements = new List<StatementNode>();

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var stmt = ParseStatement();
            if (stmt != null)
                statements.Add(stmt);
        }

        Match(TokenType.CloseBrace, "Expected '}' to close block.");
        return new BlockStatement(statements, openBrace.Span);
    }

    private StatementNode? ParseStatement()
    {
        // 1. Variable declaration: let [mut] name [: type] = expr;
        if (Check(TokenType.Let))
        {
            return ParseVariableDeclaration();
        }

        // 2. Return statement: return [expr];
        if (Check(TokenType.Return))
        {
            var returnTok = Advance();
            ExpressionNode? val = null;
            if (!Check(TokenType.Semicolon))
            {
                val = ParseExpression();
            }
            Match(TokenType.Semicolon, "Expected ';' after return statement.");
            return new ReturnStatement(val, returnTok.Span);
        }

        // 3. If statement: if condition { ... } else { ... }
        if (Check(TokenType.If))
        {
            return ParseIfStatement();
        }

        // 4. While statement: while condition { ... }
        if (Check(TokenType.While))
        {
            return ParseWhileStatement();
        }

        // 4.1 For statement: for i in start..end { ... }
        if (Check(TokenType.For))
        {
            return ParseForStatement();
        }

        // 4.2 Match statement: match expr { pat => { ... } }
        if (Check(TokenType.Match))
        {
            return ParseMatchStatement();
        }

        // 4.3 Break statement: break;
        if (Check(TokenType.Break))
        {
            var breakTok = Advance();
            Match(TokenType.Semicolon, "Expected ';' after 'break'.");
            return new BreakStatementNode(breakTok.Span);
        }

        // 4.4 Continue statement: continue;
        if (Check(TokenType.Continue))
        {
            var contTok = Advance();
            Match(TokenType.Semicolon, "Expected ';' after 'continue'.");
            return new ContinueStatementNode(contTok.Span);
        }

        // 5. Assignment or Expression statement
        var expr = ParseExpression();
        if (Current.Type is TokenType.Equal or TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual or TokenType.SlashEqual)
        {
            var opToken = Advance();
            var op = opToken.Type switch
            {
                TokenType.PlusEqual => AssignmentOperator.PlusAssign,
                TokenType.MinusEqual => AssignmentOperator.MinusAssign,
                TokenType.StarEqual => AssignmentOperator.MulAssign,
                TokenType.SlashEqual => AssignmentOperator.DivAssign,
                _ => AssignmentOperator.Assign
            };

            var value = ParseExpression();
            Match(TokenType.Semicolon, "Expected ';' after assignment.");

            if (expr is IdentifierExpression id)
            {
                return new AssignmentStatement(id.Name, null, null, op, value, id.Span);
            }
            if (expr is MemberAccessExpression mem && mem.Target is IdentifierExpression targetId)
            {
                return new AssignmentStatement(targetId.Name, mem.MemberName, null, op, value, mem.Span);
            }
            if (expr is IndexExpression idx && idx.Target is IdentifierExpression arrId)
            {
                return new AssignmentStatement(arrId.Name, null, idx.Index, op, value, idx.Span);
            }
            if (expr is IndexExpression idxMem && idxMem.Target is MemberAccessExpression memAcc && memAcc.Target is IdentifierExpression tid)
            {
                return new AssignmentStatement(tid.Name, memAcc.MemberName, idxMem.Index, op, value, idxMem.Span);
            }

            _diagnostics.ReportError("Invalid assignment target.", expr.Span);
            return new AssignmentStatement("<invalid>", null, null, op, value, expr.Span);
        }

        Match(TokenType.Semicolon, "Expected ';' after expression statement.");
        return new ExpressionStatement(expr, expr.Span);
    }

    private VariableDeclarationStatement ParseVariableDeclaration()
    {
        var letTok = Match(TokenType.Let);
        bool isMut = false;
        if (Check(TokenType.Mut))
        {
            isMut = true;
            Advance();
        }

        var nameTok = Match(TokenType.Identifier, "Expected variable name after 'let'.");
        string? typeName = null;
        if (Check(TokenType.Colon))
        {
            Advance();
            typeName = ParseTypeAnnotation();
        }

        Match(TokenType.Equal, "Expected '=' in variable declaration.");
        var init = ParseExpression();
        Match(TokenType.Semicolon, "Expected ';' after variable declaration.");

        return new VariableDeclarationStatement(nameTok.Text, isMut, typeName, init, letTok.Span);
    }

    private IfStatement ParseIfStatement()
    {
        var ifTok = Match(TokenType.If);
        var cond = ParseExpression();
        var thenBranch = ParseBlockStatement();
        StatementNode? elseBranch = null;

        if (Check(TokenType.Else))
        {
            Advance();
            if (Check(TokenType.If))
            {
                elseBranch = ParseIfStatement();
            }
            else
            {
                elseBranch = ParseBlockStatement();
            }
        }

        return new IfStatement(cond, thenBranch, elseBranch, ifTok.Span);
    }

    private WhileStatement ParseWhileStatement()
    {
        var whileTok = Match(TokenType.While);
        var cond = ParseExpression();
        var body = ParseBlockStatement();
        return new WhileStatement(cond, body, whileTok.Span);
    }

    private StatementNode ParseForStatement()
    {
        var forTok = Match(TokenType.For);
        var varName = Match(TokenType.Identifier, "Expected variable name after 'for'.");
        Match(TokenType.In, "Expected 'in' after variable name in for-loop.");
        var startOrColl = ParseExpression();
        if (Check(TokenType.DotDot))
        {
            Advance(); // ..
            var endExpr = ParseExpression();
            var body = ParseBlockStatement();
            return new ForStatement(varName.Text, startOrColl, endExpr, body, forTok.Span);
        }
        else
        {
            // 'for item in collection { ... }'
            var body = ParseBlockStatement();
            var idxName = $"__idx_{varName.Text}_{forTok.Span.Line}_{forTok.Span.Column}";
            var zeroExpr = new NumberLiteralExpression("0", false, forTok.Span);
            var lenCall = new MethodCallExpression(startOrColl, "len", Array.Empty<ExpressionNode>(), forTok.Span);
            var idxIdent = new IdentifierExpression(idxName, forTok.Span);
            var itemIdxExpr = new IndexExpression(startOrColl, idxIdent, forTok.Span);
            var itemLetStmt = new VariableDeclarationStatement(varName.Text, false, null, itemIdxExpr, forTok.Span);

            var newStmts = new List<StatementNode> { itemLetStmt };
            newStmts.AddRange(body.Statements);
            var newBody = new BlockStatement(newStmts, body.Span);
            return new ForStatement(idxName, zeroExpr, lenCall, newBody, forTok.Span);
        }
    }

    private MatchStatement ParseMatchStatement()
    {
        var matchTok = Match(TokenType.Match);
        var scrutinee = ParseExpression();
        Match(TokenType.OpenBrace, "Expected '{' after match expression.");

        var arms = new List<MatchArm>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var pat = ParseExpression();
            Match(TokenType.FatArrow, "Expected '=>' after match pattern.");
            var body = ParseBlockStatement();
            arms.Add(new MatchArm(pat, body, pat.Span));
            if (Check(TokenType.Comma)) Advance();
        }

        Match(TokenType.CloseBrace, "Expected '}' to close match body.");
        return new MatchStatement(scrutinee, arms, matchTok.Span);
    }
}
