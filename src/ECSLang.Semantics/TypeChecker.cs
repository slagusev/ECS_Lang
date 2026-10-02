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

public sealed partial class TypeChecker
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
    private readonly Dictionary<string, TraitDeclaration> _traits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _traitImpls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConstSymbol> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructDeclaration> _genericStructs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentDeclaration> _genericComponents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _genericFunctions = new(StringComparer.Ordinal);
    private readonly List<ImplDeclaration> _genericImpls = new();
    private readonly Dictionary<string, SystemDeclaration> _genericSystems = new(StringComparer.Ordinal);
    private readonly List<PipelineDeclaration> _pipelines = new();
    private Scope _currentScope = new();
    private TypeSymbol? _currentExpectedReturnType;
    private int _loopDepth = 0;

    public List<DeclarationNode> MonomorphizedDeclarations { get; } = new();

    public IReadOnlyDictionary<string, ComponentSymbol> Components => _components;
    public IReadOnlyDictionary<string, ResourceSymbol> Resources => _resources;
    public IReadOnlyDictionary<string, StructSymbol> Structs => _structs;
    public IReadOnlyDictionary<string, EventSymbol> Events => _events;
    public IReadOnlyDictionary<string, EnumSymbol> Enums => _enums;
    public IReadOnlyDictionary<string, ConstSymbol> Constants => _constants;
    public IReadOnlyDictionary<string, SystemSymbol> Systems => _systems;
    public IReadOnlyDictionary<string, FunctionDeclaration> Functions => _functions;
    public IReadOnlyDictionary<string, Dictionary<string, FunctionDeclaration>> Methods => _methods;
    public IReadOnlyDictionary<string, TraitDeclaration> Traits => _traits;
    public IReadOnlyDictionary<string, HashSet<string>> TraitImpls => _traitImpls;
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

        // Pass 0: Register Traits
        foreach (var decl in program.Declarations)
        {
            if (decl is TraitDeclaration trait)
            {
                RegisterTrait(trait);
            }
        }

        // Pass 0.5: Register compile-time Constants
        var constDecls = new List<ConstDeclaration>();
        foreach (var decl in program.Declarations)
        {
            if (decl is ConstDeclaration c)
            {
                constDecls.Add(c);
            }
        }
        RegisterConstants(constDecls);

        // Pass 1: Register all Components, Resources, Structs, Events, Enums, and Functions
        foreach (var decl in program.Declarations)
        {
            if (decl is ComponentDeclaration comp)
            {
                if (comp.TypeParameters != null && comp.TypeParameters.Count > 0)
                    _genericComponents[comp.Name] = comp;
                else
                    RegisterComponent(comp);
            }
            else if (decl is ResourceDeclaration res)
            {
                RegisterResource(res);
            }
            else if (decl is StructDeclaration st)
            {
                if (st.TypeParameters != null && st.TypeParameters.Count > 0)
                    _genericStructs[st.Name] = st;
                else
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
                if (fn.TypeParameters != null && fn.TypeParameters.Count > 0)
                    _genericFunctions[fn.Name] = fn;
                else
                    _functions[fn.Name] = fn;
            }
            else if (decl is ImplDeclaration impl)
            {
                if ((impl.TypeParameters != null && impl.TypeParameters.Count > 0) || impl.StructName.Contains("<"))
                    _genericImpls.Add(impl);
                else
                    RegisterImpl(impl);
            }
            else if (decl is SystemDeclaration sys)
            {
                if (sys.TypeParameters != null && sys.TypeParameters.Count > 0)
                    _genericSystems[sys.Name] = sys;
            }
        }

        // Pass 2: Register & Check Systems, Functions, Methods and Pipelines
        foreach (var decl in program.Declarations)
        {
            if (decl is SystemDeclaration sys && (sys.TypeParameters == null || sys.TypeParameters.Count == 0))
            {
                CheckSystem(sys);
            }
            else if (decl is FunctionDeclaration fn && (fn.TypeParameters == null || fn.TypeParameters.Count == 0))
            {
                CheckFunction(fn);
            }
            else if (decl is ImplDeclaration impl && (impl.TypeParameters == null || impl.TypeParameters.Count == 0) && !impl.StructName.Contains("<"))
            {
                CheckImpl(impl);
            }
            else if (decl is PipelineDeclaration pipe)
            {
                CheckPipeline(pipe);
            }
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

    public TypeSymbol GetMemberType(string targetTypeName, string memberName, SourceSpan span = default)
    {
        return GetMemberType(TypeSymbol.FromName(targetTypeName), memberName, span);
    }

    private TypeSymbol GetMemberType(TypeSymbol targetType, string memberName, SourceSpan span)
    {
        if (targetType.IsGenericInstantiation)
        {
            EnsureMonomorphizedType(targetType.Name, span);
        }

        if (_components.TryGetValue(targetType.Name, out var comp) || _components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(targetType.Name), out comp))
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

        if (_structs.TryGetValue(targetType.Name, out var st) || _structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(targetType.Name), out st))
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
}
