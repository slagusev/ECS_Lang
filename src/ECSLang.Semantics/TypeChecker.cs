using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed record VariableSymbol(string Name, TypeSymbol Type, bool IsMutable, SourceSpan Span);

public sealed class Scope
{
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.Ordinal);
    public Scope? Parent { get; }

    public Scope(Scope? parent = null)
    {
        Parent = parent;
    }

    public bool TryDeclare(VariableSymbol symbol) =>
        _variables.TryAdd(symbol.Name, symbol);

    public VariableSymbol? Lookup(string name)
    {
        if (_variables.TryGetValue(name, out var sym))
            return sym;
        return Parent?.Lookup(name);
    }
}

public sealed class TypeChecker
{
    private readonly DiagnosticsBag _diagnostics;
    private readonly Dictionary<AstNode, TypeSymbol> _nodeTypes = new();
    private readonly Dictionary<string, ComponentSymbol> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceSymbol> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructSymbol> _structs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventSymbol> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SystemSymbol> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _functions = new(StringComparer.Ordinal);
    private readonly List<PipelineDeclaration> _pipelines = new();
    private Scope _currentScope = new();

    public IReadOnlyDictionary<string, ComponentSymbol> Components => _components;
    public IReadOnlyDictionary<string, ResourceSymbol> Resources => _resources;
    public IReadOnlyDictionary<string, StructSymbol> Structs => _structs;
    public IReadOnlyDictionary<string, EventSymbol> Events => _events;
    public IReadOnlyDictionary<string, SystemSymbol> Systems => _systems;
    public IReadOnlyDictionary<string, FunctionDeclaration> Functions => _functions;
    public IReadOnlyList<PipelineDeclaration> Pipelines => _pipelines;

    public TypeChecker(DiagnosticsBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public TypeSymbol GetNodeType(AstNode node) =>
        _nodeTypes.TryGetValue(node, out var t) ? t : TypeSymbol.Unknown;

    public void CheckProgram(ProgramNode program)
    {
        RegisterBuiltinComponents();

        // Pass 1: Register all Components, Resources, Structs, Events, and Functions
        foreach (var decl in program.Declarations)
        {
            if (decl is ComponentDeclaration comp)
            {
                RegisterComponent(comp);
            }
            else if (decl is ResourceDeclaration res)
            {
                RegisterResource(res);
            }
            else if (decl is StructDeclaration st)
            {
                RegisterStruct(st);
            }
            else if (decl is EventDeclaration ev)
            {
                RegisterEvent(ev);
            }
            else if (decl is FunctionDeclaration fn)
            {
                _functions[fn.Name] = fn;
            }
        }

        // Pass 2: Register & Check Systems, Functions and Pipelines
        foreach (var decl in program.Declarations)
        {
            if (decl is SystemDeclaration sys)
            {
                CheckSystem(sys);
            }
            else if (decl is FunctionDeclaration fn)
            {
                CheckFunction(fn);
            }
            else if (decl is PipelineDeclaration pipe)
            {
                CheckPipeline(pipe);
            }
        }
    }

    private void RegisterBuiltinComponents()
    {
        _components["ChildOf"] = new ComponentSymbol("ChildOf", new[]
        {
            new ComponentFieldSymbol("parent", TypeSymbol.I32, SourceSpan.None)
        }, SourceSpan.None);
    }

    private void RegisterComponent(ComponentDeclaration comp)
    {
        if (_components.ContainsKey(comp.Name))
        {
            _diagnostics.ReportError($"Duplicate component declaration '{comp.Name}'.", comp.Span);
            return;
        }

        var fields = new List<ComponentFieldSymbol>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in comp.Fields)
        {
            if (!fieldNames.Add(f.Name))
            {
                _diagnostics.ReportError($"Duplicate field '{f.Name}' in component '{comp.Name}'.", f.Span);
            }
            fields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(f.TypeName), f.Span));
        }

        _components[comp.Name] = new ComponentSymbol(comp.Name, fields, comp.Span);
    }

    private void RegisterResource(ResourceDeclaration res)
    {
        if (_resources.ContainsKey(res.Name))
        {
            _diagnostics.ReportError($"Duplicate resource declaration '{res.Name}'.", res.Span);
            return;
        }

        var fields = new List<ComponentFieldSymbol>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in res.Fields)
        {
            if (!fieldNames.Add(f.Name))
            {
                _diagnostics.ReportError($"Duplicate field '{f.Name}' in resource '{res.Name}'.", f.Span);
            }
            fields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(f.TypeName), f.Span));
        }

        _resources[res.Name] = new ResourceSymbol(res.Name, fields, res.Span);
    }

    private void RegisterStruct(StructDeclaration st)
    {
        if (_structs.ContainsKey(st.Name))
        {
            _diagnostics.ReportError($"Duplicate struct declaration '{st.Name}'.", st.Span);
            return;
        }

        var fields = new List<ComponentFieldSymbol>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in st.Fields)
        {
            if (!fieldNames.Add(f.Name))
            {
                _diagnostics.ReportError($"Duplicate field '{f.Name}' in struct '{st.Name}'.", f.Span);
            }
            fields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(f.TypeName), f.Span));
        }

        _structs[st.Name] = new StructSymbol(st.Name, fields, st.Span);
    }

    private void RegisterEvent(EventDeclaration ev)
    {
        if (_events.ContainsKey(ev.Name) || _components.ContainsKey(ev.Name) || _resources.ContainsKey(ev.Name) || _structs.ContainsKey(ev.Name))
        {
            _diagnostics.ReportError($"Type '{ev.Name}' is already defined.", ev.Span);
            return;
        }

        var fields = new List<ComponentFieldSymbol>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in ev.Fields)
        {
            if (!fieldNames.Add(f.Name))
            {
                _diagnostics.ReportError($"Duplicate field '{f.Name}' in event '{ev.Name}'.", f.Span);
            }
            fields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(f.TypeName), f.Span));
        }

        var evSym = new EventSymbol(ev.Name, fields, ev.Span);
        _events[ev.Name] = evSym;

        // Also register as struct so constructor syntax Event(a, b) works
        _structs[ev.Name] = new StructSymbol(ev.Name, fields, ev.Span);
    }

    private void CheckSystem(SystemDeclaration sys)
    {
        var queryParams = new List<QueryParamSymbol>();
        var readParams = new List<QueryParamSymbol>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in sys.QueryParams)
        {
            bool isComp = _components.ContainsKey(p.TypeName);
            bool isRes = _resources.ContainsKey(p.TypeName);

            if (!isComp && !isRes)
            {
                _diagnostics.ReportError($"Unknown component or resource '{p.TypeName}' in query parameter.", p.Span);
            }

            if (!seenTypes.Add(p.TypeName))
            {
                _diagnostics.ReportError($"Type '{p.TypeName}' is queried multiple times in system '{sys.Name}'.", p.Span);
            }

            queryParams.Add(new QueryParamSymbol(p.IsMutable, p.Name, TypeSymbol.FromName(p.TypeName), isRes, p.Span));
        }

        for (int i = 0; i < sys.ReadParams.Count; i++)
        {
            var p = sys.ReadParams[i];
            bool isEvent = _events.ContainsKey(p.TypeName);
            bool isRes = _resources.ContainsKey(p.TypeName);

            if (i == 0 && !isEvent)
            {
                _diagnostics.ReportError($"First parameter in system 'read' must be an event type, but found '{p.TypeName}'.", p.Span);
            }
            else if (i > 0 && !isRes)
            {
                _diagnostics.ReportError($"Additional parameters in system 'read' must be resource types, but found '{p.TypeName}'.", p.Span);
            }

            if (!seenTypes.Add(p.TypeName))
            {
                _diagnostics.ReportError($"Type '{p.TypeName}' is read multiple times in system '{sys.Name}'.", p.Span);
            }

            readParams.Add(new QueryParamSymbol(p.IsMutable, p.Name, TypeSymbol.FromName(p.TypeName), isRes, p.Span));
        }

        _systems[sys.Name] = new SystemSymbol(sys.Name, queryParams, readParams, sys.Span);

        // System Body Scope
        var sysScope = new Scope(_currentScope);
        _currentScope = sysScope;

        // Provide world variable in system scope
        _currentScope.TryDeclare(new VariableSymbol("world", TypeSymbol.World, false, sys.Span));

        foreach (var qp in queryParams)
        {
            var varSym = new VariableSymbol(qp.ParameterName, qp.Type, qp.IsMutable, qp.Span);
            _currentScope.TryDeclare(varSym);
        }

        foreach (var rp in readParams)
        {
            var varSym = new VariableSymbol(rp.ParameterName, rp.Type, rp.IsMutable, rp.Span);
            _currentScope.TryDeclare(varSym);
        }

        CheckBlock(sys.Body);
        _currentScope = _currentScope.Parent!;
    }

    private void CheckPipeline(PipelineDeclaration pipe)
    {
        _pipelines.Add(pipe);

        foreach (var stage in pipe.Stages)
        {
            foreach (var action in stage.Actions)
            {
                if (action is SystemCallAction call)
                {
                    if (!_systems.ContainsKey(call.SystemName))
                    {
                        _diagnostics.ReportError($"Undefined system '{call.SystemName}' in stage '{stage.Name}'.", call.Span);
                    }
                }
                else if (action is ParallelAction par)
                {
                    var writeComponents = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var sCall in par.Systems)
                    {
                        if (!_systems.TryGetValue(sCall.SystemName, out var sSym))
                        {
                            _diagnostics.ReportError($"Undefined system '{sCall.SystemName}' in parallel stage.", sCall.Span);
                            continue;
                        }

                        // Parallel safety check: write conflicts
                        foreach (var qp in sSym.QueryParams.Where(q => q.IsMutable))
                        {
                            if (!writeComponents.Add(qp.Type.Name))
                            {
                                _diagnostics.ReportWarning(
                                    $"Parallel stage has multiple systems writing to '{qp.Type.Name}'. Consider placing them in separate sync stages.",
                                    sCall.Span);
                            }
                        }
                    }
                }
            }
        }
    }

    private void CheckFunction(FunctionDeclaration fn)
    {
        var fnScope = new Scope(_currentScope);
        _currentScope = fnScope;

        foreach (var param in fn.Parameters)
        {
            var paramType = TypeSymbol.FromName(param.TypeName);
            var varSym = new VariableSymbol(param.Name, paramType, IsMutable: false, param.Span);
            if (!_currentScope.TryDeclare(varSym))
            {
                _diagnostics.ReportError($"Duplicate parameter name '{param.Name}'.", param.Span);
            }
        }

        CheckBlock(fn.Body);
        _currentScope = _currentScope.Parent!;
    }

    private void CheckBlock(BlockStatement block)
    {
        var blockScope = new Scope(_currentScope);
        _currentScope = blockScope;

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
                    CheckExpression(retStmt.Value);
                }
                break;
            case ExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression);
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
            ? TypeSymbol.FromName(varDecl.TypeName)
            : initType;

        if (varDecl.TypeName != null && initType != TypeSymbol.Unknown && explicitType != initType)
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

        var valType = CheckExpression(assign.Value);
        if (expectedType != TypeSymbol.Unknown && valType != TypeSymbol.Unknown && expectedType != valType)
        {
            _diagnostics.ReportError($"Cannot assign value of type '{valType.Name}' to '{assign.TargetName}{(assign.MemberName != null ? "." + assign.MemberName : "")}' of type '{expectedType.Name}'.", assign.Value.Span);
        }
    }

    private TypeSymbol GetMemberType(TypeSymbol targetType, string memberName, SourceSpan span)
    {
        if (_components.TryGetValue(targetType.Name, out var comp))
        {
            var field = comp.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
                return field.Type;
            _diagnostics.ReportError($"Component '{targetType.Name}' has no member '{memberName}'.", span);
            return TypeSymbol.Unknown;
        }

        if (_resources.TryGetValue(targetType.Name, out var res))
        {
            var field = res.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
                return field.Type;
            _diagnostics.ReportError($"Resource '{targetType.Name}' has no member '{memberName}'.", span);
            return TypeSymbol.Unknown;
        }

        if (_structs.TryGetValue(targetType.Name, out var st))
        {
            var field = st.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
                return field.Type;
            _diagnostics.ReportError($"Struct '{targetType.Name}' has no member '{memberName}'.", span);
            return TypeSymbol.Unknown;
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' has no member '{memberName}'.", span);
        return TypeSymbol.Unknown;
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

    public TypeSymbol CheckExpression(ExpressionNode expr)
    {
        var type = expr switch
        {
            NumberLiteralExpression num => num.IsFloatingPoint ? TypeSymbol.F32 : TypeSymbol.I32,
            StringLiteralExpression => TypeSymbol.String,
            BooleanLiteralExpression => TypeSymbol.Bool,
            IdentifierExpression ident => CheckIdentifier(ident),
            MemberAccessExpression mem => CheckMemberAccess(mem),
            BinaryExpression bin => CheckBinaryExpression(bin),
            UnaryExpression un => CheckUnaryExpression(un),
            CallExpression call => CheckCallExpression(call),
            MethodCallExpression methodCall => CheckMethodCall(methodCall),
            _ => TypeSymbol.Unknown
        };

        _nodeTypes[expr] = type;
        return type;
    }

    private TypeSymbol CheckIdentifier(IdentifierExpression ident)
    {
        var sym = _currentScope.Lookup(ident.Name);
        if (sym == null)
        {
            _diagnostics.ReportError($"Undefined variable '{ident.Name}'.", ident.Span);
            return TypeSymbol.Unknown;
        }
        return sym.Type;
    }

    private TypeSymbol CheckMemberAccess(MemberAccessExpression mem)
    {
        var targetType = CheckExpression(mem.Target);
        return GetMemberType(targetType, mem.MemberName, mem.Span);
    }

    private TypeSymbol CheckBinaryExpression(BinaryExpression bin)
    {
        var leftType = CheckExpression(bin.Left);
        var rightType = CheckExpression(bin.Right);

        if (bin.Operator is BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr)
        {
            if (leftType != TypeSymbol.Bool || rightType != TypeSymbol.Bool)
            {
                _diagnostics.ReportError("Logical operators '&&' and '||' require 'bool' operands.", bin.Span);
            }
            return TypeSymbol.Bool;
        }

        if (bin.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.Less or BinaryOperator.LessOrEqual or
            BinaryOperator.Greater or BinaryOperator.GreaterOrEqual)
        {
            return TypeSymbol.Bool;
        }

        // Arithmetic
        return leftType;
    }

    private TypeSymbol CheckUnaryExpression(UnaryExpression un)
    {
        var opType = CheckExpression(un.Operand);
        if (un.Operator == UnaryOperator.LogicalNot)
        {
            if (opType != TypeSymbol.Bool)
                _diagnostics.ReportError("Operator '!' requires 'bool' operand.", un.Span);
            return TypeSymbol.Bool;
        }

        return opType;
    }

    private TypeSymbol CheckCallExpression(CallExpression call)
    {
        foreach (var arg in call.Arguments)
        {
            CheckExpression(arg);
        }

        if (call.Callee is "println" or "print")
        {
            return TypeSymbol.Void;
        }

        if (call.Callee is "readln" or "wait_key")
        {
            return TypeSymbol.I32;
        }

        // Raylib Window & Drawing built-ins
        if (call.Callee is "init_window" or "rl_init_window" or "close_window" or "rl_close_window" or
            "set_target_fps" or "rl_set_target_fps" or "begin_drawing" or "rl_begin_drawing" or
            "end_drawing" or "rl_end_drawing" or "clear_background" or "rl_clear_background" or
            "draw_rectangle" or "rl_draw_rectangle" or "draw_circle" or "rl_draw_circle" or
            "draw_text" or "rl_draw_text" or "draw_line" or "rl_draw_line")
        {
            return TypeSymbol.Void;
        }

        // Raylib Input & Query built-ins
        if (call.Callee is "window_should_close" or "rl_window_should_close" or
            "is_key_down" or "rl_is_key_down" or "is_key_pressed" or "rl_is_key_pressed" or
            "is_key_released" or "rl_is_key_released" or "is_key_up" or "rl_is_key_up" or
            "is_mouse_button_down" or "rl_is_mouse_button_down" or
            "is_mouse_button_pressed" or "rl_is_mouse_button_pressed")
        {
            return TypeSymbol.Bool;
        }

        if (call.Callee is "get_fps" or "rl_get_fps" or
            "get_mouse_x" or "rl_get_mouse_x" or
            "get_mouse_y" or "rl_get_mouse_y" or
            "rl_color")
        {
            return TypeSymbol.I32;
        }

        if (call.Callee is "get_frame_time" or "rl_get_frame_time")
        {
            return TypeSymbol.F32;
        }

        if (call.Callee is "get_time" or "rl_get_time")
        {
            return TypeSymbol.F64;
        }

        if (call.Callee is "ecs::create_world" or "create_world")
        {
            return TypeSymbol.World;
        }

        if (call.Callee == "world_spawn")
        {
            return TypeSymbol.I32;
        }

        if (call.Callee.StartsWith("world_has_"))
        {
            return TypeSymbol.Bool;
        }

        if (call.Callee.StartsWith("world_add_") ||
            call.Callee.StartsWith("world_remove_") ||
            call.Callee.StartsWith("world_set_") ||
            call.Callee.StartsWith("world_emit_") ||
            call.Callee == "world_swap_events")
        {
            return TypeSymbol.Void;
        }

        if (_pipelines.Any(p => p.Name == call.Callee))
        {
            return TypeSymbol.Void;
        }

        if (_systems.ContainsKey(call.Callee))
        {
            return TypeSymbol.Void;
        }

        if (_structs.TryGetValue(call.Callee, out var stSym))
        {
            if (call.Arguments.Count != stSym.Fields.Count)
            {
                _diagnostics.ReportError($"Struct '{call.Callee}' constructor expects {stSym.Fields.Count} arguments, but got {call.Arguments.Count}.", call.Span);
            }
            return TypeSymbol.FromName(stSym.Name);
        }

        if (_functions.TryGetValue(call.Callee, out var fnDecl))
        {
            if (call.Arguments.Count != fnDecl.Parameters.Count)
            {
                _diagnostics.ReportError($"Function '{call.Callee}' expects {fnDecl.Parameters.Count} arguments, but got {call.Arguments.Count}.", call.Span);
            }
            return TypeSymbol.FromName(fnDecl.ReturnType);
        }

        return TypeSymbol.I32;
    }

    private TypeSymbol CheckMethodCall(MethodCallExpression methodCall)
    {
        var targetType = CheckExpression(methodCall.Target);

        foreach (var arg in methodCall.Arguments)
        {
            CheckExpression(arg);
        }

        if (targetType == TypeSymbol.World)
        {
            if (methodCall.MethodName == "spawn")
            {
                return TypeSymbol.I32;
            }

            if (methodCall.MethodName.StartsWith("has_"))
            {
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName.StartsWith("set_") ||
                methodCall.MethodName.StartsWith("add_") ||
                methodCall.MethodName.StartsWith("remove_") ||
                methodCall.MethodName.StartsWith("emit_") ||
                methodCall.MethodName == "emit" ||
                methodCall.MethodName == "swap_events" ||
                methodCall.MethodName == "sort_hierarchy")
            {
                return TypeSymbol.Void;
            }
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' does not have a method '{methodCall.MethodName}'.", methodCall.Span);
        return TypeSymbol.Unknown;
    }
}
