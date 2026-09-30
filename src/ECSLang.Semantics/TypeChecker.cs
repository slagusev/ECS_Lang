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
    private readonly Dictionary<string, EnumSymbol> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SystemSymbol> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, FunctionDeclaration>> _methods = new(StringComparer.Ordinal);
    private readonly List<PipelineDeclaration> _pipelines = new();
    private Scope _currentScope = new();

    public IReadOnlyDictionary<string, ComponentSymbol> Components => _components;
    public IReadOnlyDictionary<string, ResourceSymbol> Resources => _resources;
    public IReadOnlyDictionary<string, StructSymbol> Structs => _structs;
    public IReadOnlyDictionary<string, EventSymbol> Events => _events;
    public IReadOnlyDictionary<string, EnumSymbol> Enums => _enums;
    public IReadOnlyDictionary<string, SystemSymbol> Systems => _systems;
    public IReadOnlyDictionary<string, FunctionDeclaration> Functions => _functions;
    public IReadOnlyDictionary<string, Dictionary<string, FunctionDeclaration>> Methods => _methods;
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

        // Pass 1: Register all Components, Resources, Structs, Events, Enums, and Functions
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
            else if (decl is EnumDeclaration en)
            {
                RegisterEnum(en);
            }
            else if (decl is FunctionDeclaration fn)
            {
                _functions[fn.Name] = fn;
            }
            else if (decl is ImplDeclaration impl)
            {
                RegisterImpl(impl);
            }
        }

        // Pass 2: Register & Check Systems, Functions, Methods and Pipelines
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
            else if (decl is ImplDeclaration impl)
            {
                CheckImpl(impl);
            }
            else if (decl is PipelineDeclaration pipe)
            {
                CheckPipeline(pipe);
            }
        }
    }

    private void RegisterImpl(ImplDeclaration impl)
    {
        if (!_methods.TryGetValue(impl.StructName, out var methodMap))
        {
            methodMap = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
            _methods[impl.StructName] = methodMap;
        }

        foreach (var method in impl.Methods)
        {
            if (methodMap.ContainsKey(method.Name))
            {
                _diagnostics.ReportError($"Method '{method.Name}' is already declared for '{impl.StructName}'.", method.Span);
            }
            else
            {
                methodMap[method.Name] = method;
                _functions[$"{impl.StructName}::{method.Name}"] = method;
                _functions[$"{impl.StructName}_{method.Name}"] = method;
            }
        }
    }

    private void CheckImpl(ImplDeclaration impl)
    {
        if (!_structs.ContainsKey(impl.StructName) && !_components.ContainsKey(impl.StructName))
        {
            _diagnostics.ReportError($"Cannot implement methods for unknown struct or component '{impl.StructName}'.", impl.Span);
        }

        foreach (var method in impl.Methods)
        {
            CheckFunction(method);
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

    private void RegisterEnum(EnumDeclaration en)
    {
        if (_enums.ContainsKey(en.Name) || _components.ContainsKey(en.Name) || _resources.ContainsKey(en.Name) || _structs.ContainsKey(en.Name) || _events.ContainsKey(en.Name))
        {
            _diagnostics.ReportError($"Type '{en.Name}' is already defined.", en.Span);
            return;
        }

        var members = new Dictionary<string, EnumMemberSymbol>(StringComparer.Ordinal);
        foreach (var m in en.Members)
        {
            if (!members.TryAdd(m.Name, new EnumMemberSymbol(m.Name, m.Value ?? 0, m.Span)))
            {
                _diagnostics.ReportError($"Duplicate enum member '{m.Name}' in enum '{en.Name}'.", m.Span);
            }
        }

        _enums[en.Name] = new EnumSymbol(en.Name, members, en.Span);
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
            bool isEntity = p.TypeName == "Entity";
            bool isCommands = p.TypeName == "Commands";

            if (!isComp && !isRes && !isEntity && !isCommands)
            {
                _diagnostics.ReportError($"Unknown component, resource, or system parameter '{p.TypeName}' in query parameter.", p.Span);
            }

            if (isEntity && p.IsMutable)
            {
                _diagnostics.ReportError($"Entity parameter '{p.Name}' cannot be mutable.", p.Span);
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
            bool isCommands = p.TypeName == "Commands";

            if (i == 0 && !isEvent)
            {
                _diagnostics.ReportError($"First parameter in system 'read' must be an event type, but found '{p.TypeName}'.", p.Span);
            }
            else if (i > 0 && !isRes && !isCommands)
            {
                _diagnostics.ReportError($"Additional parameters in system 'read' must be resource types or Commands, but found '{p.TypeName}'.", p.Span);
            }

            if (!seenTypes.Add(p.TypeName))
            {
                _diagnostics.ReportError($"Type '{p.TypeName}' is read multiple times in system '{sys.Name}'.", p.Span);
            }

            readParams.Add(new QueryParamSymbol(p.IsMutable, p.Name, TypeSymbol.FromName(p.TypeName), isRes, p.Span));
        }

        var withTypes = new HashSet<string>(StringComparer.Ordinal);
        var withoutTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filter in sys.Filters)
        {
            if (!_components.ContainsKey(filter.ComponentName))
            {
                _diagnostics.ReportError($"Filter component '{filter.ComponentName}' in system '{sys.Name}' is not a declared component.", filter.Span);
            }

            if (filter.Kind == QueryFilterKind.With)
            {
                if (seenTypes.Contains(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Component '{filter.ComponentName}' is already in query parameters of system '{sys.Name}'.", filter.Span);
                }
                if (!withTypes.Add(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Duplicate 'with' filter for component '{filter.ComponentName}' in system '{sys.Name}'.", filter.Span);
                }
                if (withoutTypes.Contains(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Conflicting filters: component '{filter.ComponentName}' cannot be both 'with' and 'without' in system '{sys.Name}'.", filter.Span);
                }
            }
            else if (filter.Kind == QueryFilterKind.Without)
            {
                if (seenTypes.Contains(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Conflicting query: component '{filter.ComponentName}' cannot be both queried and 'without' in system '{sys.Name}'.", filter.Span);
                }
                if (!withoutTypes.Add(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Duplicate 'without' filter for component '{filter.ComponentName}' in system '{sys.Name}'.", filter.Span);
                }
                if (withTypes.Contains(filter.ComponentName))
                {
                    _diagnostics.ReportError($"Conflicting filters: component '{filter.ComponentName}' cannot be both 'with' and 'without' in system '{sys.Name}'.", filter.Span);
                }
            }
        }

        _systems[sys.Name] = new SystemSymbol(sys.Name, queryParams, readParams, sys.Filters, sys.Span);

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
                    var resolvedSystems = new List<SystemSymbol>();
                    foreach (var sCall in par.Systems)
                    {
                        if (!_systems.TryGetValue(sCall.SystemName, out var sSym))
                        {
                            _diagnostics.ReportError($"Undefined system '{sCall.SystemName}' in parallel stage.", sCall.Span);
                        }
                        else
                        {
                            resolvedSystems.Add(sSym);
                        }
                    }

                    // Strict data race check between all pairs in the parallel block
                    for (int i = 0; i < resolvedSystems.Count; i++)
                    {
                        for (int j = i + 1; j < resolvedSystems.Count; j++)
                        {
                            var sA = resolvedSystems[i];
                            var sB = resolvedSystems[j];
                            if (sA.HasConflictWith(sB, out var reason))
                            {
                                _diagnostics.ReportError(
                                    $"Data race detected in parallel block: system '{sA.Name}' conflicts with system '{sB.Name}' ({reason}). Place them in separate stages or synchronize with 'sync;'.",
                                    par.Span);
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
            var varSym = new VariableSymbol(param.Name, paramType, IsMutable: param.IsMutable, param.Span);
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
            ? TypeSymbol.FromName(varDecl.TypeName)
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

    private static bool AreTypesCompatible(TypeSymbol expected, TypeSymbol actual)
    {
        if (expected == actual) return true;
        if (expected == TypeSymbol.Unknown || actual == TypeSymbol.Unknown) return true;
        if ((expected == TypeSymbol.Entity && actual == TypeSymbol.I32) || (expected == TypeSymbol.I32 && actual == TypeSymbol.Entity)) return true;
        if (expected.IsDynamicArray && actual.Name == "[]") return true;
        if (expected.IsDynamicArray && actual.IsDynamicArray)
        {
            expected.TryGetDynamicArrayElement(out var expElem);
            actual.TryGetDynamicArrayElement(out var actElem);
            return AreTypesCompatible(expElem, actElem);
        }
        return false;
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
        if (expectedType != TypeSymbol.Unknown && valType != TypeSymbol.Unknown && !AreTypesCompatible(expectedType, valType))
        {
            _diagnostics.ReportError($"Cannot assign value of type '{valType.Name}' to '{assign.TargetName}{(assign.MemberName != null ? "." + assign.MemberName : "")}' of type '{expectedType.Name}'.", assign.Value.Span);
        }
    }

    public TypeSymbol GetMemberType(string targetTypeName, string memberName, SourceSpan span = default)
    {
        return GetMemberType(TypeSymbol.FromName(targetTypeName), memberName, span);
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

    private void CheckMatchStatement(MatchStatement match)
    {
        var sType = CheckExpression(match.Scrutinee);
        bool isEnum = _enums.ContainsKey(sType.Name);
        if (!sType.IsInteger && !isEnum && sType != TypeSymbol.Unknown)
        {
            _diagnostics.ReportError($"Match expression must be an integer or enum type, got '{sType.Name}'.", match.Scrutinee.Span);
        }

        foreach (var arm in match.Arms)
        {
            if (arm.Pattern is not WildcardExpression)
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
            }

            CheckBlock(arm.Body);
        }
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
            ArrayLiteralExpression arrLit => CheckArrayLiteral(arrLit),
            IndexExpression idxExpr => CheckIndexExpression(idxExpr),
            WildcardExpression => TypeSymbol.Unknown,
            _ => TypeSymbol.Unknown
        };

        _nodeTypes[expr] = type;
        return type;
    }

    private TypeSymbol CheckArrayLiteral(ArrayLiteralExpression arrLit)
    {
        if (arrLit.Elements.Count == 0)
        {
            return TypeSymbol.CreateArray(TypeSymbol.Unknown, 0);
        }

        var firstType = CheckExpression(arrLit.Elements[0]);
        for (int i = 1; i < arrLit.Elements.Count; i++)
        {
            var elemType = CheckExpression(arrLit.Elements[i]);
            if (firstType != TypeSymbol.Unknown && elemType != TypeSymbol.Unknown && firstType != elemType)
            {
                _diagnostics.ReportError($"Array element at index {i} has type '{elemType.Name}', expected '{firstType.Name}'.", arrLit.Elements[i].Span);
            }
        }

        return TypeSymbol.CreateArray(firstType, arrLit.Elements.Count);
    }

    private TypeSymbol CheckIndexExpression(IndexExpression idxExpr)
    {
        var targetType = CheckExpression(idxExpr.Target);
        var indexType = CheckExpression(idxExpr.Index);

        if (targetType.TryGetMapInfo(out var mapKeyType, out var mapValType))
        {
            if (!AreTypesCompatible(mapKeyType, indexType))
            {
                _diagnostics.ReportError($"Map key of type '{targetType.Name}' expects '{mapKeyType.Name}', but got '{indexType.Name}'.", idxExpr.Index.Span);
            }
            return mapValType;
        }

        if (!indexType.IsInteger)
        {
            _diagnostics.ReportError("Array index must be an integer.", idxExpr.Index.Span);
        }

        if (targetType.TryGetArrayInfo(out var elemType, out _))
        {
            return elemType;
        }

        if (targetType.TryGetDynamicArrayElement(out var dynElem))
        {
            return dynElem;
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' is not indexable.", idxExpr.Span);
        return TypeSymbol.Unknown;
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
        if (mem.Target is IdentifierExpression id && _enums.TryGetValue(id.Name, out var enumSym))
        {
            if (enumSym.Members.TryGetValue(mem.MemberName, out var memberSym))
            {
                return TypeSymbol.FromName(enumSym.Name);
            }
            _diagnostics.ReportError($"Enum '{enumSym.Name}' does not have member '{mem.MemberName}'.", mem.Span);
            return TypeSymbol.Unknown;
        }

        var targetType = CheckExpression(mem.Target);
        if (targetType == TypeSymbol.String && mem.MemberName is "len" or "length")
        {
            return TypeSymbol.I32;
        }
        if (targetType.IsDynamicArray && mem.MemberName is "len" or "length" or "capacity")
        {
            return TypeSymbol.I32;
        }
        if (targetType.IsMap && mem.MemberName is "len" or "length" or "capacity" or "count")
        {
            return TypeSymbol.I32;
        }
        return GetMemberType(targetType, mem.MemberName, mem.Span);
    }

    private TypeSymbol CheckBinaryExpression(BinaryExpression bin)
    {
        var leftType = CheckExpression(bin.Left);
        var rightType = CheckExpression(bin.Right);

        if (bin.Operator == BinaryOperator.Add && (leftType == TypeSymbol.String || rightType == TypeSymbol.String))
        {
            return TypeSymbol.String;
        }

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

        if (call.Callee.StartsWith("Vec<") || call.Callee.StartsWith("List<") ||
            call.Callee.StartsWith("Map<") || call.Callee.StartsWith("HashMap<"))
        {
            return TypeSymbol.FromName(call.Callee);
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
            "draw_text" or "rl_draw_text" or "draw_line" or "rl_draw_line" or
            "draw_texture" or "rl_draw_texture" or "draw_texture_pro" or "rl_draw_texture_pro" or
            "unload_texture" or "rl_unload_texture" or
            "init_audio_device" or "rl_init_audio_device" or "close_audio_device" or "rl_close_audio_device" or
            "play_sound" or "rl_play_sound" or "stop_sound" or "rl_stop_sound" or
            "pause_sound" or "rl_pause_sound" or "resume_sound" or "rl_resume_sound" or
            "set_sound_volume" or "rl_set_sound_volume" or "unload_sound" or "rl_unload_sound" or
            "begin_mode_2d" or "rl_begin_mode_2d" or "end_mode_2d" or "rl_end_mode_2d" or
            "render_profiler" or "render_debug_overlay" or "ecs::render_profiler")
        {
            return TypeSymbol.Void;
        }

        // Raylib Input & Query built-ins
        if (call.Callee is "window_should_close" or "rl_window_should_close" or
            "is_key_down" or "rl_is_key_down" or "is_key_pressed" or "rl_is_key_pressed" or
            "is_key_released" or "rl_is_key_released" or "is_key_up" or "rl_is_key_up" or
            "is_mouse_button_down" or "rl_is_mouse_button_down" or
            "is_mouse_button_pressed" or "rl_is_mouse_button_pressed" or
            "is_audio_device_ready" or "rl_is_audio_device_ready" or
            "is_sound_playing" or "rl_is_sound_playing")
        {
            return TypeSymbol.Bool;
        }

        if (call.Callee is "get_fps" or "rl_get_fps" or
            "get_mouse_x" or "rl_get_mouse_x" or
            "get_mouse_y" or "rl_get_mouse_y" or
            "get_texture_width" or "rl_get_texture_width" or
            "get_texture_height" or "rl_get_texture_height" or
            "rl_color" or "get_tick_count" or "time_ms")
        {
            return TypeSymbol.I32;
        }

        if (call.Callee is "load_texture" or "rl_load_texture" or
            "load_sound" or "rl_load_sound")
        {
            return TypeSymbol.I64;
        }

        if (call.Callee is "get_frame_time" or "rl_get_frame_time")
        {
            return TypeSymbol.F32;
        }

        if (call.Callee is "get_time" or "rl_get_time")
        {
            return TypeSymbol.F64;
        }

        // Built-in string functions
        if (call.Callee is "to_string" or "str_concat")
        {
            return TypeSymbol.String;
        }

        if (call.Callee is "str_len" or "string_length")
        {
            return TypeSymbol.I32;
        }

        // Built-in math functions
        if (call.Callee is "sqrt" or "sin" or "cos" or "floor" or "ceil")
        {
            return (call.Arguments.Count > 0 && _nodeTypes.TryGetValue(call.Arguments[0], out var argT) && argT == TypeSymbol.F64)
                ? TypeSymbol.F64
                : TypeSymbol.F32;
        }

        if (call.Callee is "abs" or "min" or "max" or "clamp")
        {
            return (call.Arguments.Count > 0 && _nodeTypes.TryGetValue(call.Arguments[0], out var argT))
                ? argT
                : TypeSymbol.I32;
        }

        if (call.Callee is "rand" or "rand_range")
        {
            return TypeSymbol.I32;
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

        if (_methods.TryGetValue(targetType.Name, out var methodMap) &&
            methodMap.TryGetValue(methodCall.MethodName, out var methodDecl))
        {
            int expectedArgCount = methodDecl.Parameters.Count;
            bool hasSelf = methodDecl.Parameters.Count > 0 && methodDecl.Parameters[0].Name == "self";
            if (hasSelf) expectedArgCount--;

            if (methodCall.Arguments.Count != expectedArgCount)
            {
                _diagnostics.ReportError(
                    $"Method '{methodCall.MethodName}' expects {expectedArgCount} arguments, but got {methodCall.Arguments.Count}.",
                    methodCall.Span);
            }
            else
            {
                int pOffset = hasSelf ? 1 : 0;
                for (int i = 0; i < methodCall.Arguments.Count; i++)
                {
                    var argType = GetNodeType(methodCall.Arguments[i]);
                    var expectedParamType = TypeSymbol.FromName(methodDecl.Parameters[i + pOffset].TypeName);
                    if (!AreTypesCompatible(expectedParamType, argType))
                    {
                        _diagnostics.ReportError(
                            $"Argument {i + 1} of method '{methodCall.MethodName}' expects type '{expectedParamType.Name}', but got '{argType.Name}'.",
                            methodCall.Arguments[i].Span);
                    }
                }
            }

            var retType = methodDecl.ReturnType != null
                ? TypeSymbol.FromName(methodDecl.ReturnType)
                : TypeSymbol.Void;
            _nodeTypes[methodCall] = retType;
            return retType;
        }

        if (targetType == TypeSymbol.World || targetType == TypeSymbol.Commands)
        {
            if (methodCall.MethodName == "spawn")
            {
                return TypeSymbol.Entity;
            }

            if (methodCall.MethodName == "get_by_name")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'get_by_name' expects 1 argument (entity name string).", methodCall.Span);
                }
                else
                {
                    var aType = GetNodeType(methodCall.Arguments[0]);
                    if (aType != TypeSymbol.String && aType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of 'get_by_name' expects type 'string', but got '{aType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Entity;
                return TypeSymbol.Entity;
            }

            if (methodCall.MethodName == "has_name")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'has_name' expects 1 argument (entity name string).", methodCall.Span);
                }
                else
                {
                    var aType = GetNodeType(methodCall.Arguments[0]);
                    if (aType != TypeSymbol.String && aType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of 'has_name' expects type 'string', but got '{aType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "set_name")
            {
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Method 'set_name' expects 2 arguments (entity, name string).", methodCall.Span);
                }
                else
                {
                    var eType = GetNodeType(methodCall.Arguments[0]);
                    var nType = GetNodeType(methodCall.Arguments[1]);
                    if (!eType.IsInteger && eType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of 'set_name' expects type 'Entity' or integer, but got '{eType.Name}'.", methodCall.Arguments[0].Span);
                    }
                    if (nType != TypeSymbol.String && nType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 2 of 'set_name' expects type 'string', but got '{nType.Name}'.", methodCall.Arguments[1].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }

            if (methodCall.MethodName.StartsWith("has_"))
            {
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "reset_string_arena")
            {
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }

            if (methodCall.MethodName == "alloc_string")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'alloc_string' expects 1 argument (capacity i32).", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.String;
                return TypeSymbol.String;
            }

            if (methodCall.MethodName.StartsWith("set_") ||
                methodCall.MethodName.StartsWith("add_") ||
                methodCall.MethodName.StartsWith("remove_") ||
                methodCall.MethodName.StartsWith("emit_") ||
                methodCall.MethodName == "add" ||
                methodCall.MethodName == "set" ||
                methodCall.MethodName == "despawn" ||
                methodCall.MethodName == "apply_commands" ||
                methodCall.MethodName == "emit" ||
                methodCall.MethodName == "swap_events" ||
                methodCall.MethodName == "sort_hierarchy" ||
                methodCall.MethodName == "render_profiler" ||
                methodCall.MethodName == "render_debug_overlay")
            {
                return TypeSymbol.Void;
            }
        }

        if (methodCall.MethodName == "to_string")
        {
            return TypeSymbol.String;
        }

        if (targetType == TypeSymbol.String && methodCall.MethodName is "len" or "length")
        {
            return TypeSymbol.I32;
        }

        if (targetType.IsDynamicArray)
        {
            targetType.TryGetDynamicArrayElement(out var elemType);
            if (methodCall.MethodName == "push")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'push' expects exactly 1 argument.", methodCall.Span);
                }
                else
                {
                    var argType = GetNodeType(methodCall.Arguments[0]);
                    if (elemType != TypeSymbol.Unknown && argType != TypeSymbol.Unknown && !AreTypesCompatible(elemType, argType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'push' expects type '{elemType.Name}', but got '{argType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }

            if (methodCall.MethodName == "pop")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'pop' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = elemType;
                return elemType;
            }

            if (methodCall.MethodName is "len" or "length" or "capacity")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.I32;
                return TypeSymbol.I32;
            }

            if (methodCall.MethodName == "clear")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'clear' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }
        }

        if (targetType.IsMap)
        {
            targetType.TryGetMapInfo(out var keyType, out var valType);

            if (methodCall.MethodName is "insert" or "put")
            {
                if (methodCall.Arguments.Count != 2)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 2 arguments (key, value).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    var vArg = GetNodeType(methodCall.Arguments[1]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                    if (valType != TypeSymbol.Unknown && vArg != TypeSymbol.Unknown && !AreTypesCompatible(valType, vArg))
                    {
                        _diagnostics.ReportError($"Argument 2 of '{methodCall.MethodName}' expects value type '{valType.Name}', but got '{vArg.Name}'.", methodCall.Arguments[1].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }

            if (methodCall.MethodName == "get")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'get' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'get' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = valType;
                return valType;
            }

            if (methodCall.MethodName is "contains" or "has")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "remove")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'remove' expects 1 argument (key).", methodCall.Span);
                }
                else
                {
                    var kArg = GetNodeType(methodCall.Arguments[0]);
                    if (keyType != TypeSymbol.Unknown && kArg != TypeSymbol.Unknown && !AreTypesCompatible(keyType, kArg))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'remove' expects key type '{keyType.Name}', but got '{kArg.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName is "len" or "length" or "count" or "capacity")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.I32;
                return TypeSymbol.I32;
            }

            if (methodCall.MethodName == "clear")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'clear' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' does not have a method '{methodCall.MethodName}'.", methodCall.Span);
        return TypeSymbol.Unknown;
    }
}
