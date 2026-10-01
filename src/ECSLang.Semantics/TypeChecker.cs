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

    public bool ContainsLocal(string name) =>
        _variables.ContainsKey(name);

    public VariableSymbol? LookupLocal(string name) =>
        _variables.TryGetValue(name, out var sym) ? sym : null;

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

    private TypeSymbol? _currentExpectedReturnType;

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

        var oldRet = _currentExpectedReturnType;
        _currentExpectedReturnType = fn.ReturnType != null ? TypeSymbol.FromName(fn.ReturnType) : TypeSymbol.Void;

        CheckBlock(fn.Body);

        _currentExpectedReturnType = oldRet;
        _currentScope = _currentScope.Parent!;
    }

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
        if (expected.IsOption && actual.Name == "None") return true;
        if (expected.IsOption && actual.IsOption)
        {
            expected.TryGetOptionInfo(out var expInner);
            actual.TryGetOptionInfo(out var actInner);
            return AreTypesCompatible(expInner, actInner);
        }
        if (expected.IsResult && actual.IsResult)
        {
            expected.TryGetResultInfo(out var expOk, out var expErr);
            actual.TryGetResultInfo(out var actOk, out var actErr);
            return (expOk == TypeSymbol.Unknown || actOk == TypeSymbol.Unknown || AreTypesCompatible(expOk, actOk)) &&
                   (expErr == TypeSymbol.Unknown || actErr == TypeSymbol.Unknown || AreTypesCompatible(expErr, actErr));
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
            LambdaExpression lambda => CheckLambdaExpression(lambda),
            IndirectCallExpression indCall => CheckIndirectCallExpression(indCall),
            _ => TypeSymbol.Unknown
        };

        _nodeTypes[expr] = type;
        return type;
    }

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
        }
    }

    private void CheckCaptureCandidate(string name, Scope lambdaScope, HashSet<string> captures)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (lambdaScope.ContainsLocal(name)) return;
        if (_functions.ContainsKey(name) || _components.ContainsKey(name) || _resources.ContainsKey(name) ||
            _structs.ContainsKey(name) || _enums.ContainsKey(name) || _systems.ContainsKey(name)) return;
        if (name is "println" or "print" or "readln" or "wait_key" or "Some" or "None" or "Ok" or "Err") return;

        var outerSym = lambdaScope.Parent?.Lookup(name);
        if (outerSym != null)
        {
            captures.Add(name);
        }
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
        if (ident.Name == "None")
        {
            return new TypeSymbol("None", IsPrimitive: false);
        }
        if (ident.Name.StartsWith("Option<") && ident.Name.EndsWith("::None"))
        {
            var optTypeName = ident.Name.Substring(0, ident.Name.Length - 6);
            return TypeSymbol.FromName(optTypeName);
        }

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

        if (bin.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            if (leftType.IsOption && rightType.Name == "None")
            {
                _nodeTypes[bin.Right] = leftType;
                return TypeSymbol.Bool;
            }
            if (rightType.IsOption && leftType.Name == "None")
            {
                _nodeTypes[bin.Left] = rightType;
                return TypeSymbol.Bool;
            }
            return TypeSymbol.Bool;
        }

        if (bin.Operator is BinaryOperator.Less or BinaryOperator.LessOrEqual or
            BinaryOperator.Greater or BinaryOperator.GreaterOrEqual)
        {
            return TypeSymbol.Bool;
        }

        // Arithmetic
        if (leftType == TypeSymbol.F64 || rightType == TypeSymbol.F64)
            return TypeSymbol.F64;
        if (leftType == TypeSymbol.F32 || rightType == TypeSymbol.F32)
            return TypeSymbol.F32;
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

        if (call.Callee.StartsWith("Option<") && call.Callee.Contains("::"))
        {
            var optTypeName = call.Callee.Substring(0, call.Callee.IndexOf("::", StringComparison.Ordinal));
            return TypeSymbol.FromName(optTypeName);
        }
        if (call.Callee.StartsWith("Result<") && call.Callee.Contains("::"))
        {
            var resTypeName = call.Callee.Substring(0, call.Callee.IndexOf("::", StringComparison.Ordinal));
            return TypeSymbol.FromName(resTypeName);
        }

        if (call.Callee == "Some")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Some' expects 1 argument.", call.Span);
                return TypeSymbol.CreateOption(TypeSymbol.Unknown);
            }
            var argType = GetNodeType(call.Arguments[0]);
            return TypeSymbol.CreateOption(argType);
        }

        if (call.Callee == "None")
        {
            return new TypeSymbol("None", IsPrimitive: false);
        }

        if (call.Callee == "Ok")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Ok' expects 1 argument.", call.Span);
                return TypeSymbol.CreateResult(TypeSymbol.Unknown, TypeSymbol.String);
            }
            var argType = GetNodeType(call.Arguments[0]);
            return TypeSymbol.CreateResult(argType, TypeSymbol.String);
        }

        if (call.Callee == "Err")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Err' expects 1 argument.", call.Span);
                return TypeSymbol.CreateResult(TypeSymbol.Unknown, TypeSymbol.String);
            }
            var argType = GetNodeType(call.Arguments[0]);
            return TypeSymbol.CreateResult(TypeSymbol.Unknown, argType);
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
        if (call.Callee is "is_window_ready" or "rl_is_window_ready" or
            "window_should_close" or "rl_window_should_close" or
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

        var localSym = _currentScope.Lookup(call.Callee);
        if (localSym != null && localSym.Type.IsFunction)
        {
            if (localSym.Type.TryGetFunctionInfo(out var paramTypes, out var retType))
            {
                if (call.Arguments.Count != paramTypes.Count)
                {
                    _diagnostics.ReportError($"Closure/Function '{call.Callee}' expects {paramTypes.Count} arguments, but got {call.Arguments.Count}.", call.Span);
                }
                else
                {
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        var argType = GetNodeType(call.Arguments[i]);
                        if (!AreTypesCompatible(paramTypes[i], argType))
                        {
                            _diagnostics.ReportError($"Argument {i + 1} of '{call.Callee}' expects '{paramTypes[i].Name}', but got '{argType.Name}'.", call.Arguments[i].Span);
                        }
                    }
                }
                return retType;
            }
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

            if (methodCall.MethodName is "find" or "find_entity")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (entity name string).", methodCall.Span);
                }
                else
                {
                    var aType = GetNodeType(methodCall.Arguments[0]);
                    if (aType != TypeSymbol.String && aType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of '{methodCall.MethodName}' expects type 'string', but got '{aType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                var optEnt = TypeSymbol.CreateOption(TypeSymbol.Entity);
                _nodeTypes[methodCall] = optEnt;
                return optEnt;
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

            if (methodCall.MethodName == "get")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'get' expects 1 argument (index i32).", methodCall.Span);
                }
                else
                {
                    var idxType = GetNodeType(methodCall.Arguments[0]);
                    if (!idxType.IsInteger && idxType != TypeSymbol.Unknown)
                    {
                        _diagnostics.ReportError($"Argument 1 of 'get' expects integer index, but got '{idxType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                var optRet = TypeSymbol.CreateOption(elemType);
                _nodeTypes[methodCall] = optRet;
                return optRet;
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

            if (methodCall.MethodName == "for_each")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'for_each' expects 1 argument (closure/function).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out _))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Closure for 'for_each' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Void;
                return TypeSymbol.Void;
            }

            if (methodCall.MethodName == "map")
            {
                TypeSymbol mappedElem = TypeSymbol.Unknown;
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'map' expects 1 argument (closure/function).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out var rType))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Closure for 'map' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                        mappedElem = rType;
                    }
                }
                var resType = TypeSymbol.CreateDynamicArray(mappedElem != TypeSymbol.Unknown ? mappedElem : elemType);
                _nodeTypes[methodCall] = resType;
                return resType;
            }

            if (methodCall.MethodName == "filter")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'filter' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out var rType))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for 'filter' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = targetType;
                return targetType;
            }

            if (methodCall.MethodName is "any" or "all")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out var rType))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for '{methodCall.MethodName}' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "find")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'find' expects 1 argument (predicate closure).", methodCall.Span);
                }
                else
                {
                    var cType = GetNodeType(methodCall.Arguments[0]);
                    if (cType.IsFunction && cType.TryGetFunctionInfo(out var pTypes, out var rType))
                    {
                        if (pTypes.Count != 1 || (elemType != TypeSymbol.Unknown && !AreTypesCompatible(pTypes[0], elemType)))
                        {
                            _diagnostics.ReportError($"Predicate closure for 'find' must accept '{elemType.Name}'.", methodCall.Arguments[0].Span);
                        }
                    }
                }
                var optRet = TypeSymbol.CreateOption(elemType);
                _nodeTypes[methodCall] = optRet;
                return optRet;
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

            if (methodCall.MethodName is "find" or "get_opt")
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
                var optRet = TypeSymbol.CreateOption(valType);
                _nodeTypes[methodCall] = optRet;
                return optRet;
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

        if (targetType.IsOption)
        {
            targetType.TryGetOptionInfo(out var optElem);
            if (methodCall.MethodName is "is_some" or "is_none")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "unwrap")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = optElem;
                return optElem;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'unwrap_or' expects 1 argument (default value).", methodCall.Span);
                }
                else
                {
                    var defType = GetNodeType(methodCall.Arguments[0]);
                    if (optElem != TypeSymbol.Unknown && defType != TypeSymbol.Unknown && !AreTypesCompatible(optElem, defType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'unwrap_or' expects type '{optElem.Name}', but got '{defType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = optElem;
                return optElem;
            }
        }

        if (targetType.IsResult)
        {
            targetType.TryGetResultInfo(out var okType, out var errType);
            if (methodCall.MethodName is "is_ok" or "is_err")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError($"Method '{methodCall.MethodName}' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = TypeSymbol.Bool;
                return TypeSymbol.Bool;
            }

            if (methodCall.MethodName == "unwrap")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = okType;
                return okType;
            }

            if (methodCall.MethodName == "unwrap_err")
            {
                if (methodCall.Arguments.Count != 0)
                {
                    _diagnostics.ReportError("Method 'unwrap_err' expects 0 arguments.", methodCall.Span);
                }
                _nodeTypes[methodCall] = errType;
                return errType;
            }

            if (methodCall.MethodName == "unwrap_or")
            {
                if (methodCall.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Method 'unwrap_or' expects 1 argument (default value).", methodCall.Span);
                }
                else
                {
                    var defType = GetNodeType(methodCall.Arguments[0]);
                    if (okType != TypeSymbol.Unknown && defType != TypeSymbol.Unknown && !AreTypesCompatible(okType, defType))
                    {
                        _diagnostics.ReportError($"Argument 1 of 'unwrap_or' expects type '{okType.Name}', but got '{defType.Name}'.", methodCall.Arguments[0].Span);
                    }
                }
                _nodeTypes[methodCall] = okType;
                return okType;
            }
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' does not have a method '{methodCall.MethodName}'.", methodCall.Span);
        return TypeSymbol.Unknown;
    }
}
