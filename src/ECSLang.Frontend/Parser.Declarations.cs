using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
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
        if (Check(TokenType.Fn) || (Check(TokenType.Identifier) && (Current.Text == "fn" || Current.Text == "closure")))
        {
            bool isClosure = Check(TokenType.Identifier) && Current.Text == "closure";
            Advance();
            Match(TokenType.OpenParen, "Expected '(' after function/closure in type.");
            var pTypes = new List<string>();
            if (!Check(TokenType.CloseParen))
            {
                do
                {
                    pTypes.Add(ParseTypeAnnotation());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            }
            Match(TokenType.CloseParen, "Expected ')' after parameter types in function/closure type.");
            string retType = "void";
            if (Check(TokenType.Colon) || Check(TokenType.Arrow))
            {
                Advance();
                retType = ParseTypeAnnotation();
            }
            return isClosure
                ? $"closure({string.Join(", ", pTypes)}): {retType}"
                : $"fn({string.Join(", ", pTypes)}): {retType}";
        }
        var idTok = Match(TokenType.Identifier, "Expected type name.");
        if ((idTok.Text == "Vec" || idTok.Text == "List") && Check(TokenType.Less))
        {
            Advance(); // <
            var elemType = ParseTypeAnnotation();
            Match(TokenType.Greater, "Expected '>' to close generic type.");
            return $"[{elemType}]";
        }
        if (idTok.Text == "Option" && Check(TokenType.Less))
        {
            Advance(); // <
            var innerType = ParseTypeAnnotation();
            Match(TokenType.Greater, "Expected '>' to close Option generic type.");
            return $"Option<{innerType}>";
        }
        if (idTok.Text == "Result" && Check(TokenType.Less))
        {
            Advance(); // <
            var okType = ParseTypeAnnotation();
            Match(TokenType.Comma, "Expected ',' between Result Ok and Err types.");
            var errType = ParseTypeAnnotation();
            Match(TokenType.Greater, "Expected '>' to close Result generic type.");
            return $"Result<{okType}, {errType}>";
        }
        if ((idTok.Text == "HashMap" || idTok.Text == "Map") && Check(TokenType.Less))
        {
            Advance(); // <
            var keyType = ParseTypeAnnotation();
            Match(TokenType.Comma, "Expected ',' between Map key and value types.");
            var valType = ParseTypeAnnotation();
            Match(TokenType.Greater, "Expected '>' to close generic type.");
            return $"Map<{keyType}, {valType}>";
        }
        if (Check(TokenType.Less))
        {
            Advance(); // <
            var typeArgs = new List<string>();
            do
            {
                typeArgs.Add(ParseTypeAnnotation());
            } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
            Match(TokenType.Greater, "Expected '>' to close generic type.");
            return $"{idTok.Text}<{string.Join(", ", typeArgs)}>";
        }
        return idTok.Text;
    }

    private IReadOnlyList<TypeParameter>? ParseOptionalTypeParameters()
    {
        if (!Check(TokenType.Less))
            return null;

        Advance(); // <
        var typeParams = new List<TypeParameter>();
        do
        {
            var nameTok = Match(TokenType.Identifier, "Expected type parameter name.");
            string? constraint = null;
            if (Check(TokenType.Colon))
            {
                Advance(); // :
                var traitTok = Match(TokenType.Identifier, "Expected trait name after ':'.");
                constraint = traitTok.Text;
            }
            typeParams.Add(new TypeParameter(nameTok.Text, constraint, nameTok.Span));
        } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);

        Match(TokenType.Greater, "Expected '>' after type parameters.");
        return typeParams;
    }

    private ComponentDeclaration ParseComponentDeclaration()
    {
        var compTok = Match(TokenType.Component);
        var nameTok = Match(TokenType.Identifier, "Expected component name after 'component'.");
        var typeParams = ParseOptionalTypeParameters();
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
        return new ComponentDeclaration(nameTok.Text, fields, compTok.Span, typeParams);
    }

    private StructDeclaration ParseStructDeclaration()
    {
        var structTok = Match(TokenType.Struct);
        var nameTok = Match(TokenType.Identifier, "Expected struct name after 'struct'.");
        var typeParams = ParseOptionalTypeParameters();
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
        return new StructDeclaration(nameTok.Text, fields, structTok.Span, typeParams);
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
        var typeParams = ParseOptionalTypeParameters();
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
                        var paramType = ParseTypeAnnotation();

                        queryParams.Add(new QueryParameter(isMut, paramName.Text, paramType, paramName.Span));
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
                    var paramType = ParseTypeAnnotation();

                    readParams.Add(new QueryParameter(isMut, paramName.Text, paramType, paramName.Span));
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

        return new SystemDeclaration(nameTok.Text, queryParams, readParams, filters, body, sysTok.Span, typeParams);
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
                        actions.Add(ParseParallelAction());
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
                        var sNameTok = Advance();
                        var sFullName = sNameTok.Text;
                        if (Check(TokenType.Less))
                        {
                            Advance(); // <
                            var typeArgs = new List<string>();
                            do
                            {
                                typeArgs.Add(ParseTypeAnnotation());
                            } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                            Match(TokenType.Greater, "Expected '>' to close generic system type.");
                            sFullName = $"{sFullName}<{string.Join(", ", typeArgs)}>";
                        }
                        Match(TokenType.Semicolon, "Expected ';' after system name.");
                        actions.Add(new SystemCallAction(sFullName, sNameTok.Span));
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
        var typeParams = ParseOptionalTypeParameters();

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
        return new FunctionDeclaration(nameToken.Text, parameters, returnType, body, fnKeyword.Span, typeParams);
    }

    private ImplDeclaration ParseImplDeclaration()
    {
        var implKeyword = Match(TokenType.Impl);
        var typeParams = ParseOptionalTypeParameters();
        var firstIdent = Match(TokenType.Identifier, "Expected identifier after 'impl'.");

        string? traitName = null;
        string structName;

        if (Check(TokenType.For))
        {
            Advance(); // for
            traitName = firstIdent.Text;
            var structNameTok = Match(TokenType.Identifier, "Expected struct name after 'for'.");
            structName = structNameTok.Text;
            if (Check(TokenType.Less))
            {
                Advance(); // <
                var args = new List<string>();
                do
                {
                    args.Add(ParseTypeAnnotation());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                Match(TokenType.Greater, "Expected '>' to close struct type parameters.");
                structName = $"{structName}<{string.Join(", ", args)}>";
            }
        }
        else
        {
            structName = firstIdent.Text;
            if (Check(TokenType.Less))
            {
                Advance(); // <
                var args = new List<string>();
                do
                {
                    args.Add(ParseTypeAnnotation());
                } while (Check(TokenType.Comma) && Advance().Type == TokenType.Comma);
                Match(TokenType.Greater, "Expected '>' to close struct type parameters.");
                structName = $"{structName}<{string.Join(", ", args)}>";
            }
        }

        Match(TokenType.OpenBrace, "Expected '{' to start 'impl' block.");

        var methods = new List<FunctionDeclaration>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Fn))
            {
                methods.Add(ParseFunctionDeclaration(structName));
            }
            else
            {
                _diagnostics.ReportError($"Unexpected token '{Current.Text}' in 'impl' block. Only methods ('fn') are allowed.", Current.Span);
                Advance();
            }
        }

        Match(TokenType.CloseBrace, "Expected '}' to close 'impl' block.");
        return new ImplDeclaration(structName, methods, implKeyword.Span, traitName, typeParams);
    }

    private TraitDeclaration ParseTraitDeclaration()
    {
        var traitKeyword = Match(TokenType.Trait);
        var nameToken = Match(TokenType.Identifier, "Expected trait name after 'trait'.");
        Match(TokenType.OpenBrace, "Expected '{' to start 'trait' body.");

        var methods = new List<TraitMethodDeclaration>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Fn))
            {
                var fnKeyword = Advance(); // fn
                var methodName = Match(TokenType.Identifier, "Expected method name after 'fn'.");
                Match(TokenType.OpenParen, "Expected '(' after method name.");
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
                        if (paramName.Text == "self" && !Check(TokenType.Colon))
                        {
                            paramType = "Self";
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

                Match(TokenType.Semicolon, "Expected ';' after trait method declaration.");
                methods.Add(new TraitMethodDeclaration(methodName.Text, parameters, returnType, fnKeyword.Span));
            }
            else
            {
                _diagnostics.ReportError($"Unexpected token '{Current.Text}' in trait declaration.", Current.Span);
                Advance();
            }
        }

        Match(TokenType.CloseBrace, "Expected '}' to close 'trait' body.");
        return new TraitDeclaration(nameToken.Text, methods, traitKeyword.Span);
    }

    private ConstDeclaration ParseConstDeclaration()
    {
        var startTok = Match(TokenType.Const);
        var nameTok = Match(TokenType.Identifier, "Expected constant name after 'const'.");
        string typeName = "";
        if (Check(TokenType.Colon))
        {
            Advance();
            typeName = ParseTypeAnnotation();
        }
        Match(TokenType.Equal, "Expected '=' after constant name or type in const declaration.");
        var initializer = ParseExpression();
        Match(TokenType.Semicolon, "Expected ';' after const declaration.");
        return new ConstDeclaration(nameTok.Text, typeName, initializer, startTok.Span);
    }
}
