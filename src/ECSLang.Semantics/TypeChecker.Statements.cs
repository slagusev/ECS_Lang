using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private void CheckBlock(BlockStatement block, IEnumerable<VariableSymbol>? extraSymbols = null)
    {
        var blockScope = new Scope(_currentScope);
        _currentScope = blockScope;

        if (extraSymbols != null)
        {
            foreach (var sym in extraSymbols)
            {
                _currentScope.TryDeclare(sym);
            }
        }

        foreach (var stmt in block.Statements)
        {
            CheckStatement(stmt);
        }

        _currentScope = _currentScope.Parent!;
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case VariableDeclarationStatement varDecl:
                CheckVariableDeclaration(varDecl);
                break;
            case AssignmentStatement assign:
                CheckAssignment(assign);
                break;
            case IfStatement ifStmt:
                CheckIfStatement(ifStmt);
                break;
            case WhileStatement whileStmt:
                CheckWhileStatement(whileStmt);
                break;
            case ForStatement forStmt:
                CheckForStatement(forStmt);
                break;
            case ReturnStatement retStmt:
                if (retStmt.Value != null)
                {
                    var rType = CheckExpression(retStmt.Value);
                    if (_currentExpectedReturnType != null)
                    {
                        if (_currentExpectedReturnType.IsOption && (rType.Name == "None" || retStmt.Value is IdentifierExpression { Name: "None" } || retStmt.Value is CallExpression { Callee: "None" }))
                        {
                            _nodeTypes[retStmt.Value] = _currentExpectedReturnType;
                        }
                        else if (_currentExpectedReturnType.IsResult && (retStmt.Value is CallExpression { Callee: "Ok" } || retStmt.Value is CallExpression { Callee: "Err" }))
                        {
                            _nodeTypes[retStmt.Value] = _currentExpectedReturnType;
                        }
                    }
                }
                break;
            case ExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression);
                break;
            case MatchStatement matchStmt:
                CheckMatchStatement(matchStmt);
                break;
            case BlockStatement block:
                CheckBlock(block);
                break;
        }
    }

    private void CheckVariableDeclaration(VariableDeclarationStatement varDecl)
    {
        var initType = CheckExpression(varDecl.Initializer);

        TypeSymbol explicitType = varDecl.TypeName != null
            ? EnsureMonomorphizedType(varDecl.TypeName, varDecl.Span)
            : initType;

        if (explicitType.IsDynamicArray && varDecl.Initializer is ArrayLiteralExpression arrLit)
        {
            explicitType.TryGetDynamicArrayElement(out var expectedElem);
            foreach (var el in arrLit.Elements)
            {
                var elType = CheckExpression(el);
                if (expectedElem != TypeSymbol.Unknown && elType != TypeSymbol.Unknown && !AreTypesCompatible(expectedElem, elType))
                {
                    _diagnostics.ReportError($"Array element of type '{elType.Name}' is not compatible with '{expectedElem.Name}'.", el.Span);
                }
            }
            _nodeTypes[arrLit] = explicitType;
            initType = explicitType;
        }

        if (explicitType.IsOption && (initType.Name == "None" || varDecl.Initializer is IdentifierExpression { Name: "None" } || varDecl.Initializer is CallExpression { Callee: "None" }))
        {
            _nodeTypes[varDecl.Initializer] = explicitType;
            initType = explicitType;
        }
        else if (explicitType.IsResult && (varDecl.Initializer is CallExpression { Callee: "Ok" } || varDecl.Initializer is CallExpression { Callee: "Err" }))
        {
            _nodeTypes[varDecl.Initializer] = explicitType;
            initType = explicitType;
        }

        if (varDecl.TypeName != null && initType != TypeSymbol.Unknown && !AreTypesCompatible(explicitType, initType))
        {
            _diagnostics.ReportError(
                $"Cannot initialize variable of type '{explicitType.Name}' with value of type '{initType.Name}'.",
                varDecl.Initializer.Span);
        }

        var sym = new VariableSymbol(varDecl.Name, explicitType, varDecl.IsMutable, varDecl.Span);
        if (!_currentScope.TryDeclare(sym))
        {
            _diagnostics.ReportError($"Variable '{varDecl.Name}' is already declared in this scope.", varDecl.Span);
        }
    }

    private void CheckAssignment(AssignmentStatement assign)
    {
        var sym = _currentScope.Lookup(assign.TargetName);
        if (sym == null)
        {
            _diagnostics.ReportError($"Undefined variable '{assign.TargetName}'.", assign.Span);
            return;
        }

        if (!sym.IsMutable)
        {
            _diagnostics.ReportError($"Cannot assign to immutable variable '{assign.TargetName}'. Use 'mut' to make it mutable.", assign.Span);
        }

        TypeSymbol expectedType = sym.Type;

        // If member assignment: target.member = expr
        if (assign.MemberName != null)
        {
            expectedType = GetMemberType(sym.Type, assign.MemberName, assign.Span);
        }

        // If indexed assignment: target[index] = expr OR target.member[index] = expr
        if (assign.Index != null)
        {
            var indexType = CheckExpression(assign.Index);

            if (expectedType.TryGetMapInfo(out var mapKeyType, out var mapValType))
            {
                if (!AreTypesCompatible(mapKeyType, indexType))
                {
                    _diagnostics.ReportError($"Map key of type '{expectedType.Name}' expects '{mapKeyType.Name}', but got '{indexType.Name}'.", assign.Index.Span);
                }
                expectedType = mapValType;
            }
            else
            {
                if (!indexType.IsInteger)
                {
                    _diagnostics.ReportError("Array index must be an integer.", assign.Index.Span);
                }

                if (expectedType.TryGetArrayInfo(out var elemType, out _))
                {
                    expectedType = elemType;
                }
                else if (expectedType.TryGetDynamicArrayElement(out var dynElem))
                {
                    expectedType = dynElem;
                }
                else
                {
                    _diagnostics.ReportError($"Type '{expectedType.Name}' is not indexable.", assign.Span);
                }
            }
        }

        var valType = CheckExpression(assign.Value);
        if (expectedType.IsOption && (valType.Name == "None" || assign.Value is IdentifierExpression { Name: "None" } || assign.Value is CallExpression { Callee: "None" }))
        {
            _nodeTypes[assign.Value] = expectedType;
            valType = expectedType;
        }
        else if (expectedType.IsResult && (assign.Value is CallExpression { Callee: "Ok" } || assign.Value is CallExpression { Callee: "Err" }))
        {
            _nodeTypes[assign.Value] = expectedType;
            valType = expectedType;
        }

        if (expectedType != TypeSymbol.Unknown && valType != TypeSymbol.Unknown && !AreTypesCompatible(expectedType, valType))
        {
            _diagnostics.ReportError($"Cannot assign value of type '{valType.Name}' to '{assign.TargetName}{(assign.MemberName != null ? "." + assign.MemberName : "")}' of type '{expectedType.Name}'.", assign.Value.Span);
        }
    }

    private void CheckIfStatement(IfStatement ifStmt)
    {
        var condType = CheckExpression(ifStmt.Condition);
        if (condType != TypeSymbol.Bool && condType != TypeSymbol.Unknown)
        {
            _diagnostics.ReportError($"Condition in 'if' statement must be of type 'bool', got '{condType.Name}'.", ifStmt.Condition.Span);
        }

        CheckBlock(ifStmt.ThenBranch);
        if (ifStmt.ElseBranch != null)
        {
            CheckStatement(ifStmt.ElseBranch);
        }
    }

    private void CheckWhileStatement(WhileStatement whileStmt)
    {
        var condType = CheckExpression(whileStmt.Condition);
        if (condType != TypeSymbol.Bool && condType != TypeSymbol.Unknown)
        {
            _diagnostics.ReportError($"Condition in 'while' statement must be of type 'bool', got '{condType.Name}'.", whileStmt.Condition.Span);
        }

        CheckBlock(whileStmt.Body);
    }

    private void CheckForStatement(ForStatement forStmt)
    {
        var startType = CheckExpression(forStmt.Start);
        var endType = CheckExpression(forStmt.End);

        if (!startType.IsInteger || !endType.IsInteger)
        {
            _diagnostics.ReportError("Range bounds in 'for' loop must be integers.", forStmt.Span);
        }

        var loopScope = new Scope(_currentScope);
        _currentScope = loopScope;

        var loopVar = new VariableSymbol(forStmt.VariableName, TypeSymbol.I32, IsMutable: false, forStmt.Span);
        _currentScope.TryDeclare(loopVar);

        CheckBlock(forStmt.Body);

        _currentScope = _currentScope.Parent!;
    }

    private void CheckMatchStatement(MatchStatement match)
    {
        var sType = CheckExpression(match.Scrutinee);
        bool isEnum = _enums.ContainsKey(sType.Name);
        if (!sType.IsInteger && !isEnum && !sType.IsOption && !sType.IsResult && sType != TypeSymbol.Unknown)
        {
            _diagnostics.ReportError($"Match expression must be an integer, enum, Option or Result type, got '{sType.Name}'.", match.Scrutinee.Span);
        }

        bool hasSomeArm = false;
        bool hasNoneArm = false;
        bool hasOkArm = false;
        bool hasErrArm = false;
        bool hasWildcard = false;

        foreach (var arm in match.Arms)
        {
            if (arm.Pattern is WildcardExpression)
            {
                hasWildcard = true;
                CheckBlock(arm.Body);
                continue;
            }

            if (sType.IsOption)
            {
                sType.TryGetOptionInfo(out var optElem);
                if (arm.Pattern is IdentifierExpression { Name: "None" } || arm.Pattern is CallExpression { Callee: "None" } ||
                    (arm.Pattern is IdentifierExpression idN && idN.Name.EndsWith("::None")))
                {
                    hasNoneArm = true;
                    CheckBlock(arm.Body);
                }
                else if (arm.Pattern is CallExpression callSome && (callSome.Callee == "Some" || callSome.Callee.EndsWith("::Some")) && callSome.Arguments.Count == 1 && callSome.Arguments[0] is IdentifierExpression bindId)
                {
                    hasSomeArm = true;
                    var bindSym = new VariableSymbol(bindId.Name, optElem, IsMutable: false, bindId.Span);
                    CheckBlock(arm.Body, new[] { bindSym });
                }
                else
                {
                    _diagnostics.ReportError($"Invalid pattern for Option: expected 'Some(x)' or 'None'.", arm.Pattern.Span);
                    CheckBlock(arm.Body);
                }
            }
            else if (sType.IsResult)
            {
                sType.TryGetResultInfo(out var okType, out var errType);
                if (arm.Pattern is CallExpression callOk && (callOk.Callee == "Ok" || callOk.Callee.EndsWith("::Ok")) && callOk.Arguments.Count == 1 && callOk.Arguments[0] is IdentifierExpression bindOk)
                {
                    hasOkArm = true;
                    var bindSym = new VariableSymbol(bindOk.Name, okType, IsMutable: false, bindOk.Span);
                    CheckBlock(arm.Body, new[] { bindSym });
                }
                else if (arm.Pattern is CallExpression callErr && (callErr.Callee == "Err" || callErr.Callee.EndsWith("::Err")) && callErr.Arguments.Count == 1 && callErr.Arguments[0] is IdentifierExpression bindErr)
                {
                    hasErrArm = true;
                    var bindSym = new VariableSymbol(bindErr.Name, errType, IsMutable: false, bindErr.Span);
                    CheckBlock(arm.Body, new[] { bindSym });
                }
                else
                {
                    _diagnostics.ReportError($"Invalid pattern for Result: expected 'Ok(x)' or 'Err(e)'.", arm.Pattern.Span);
                    CheckBlock(arm.Body);
                }
            }
            else
            {
                var patType = CheckExpression(arm.Pattern);
                if (isEnum)
                {
                    if (patType != TypeSymbol.Unknown && patType.Name != sType.Name)
                    {
                        _diagnostics.ReportError($"Pattern type '{patType.Name}' does not match enum type '{sType.Name}'.", arm.Pattern.Span);
                    }
                }
                else
                {
                    if (patType != TypeSymbol.Unknown && !patType.IsInteger)
                    {
                        _diagnostics.ReportError($"Pattern type '{patType.Name}' is not a valid integer pattern.", arm.Pattern.Span);
                    }
                }
                CheckBlock(arm.Body);
            }
        }

        if (sType.IsOption && !hasWildcard)
        {
            if (!hasSomeArm || !hasNoneArm)
            {
                _diagnostics.ReportWarning($"Match on Option<{sType.Name}> is non-exhaustive. Missing {(!hasSomeArm ? "'Some'" : "")}{(!hasSomeArm && !hasNoneArm ? " and " : "")}{(!hasNoneArm ? "'None'" : "")}.", match.Span);
            }
        }
        else if (sType.IsResult && !hasWildcard)
        {
            if (!hasOkArm || !hasErrArm)
            {
                _diagnostics.ReportWarning($"Match on Result<{sType.Name}> is non-exhaustive. Missing {(!hasOkArm ? "'Ok'" : "")}{(!hasOkArm && !hasErrArm ? " and " : "")}{(!hasErrArm ? "'Err'" : "")}.", match.Span);
            }
        }
    }
}
