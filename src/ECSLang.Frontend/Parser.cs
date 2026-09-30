using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly DiagnosticsBag _diagnostics;
    private int _index;

    public Parser(IReadOnlyList<Token> tokens, DiagnosticsBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    private Token Current => _index < _tokens.Count ? _tokens[_index] : _tokens[^1];
    private Token Peek(int offset) => (_index + offset < _tokens.Count) ? _tokens[_index + offset] : _tokens[^1];

    private Token Advance()
    {
        var token = Current;
        if (_index < _tokens.Count - 1)
            _index++;
        return token;
    }

    private Token Match(TokenType type, string? errorMessage = null)
    {
        if (Current.Type == type)
            return Advance();

        _diagnostics.ReportError(
            errorMessage ?? $"Expected token '{type}' but found '{Current.Type}' ('{Current.Text}').",
            Current.Span);
        return Current;
    }

    private bool Check(TokenType type) => Current.Type == type;

    public ProgramNode ParseProgram()
    {
        var declarations = new List<DeclarationNode>();
        var startSpan = Current.Span;

        while (!Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Fn))
            {
                declarations.Add(ParseFunctionDeclaration());
            }
            else if (Check(TokenType.Component))
            {
                declarations.Add(ParseComponentDeclaration());
            }
            else if (Check(TokenType.Struct))
            {
                declarations.Add(ParseStructDeclaration());
            }
            else if (Check(TokenType.Resource))
            {
                declarations.Add(ParseResourceDeclaration());
            }
            else if (Check(TokenType.Event))
            {
                declarations.Add(ParseEventDeclaration());
            }
            else if (Check(TokenType.Enum))
            {
                declarations.Add(ParseEnumDeclaration());
            }
            else if (Check(TokenType.System))
            {
                declarations.Add(ParseSystemDeclaration());
            }
            else if (Check(TokenType.Pipeline))
            {
                declarations.Add(ParsePipelineDeclaration());
            }
            else if (Check(TokenType.Import))
            {
                declarations.Add(ParseImportDirective());
            }
            else if (Check(TokenType.Impl))
            {
                declarations.Add(ParseImplDeclaration());
            }
            else
            {
                _diagnostics.ReportError($"Unexpected token '{Current.Text}' at file root.", Current.Span);
                Advance();
            }
        }

        return new ProgramNode(declarations, startSpan);
    }

    private ImportDirective ParseImportDirective()
    {
        var importTok = Match(TokenType.Import);
        string path;

        if (Check(TokenType.StringLiteral))
        {
            var strTok = Match(TokenType.StringLiteral);
            path = strTok.Text;
        }
        else if (Check(TokenType.Identifier))
        {
            var idTok = Match(TokenType.Identifier);
            path = idTok.Text + ".ecs";
        }
        else
        {
            _diagnostics.ReportError("Expected string literal (module path) or module identifier after 'import'.", Current.Span);
            path = string.Empty;
        }

        Match(TokenType.Semicolon, "Expected ';' after import directive.");
        return new ImportDirective(path, importTok.Span);
    }

    private string ParseTypeAnnotation()
    {
        if (Check(TokenType.OpenBracket))
        {
            Advance(); // [
            var elemType = ParseTypeAnnotation();
            if (Check(TokenType.Semicolon))
            {
                Advance(); // ;
                var lenTok = Match(TokenType.NumberLiteral, "Expected array length number.");
                Match(TokenType.CloseBracket, "Expected ']' to close array type.");
                return $"[{elemType}; {lenTok.Text}]";
            }
            Match(TokenType.CloseBracket, "Expected ']' to close dynamic array type.");
            return $"[{elemType}]";
        }
        var idTok = Match(TokenType.Identifier, "Expected type name.");
        if ((idTok.Text == "Vec" || idTok.Text == "List") && Check(TokenType.Less))
        {
            Advance(); // <
            var elemType = ParseTypeAnnotation();
            Match(TokenType.Greater, "Expected '>' to close generic type.");
            return $"[{elemType}]";
        }
        return idTok.Text;
    }

    private ComponentDeclaration ParseComponentDeclaration()
    {
        var compTok = Match(TokenType.Component);
        var nameTok = Match(TokenType.Identifier, "Expected component name after 'component'.");
        Match(TokenType.OpenBrace, "Expected '{' to start component body.");

        var fields = new List<FieldDefinition>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var fieldName = Match(TokenType.Identifier, "Expected field name.");
            Match(TokenType.Colon, "Expected ':' after field name.");
            var fieldType = ParseTypeAnnotation();
            if (Check(TokenType.Comma)) Advance();
            else if (Check(TokenType.Semicolon)) Advance();

            fields.Add(new FieldDefinition(fieldName.Text, fieldType, fieldName.Span));
        }

        Match(TokenType.CloseBrace, "Expected '}' to close component body.");
        return new ComponentDeclaration(nameTok.Text, fields, compTok.Span);
    }

    private StructDeclaration ParseStructDeclaration()
    {
        var structTok = Match(TokenType.Struct);
        var nameTok = Match(TokenType.Identifier, "Expected struct name after 'struct'.");
        Match(TokenType.OpenBrace, "Expected '{' to start struct body.");

        var fields = new List<FieldDefinition>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var fieldName = Match(TokenType.Identifier, "Expected field name.");
            Match(TokenType.Colon, "Expected ':' after field name.");
            var fieldType = ParseTypeAnnotation();
            if (Check(TokenType.Comma)) Advance();
            else if (Check(TokenType.Semicolon)) Advance();

            fields.Add(new FieldDefinition(fieldName.Text, fieldType, fieldName.Span));
        }

        Match(TokenType.CloseBrace, "Expected '}' to close struct body.");
        return new StructDeclaration(nameTok.Text, fields, structTok.Span);
    }

    private ResourceDeclaration ParseResourceDeclaration()
    {
        var resTok = Match(TokenType.Resource);
        var nameTok = Match(TokenType.Identifier, "Expected resource name after 'resource'.");
        Match(TokenType.OpenBrace, "Expected '{' to start resource body.");

        var fields = new List<FieldDefinition>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var fieldName = Match(TokenType.Identifier, "Expected field name.");
            Match(TokenType.Colon, "Expected ':' after field name.");
            var fieldType = ParseTypeAnnotation();
            if (Check(TokenType.Comma)) Advance();
            else if (Check(TokenType.Semicolon)) Advance();

            fields.Add(new FieldDefinition(fieldName.Text, fieldType, fieldName.Span));
        }

        Match(TokenType.CloseBrace, "Expected '}' to close resource body.");
        return new ResourceDeclaration(nameTok.Text, fields, resTok.Span);
    }

    private EventDeclaration ParseEventDeclaration()
    {
        var evTok = Match(TokenType.Event);
        var nameTok = Match(TokenType.Identifier, "Expected event name after 'event'.");
        Match(TokenType.OpenBrace, "Expected '{' to start event body.");

        var fields = new List<FieldDefinition>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var fieldName = Match(TokenType.Identifier, "Expected field name.");
            Match(TokenType.Colon, "Expected ':' after field name.");
            var fieldType = ParseTypeAnnotation();
            if (Check(TokenType.Comma)) Advance();
            else if (Check(TokenType.Semicolon)) Advance();

            fields.Add(new FieldDefinition(fieldName.Text, fieldType, fieldName.Span));
        }

        Match(TokenType.CloseBrace, "Expected '}' to close event body.");
        return new EventDeclaration(nameTok.Text, fields, evTok.Span);
    }

    private EnumDeclaration ParseEnumDeclaration()
    {
        var enumTok = Match(TokenType.Enum);
        var nameTok = Match(TokenType.Identifier, "Expected enum name after 'enum'.");
        Match(TokenType.OpenBrace, "Expected '{' to start enum body.");

        var members = new List<EnumMemberDefinition>();
        int autoVal = 0;
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var memName = Match(TokenType.Identifier, "Expected enum member name.");
            int? val = null;
            if (Check(TokenType.Equal))
            {
                Advance(); // =
                var valTok = Match(TokenType.NumberLiteral, "Expected integer value after '='.");
                if (int.TryParse(valTok.Text, out int parsedVal))
                {
                    val = parsedVal;
                    autoVal = parsedVal + 1;
                }
                else
                {
                    _diagnostics.ReportError("Invalid integer value for enum member.", valTok.Span);
                }
            }
            else
            {
                val = autoVal++;
            }

            if (Check(TokenType.Comma)) Advance();
            else if (Check(TokenType.Semicolon)) Advance();

            members.Add(new EnumMemberDefinition(memName.Text, val, memName.Span));
        }

        Match(TokenType.CloseBrace, "Expected '}' to close enum body.");
        return new EnumDeclaration(nameTok.Text, members, enumTok.Span);
    }

    private SystemDeclaration ParseSystemDeclaration()
    {
        var sysTok = Match(TokenType.System);
        var nameTok = Match(TokenType.Identifier, "Expected system name after 'system'.");
        Match(TokenType.OpenBrace, "Expected '{' to start system body.");

        var queryParams = new List<QueryParameter>();
        var readParams = new List<QueryParameter>();
        var filters = new List<QueryFilter>();

        if (Check(TokenType.Query))
        {
            Match(TokenType.Query);
            Match(TokenType.OpenParen, "Expected '(' after 'query'.");
            if (!Check(TokenType.CloseParen))
            {
                do
                {
                    if (Check(TokenType.Without) || (Check(TokenType.Identifier) && Current.Text == "without"))
                    {
                        var tok = Advance();
                        Match(TokenType.Colon, "Expected ':' after 'without'.");
                        var compName = Match(TokenType.Identifier, "Expected component name after 'without:'.");
                        filters.Add(new QueryFilter(QueryFilterKind.Without, compName.Text, tok.Span));
                    }
                    else if (Check(TokenType.With) || (Check(TokenType.Identifier) && Current.Text == "with"))
                    {
                        var tok = Advance();
                        Match(TokenType.Colon, "Expected ':' after 'with'.");
                        var compName = Match(TokenType.Identifier, "Expected component name after 'with:'.");
                        filters.Add(new QueryFilter(QueryFilterKind.With, compName.Text, tok.Span));
                    }
                    else
                    {
                        bool isMut = false;
                        if (Check(TokenType.Mut))
                        {
                            isMut = true;
                            Advance();
                        }

                        var paramName = Match(TokenType.Identifier, "Expected query parameter name.");
                        Match(TokenType.Colon, "Expected ':' after query parameter name.");
                        var paramType = Match(TokenType.Identifier, "Expected component or resource type name.");

                        queryParams.Add(new QueryParameter(isMut, paramName.Text, paramType.Text, paramName.Span));
                    }
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            }
            Match(TokenType.CloseParen, "Expected ')' after query parameters.");
        }
        else if (Check(TokenType.Read))
        {
            Match(TokenType.Read);
            Match(TokenType.OpenParen, "Expected '(' after 'read'.");
            if (!Check(TokenType.CloseParen))
            {
                do
                {
                    bool isMut = false;
                    if (Check(TokenType.Mut))
                    {
                        isMut = true;
                        Advance();
                    }

                    var paramName = Match(TokenType.Identifier, "Expected read parameter name.");
                    Match(TokenType.Colon, "Expected ':' after read parameter name.");
                    var paramType = Match(TokenType.Identifier, "Expected event or resource type name.");

                    readParams.Add(new QueryParameter(isMut, paramName.Text, paramType.Text, paramName.Span));
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            }
            Match(TokenType.CloseParen, "Expected ')' after read parameters.");
        }
        else
        {
            _diagnostics.ReportError("Expected 'query' or 'read' inside system declaration.", Current.Span);
        }

        var body = ParseBlockStatement();
        Match(TokenType.CloseBrace, "Expected '}' to close system.");

        return new SystemDeclaration(nameTok.Text, queryParams, readParams, filters, body, sysTok.Span);
    }

    private PipelineDeclaration ParsePipelineDeclaration()
    {
        var pipeTok = Match(TokenType.Pipeline);
        var nameTok = Match(TokenType.Identifier, "Expected pipeline name after 'pipeline'.");
        Match(TokenType.OpenBrace, "Expected '{' to start pipeline body.");

        var stages = new List<StageDefinition>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Stage))
            {
                var stageTok = Advance();
                var stageNameTok = Match(TokenType.Identifier, "Expected stage name.");
                Match(TokenType.OpenBrace, "Expected '{' to start stage body.");

                var actions = new List<StageAction>();
                while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                {
                    if (Check(TokenType.Parallel))
                    {
                        var parTok = Advance();
                        Match(TokenType.OpenBrace, "Expected '{' after parallel.");
                        var parallelSystems = new List<SystemCallAction>();
                        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                        {
                            var sName = Match(TokenType.Identifier, "Expected system name in parallel block.");
                            Match(TokenType.Semicolon, "Expected ';' after system name.");
                            parallelSystems.Add(new SystemCallAction(sName.Text, sName.Span));
                        }
                        Match(TokenType.CloseBrace, "Expected '}' after parallel block.");
                        actions.Add(new ParallelAction(parallelSystems, parTok.Span));
                    }
                    else if (Check(TokenType.Sync))
                    {
                        var syncTok = Advance();
                        Match(TokenType.Semicolon, "Expected ';' after sync.");
                        actions.Add(new SyncAction(syncTok.Span));
                    }
                    else if (Check(TokenType.SortHierarchy))
                    {
                        var sortTok = Advance();
                        Match(TokenType.Semicolon, "Expected ';' after sort_hierarchy.");
                        actions.Add(new SortHierarchyAction(sortTok.Span));
                    }
                    else if (Check(TokenType.SwapEvents))
                    {
                        var swapTok = Advance();
                        Match(TokenType.Semicolon, "Expected ';' after swap_events.");
                        actions.Add(new SwapEventsAction(swapTok.Span));
                    }
                    else if (Check(TokenType.ApplyCommands))
                    {
                        var appTok = Advance();
                        Match(TokenType.Semicolon, "Expected ';' after apply_commands.");
                        actions.Add(new ApplyCommandsAction(appTok.Span));
                    }
                    else if (Check(TokenType.Identifier))
                    {
                        var sName = Advance();
                        Match(TokenType.Semicolon, "Expected ';' after system name.");
                        actions.Add(new SystemCallAction(sName.Text, sName.Span));
                    }
                    else
                    {
                        _diagnostics.ReportError($"Unexpected token '{Current.Text}' in stage.", Current.Span);
                        Advance();
                    }
                }
                Match(TokenType.CloseBrace, "Expected '}' to close stage.");
                stages.Add(new StageDefinition(stageNameTok.Text, actions, stageTok.Span));
            }
            else
            {
                _diagnostics.ReportError($"Expected 'stage' inside pipeline, got '{Current.Text}'.", Current.Span);
                Advance();
            }
        }

        Match(TokenType.CloseBrace, "Expected '}' to close pipeline.");
        return new PipelineDeclaration(nameTok.Text, stages, pipeTok.Span);
    }

    private FunctionDeclaration ParseFunctionDeclaration(string? enclosingType = null)
    {
        var fnKeyword = Match(TokenType.Fn);
        var nameToken = Match(TokenType.Identifier, "Expected function name after 'fn'.");

        Match(TokenType.OpenParen, "Expected '(' after function name.");
        var parameters = new List<FunctionParameter>();
        if (!Check(TokenType.CloseParen))
        {
            do
            {
                bool isMut = false;
                if (Check(TokenType.Mut))
                {
                    Advance();
                    isMut = true;
                }

                var paramName = Match(TokenType.Identifier, "Expected parameter name.");
                string paramType;
                if (enclosingType != null && paramName.Text == "self" && !Check(TokenType.Colon))
                {
                    paramType = enclosingType;
                }
                else
                {
                    Match(TokenType.Colon, "Expected ':' after parameter name.");
                    paramType = ParseTypeAnnotation();
                }

                parameters.Add(new FunctionParameter(paramName.Text, paramType, paramName.Span, isMut));
            } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
        }
        Match(TokenType.CloseParen, "Expected ')' after parameters.");

        string? returnType = null;
        if (Check(TokenType.Colon))
        {
            Advance();
            returnType = ParseTypeAnnotation();
        }

        var body = ParseBlockStatement();
        return new FunctionDeclaration(nameToken.Text, parameters, returnType, body, fnKeyword.Span);
    }

    private ImplDeclaration ParseImplDeclaration()
    {
        var implKeyword = Match(TokenType.Impl);
        var structNameToken = Match(TokenType.Identifier, "Expected struct name after 'impl'.");
        Match(TokenType.OpenBrace, "Expected '{' to start 'impl' block.");

        var methods = new List<FunctionDeclaration>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Fn))
            {
                methods.Add(ParseFunctionDeclaration(structNameToken.Text));
            }
            else
            {
                _diagnostics.ReportError($"Unexpected token '{Current.Text}' in 'impl' block. Only methods ('fn') are allowed.", Current.Span);
                Advance();
            }
        }

        Match(TokenType.CloseBrace, "Expected '}' to close 'impl' block.");
        return new ImplDeclaration(structNameToken.Text, methods, implKeyword.Span);
    }

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
            else
            {
                break;
            }
        }

        return expr;
    }

    private ExpressionNode ParsePrimaryExpression()
    {
        var token = Current;

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

            if ((identName == "Vec" || identName == "List") && Check(TokenType.Less))
            {
                Advance(); // <
                var elemType = ParseTypeAnnotation();
                Match(TokenType.Greater, "Expected '>' to close generic type.");
                Match(TokenType.OpenParen, "Expected '(' after generic type.");
                var genArgs = new List<ExpressionNode>();
                if (!Check(TokenType.CloseParen))
                {
                    do
                    {
                        genArgs.Add(ParseExpression());
                    } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                }
                Match(TokenType.CloseParen, "Expected ')' after constructor argument list.");
                return new CallExpression($"Vec<{elemType}>", genArgs, token.Span);
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
}
