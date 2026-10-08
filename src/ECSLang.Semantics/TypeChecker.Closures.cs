using System.Collections.Generic;
using System.Linq;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private TypeSymbol CheckLambdaExpression(LambdaExpression lambda)
    {
        var lambdaScope = new Scope(_currentScope);
        var prevScope = _currentScope;
        _currentScope = lambdaScope;

        var paramTypes = new List<TypeSymbol>();
        foreach (var p in lambda.Parameters)
        {
            var pType = p.TypeName != null ? TypeSymbol.FromName(p.TypeName) : TypeSymbol.I32;
            paramTypes.Add(pType);
            var varSym = new VariableSymbol(p.Name, pType, IsMutable: false, p.Span);
            _currentScope.TryDeclare(varSym);
        }

        var prevRetType = _currentExpectedReturnType;
        TypeSymbol expectedRet = lambda.ReturnType != null ? TypeSymbol.FromName(lambda.ReturnType) : TypeSymbol.Unknown;
        _currentExpectedReturnType = expectedRet != TypeSymbol.Unknown ? expectedRet : null;

        CheckBlock(lambda.Body);

        TypeSymbol retType = expectedRet != TypeSymbol.Unknown ? expectedRet : TypeSymbol.Void;
        foreach (var stmt in lambda.Body.Statements)
        {
            if (stmt is ReturnStatement ret && ret.Value != null)
            {
                if (_nodeTypes.TryGetValue(ret.Value, out var valType) && valType != TypeSymbol.Unknown)
                {
                    retType = valType;
                    break;
                }
            }
        }

        _currentExpectedReturnType = prevRetType;

        // Detect captured variables
        var captures = new HashSet<string>();
        FindCaptures(lambda.Body, lambdaScope, captures);
        lambda.Captures.Clear();
        lambda.Captures.AddRange(captures);

        _currentScope = prevScope;

        var funcType = TypeSymbol.CreateFunction(paramTypes, retType);
        _nodeTypes[lambda] = funcType;
        return funcType;
    }

    private TypeSymbol CheckIndirectCallExpression(IndirectCallExpression indCall)
    {
        var calleeType = CheckExpression(indCall.Callee);
        var argTypes = indCall.Arguments.Select(CheckExpression).ToList();

        if (calleeType.TryGetFunctionInfo(out var paramTypes, out var retType))
        {
            if (paramTypes.Count != argTypes.Count)
            {
                _diagnostics.ReportError($"Function/closure expects {paramTypes.Count} argument(s), got {argTypes.Count}.", indCall.Span);
            }
            else
            {
                for (int i = 0; i < paramTypes.Count; i++)
                {
                    if (!AreTypesCompatible(paramTypes[i], argTypes[i]))
                    {
                        _diagnostics.ReportError($"Argument {i + 1} expects '{paramTypes[i].Name}', got '{argTypes[i].Name}'.", indCall.Arguments[i].Span);
                    }
                }
            }
            return retType;
        }

        _diagnostics.ReportError($"Expression of type '{calleeType.Name}' is not callable.", indCall.Span);
        return TypeSymbol.Unknown;
    }

    private void FindCaptures(AstNode node, Scope lambdaScope, HashSet<string> captures)
    {
        switch (node)
        {
            case BlockStatement block:
                foreach (var stmt in block.Statements) FindCaptures(stmt, lambdaScope, captures);
                break;
            case VariableDeclarationStatement varDecl:
                FindCaptures(varDecl.Initializer, lambdaScope, captures);
                break;
            case AssignmentStatement assign:
                CheckCaptureCandidate(assign.TargetName, lambdaScope, captures);
                if (assign.Index != null) FindCaptures(assign.Index, lambdaScope, captures);
                FindCaptures(assign.Value, lambdaScope, captures);
                break;
            case ExpressionStatement exprStmt:
                FindCaptures(exprStmt.Expression, lambdaScope, captures);
                break;
            case ReturnStatement ret:
                if (ret.Value != null) FindCaptures(ret.Value, lambdaScope, captures);
                break;
            case IfStatement ifStmt:
                FindCaptures(ifStmt.Condition, lambdaScope, captures);
                FindCaptures(ifStmt.ThenBranch, lambdaScope, captures);
                if (ifStmt.ElseBranch != null) FindCaptures(ifStmt.ElseBranch, lambdaScope, captures);
                break;
            case WhileStatement whileStmt:
                FindCaptures(whileStmt.Condition, lambdaScope, captures);
                FindCaptures(whileStmt.Body, lambdaScope, captures);
                break;
            case ForStatement forStmt:
                FindCaptures(forStmt.Start, lambdaScope, captures);
                FindCaptures(forStmt.End, lambdaScope, captures);
                FindCaptures(forStmt.Body, lambdaScope, captures);
                break;
            case MatchStatement matchStmt:
                FindCaptures(matchStmt.Scrutinee, lambdaScope, captures);
                foreach (var arm in matchStmt.Arms)
                {
                    FindCaptures(arm.Body, lambdaScope, captures);
                }
                break;
            case BinaryExpression bin:
                FindCaptures(bin.Left, lambdaScope, captures);
                FindCaptures(bin.Right, lambdaScope, captures);
                break;
            case UnaryExpression un:
                FindCaptures(un.Operand, lambdaScope, captures);
                break;
            case CallExpression call:
                CheckCaptureCandidate(call.Callee, lambdaScope, captures);
                foreach (var arg in call.Arguments) FindCaptures(arg, lambdaScope, captures);
                break;
            case MethodCallExpression mCall:
                FindCaptures(mCall.Target, lambdaScope, captures);
                foreach (var arg in mCall.Arguments) FindCaptures(arg, lambdaScope, captures);
                break;
            case MemberAccessExpression mem:
                FindCaptures(mem.Target, lambdaScope, captures);
                break;
            case IndexExpression idx:
                FindCaptures(idx.Target, lambdaScope, captures);
                FindCaptures(idx.Index, lambdaScope, captures);
                break;
            case ArrayLiteralExpression arrLit:
                foreach (var el in arrLit.Elements) FindCaptures(el, lambdaScope, captures);
                break;
            case IdentifierExpression id:
                CheckCaptureCandidate(id.Name, lambdaScope, captures);
                break;
            case IndirectCallExpression ind:
                FindCaptures(ind.Callee, lambdaScope, captures);
                foreach (var a in ind.Arguments) FindCaptures(a, lambdaScope, captures);
                break;
            case CastExpressionNode cast:
                FindCaptures(cast.Expr, lambdaScope, captures);
                break;
        }
    }

    private void CheckCaptureCandidate(string name, Scope lambdaScope, HashSet<string> captures)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (lambdaScope.ContainsLocal(name)) return;
        if (_functions.ContainsKey(name) || _components.ContainsKey(name) || _resources.ContainsKey(name) ||
            _structs.ContainsKey(name) || _enums.ContainsKey(name) || _systems.ContainsKey(name) || _constants.ContainsKey(name)) return;
        if (name is "println" or "print" or "readln" or "wait_key" or "Some" or "None" or "Ok" or "Err") return;

        var outerSym = lambdaScope.Parent?.Lookup(name);
        if (outerSym != null)
        {
            captures.Add(name);
        }
    }
}
