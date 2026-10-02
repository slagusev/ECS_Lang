using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private void RegisterBuiltinComponents()
    {
        _components["ChildOf"] = new ComponentSymbol("ChildOf", new[]
        {
            new ComponentFieldSymbol("parent", TypeSymbol.I32, SourceSpan.None)
        }, SourceSpan.None);
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
        if (impl.TraitName != null)
        {
            VerifyTraitImplementation(impl);
        }

        if (!_structs.ContainsKey(impl.StructName) && !_components.ContainsKey(impl.StructName) && !_genericStructs.ContainsKey(impl.StructName) && !_genericComponents.ContainsKey(impl.StructName))
        {
            _diagnostics.ReportError($"Cannot implement methods for unknown struct or component '{impl.StructName}'.", impl.Span);
        }

        foreach (var method in impl.Methods)
        {
            CheckFunction(method);
        }
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

    private void RegisterConstants(IReadOnlyList<ConstDeclaration> constDecls)
    {
        var pending = new List<ConstDeclaration>(constDecls);
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var decl = pending[i];
                if (TryRegisterConstant(decl))
                {
                    pending.RemoveAt(i);
                    progress = true;
                }
            }
        }

        foreach (var remaining in pending)
        {
            _diagnostics.ReportError($"Constant '{remaining.Name}' has an invalid or cyclical compile-time expression.", remaining.Span);
        }
    }

    private bool TryRegisterConstant(ConstDeclaration decl)
    {
        if (_constants.ContainsKey(decl.Name))
        {
            _diagnostics.ReportError($"Constant '{decl.Name}' is already defined.", decl.Span);
            return true;
        }

        if (!EvaluateConstantExpression(decl.Initializer, out var evalType, out var evalValue))
        {
            return false;
        }

        TypeSymbol targetType = evalType;
        if (!string.IsNullOrEmpty(decl.TypeName))
        {
            targetType = TypeSymbol.FromName(decl.TypeName);
            if (targetType == TypeSymbol.F32 && evalValue is int i)
            {
                evalValue = (float)i;
            }
            else if (targetType == TypeSymbol.F64 && evalValue is int i2)
            {
                evalValue = (double)i2;
            }
            else if (targetType == TypeSymbol.I64 && evalValue is int i3)
            {
                evalValue = (long)i3;
            }
            else if (targetType == TypeSymbol.I32 && evalValue is long l)
            {
                evalValue = (int)l;
            }
            else if (!AreTypesCompatible(targetType, evalType))
            {
                _diagnostics.ReportError($"Constant '{decl.Name}' declared as '{targetType.Name}' but initializer has type '{evalType.Name}'.", decl.Span);
                return true;
            }
        }

        _nodeTypes[decl.Initializer] = targetType;
        _constants[decl.Name] = new ConstSymbol(decl.Name, targetType, evalValue, decl.Span);
        return true;
    }


    private void CheckSystem(SystemDeclaration sys)
    {
        var queryParams = new List<QueryParamSymbol>();
        var readParams = new List<QueryParamSymbol>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in sys.QueryParams)
        {
            var pType = EnsureMonomorphizedType(p.TypeName, p.Span);
            bool isComp = _components.ContainsKey(p.TypeName) || _components.ContainsKey(pType.Name) || _components.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(p.TypeName));
            bool isRes = _resources.ContainsKey(p.TypeName);
            bool isEntity = p.TypeName is "Entity" or "entity";
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
                    if (call.SystemName.Contains("<"))
                    {
                        EnsureMonomorphizedSystem(call.SystemName, null, call.Span);
                    }

                    if (!_systems.ContainsKey(call.SystemName) && !_systems.ContainsKey(TypeSymbol.ToMonomorphizedIdentifier(call.SystemName)))
                    {
                        _diagnostics.ReportError($"Undefined system '{call.SystemName}' in stage '{stage.Name}'.", call.Span);
                    }
                }
                else if (action is ParallelAction par)
                {
                    var resolvedSystems = new List<SystemSymbol>();
                    foreach (var sCall in par.Systems)
                    {
                        if (sCall.SystemName.Contains("<"))
                        {
                            EnsureMonomorphizedSystem(sCall.SystemName, null, sCall.Span);
                        }

                        if (!_systems.TryGetValue(sCall.SystemName, out var sSym) && !_systems.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(sCall.SystemName), out sSym))
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
            var paramType = EnsureMonomorphizedType(param.TypeName, param.Span);
            var varSym = new VariableSymbol(param.Name, paramType, IsMutable: param.IsMutable, param.Span);
            if (!_currentScope.TryDeclare(varSym))
            {
                _diagnostics.ReportError($"Duplicate parameter name '{param.Name}'.", param.Span);
            }
        }

        var oldRet = _currentExpectedReturnType;
        var oldFnName = _currentFunctionName;
        _currentFunctionName = fn.Name;
        _currentExpectedReturnType = fn.ReturnType != null ? EnsureMonomorphizedType(fn.ReturnType, fn.Span) : TypeSymbol.Void;

        CheckBlock(fn.Body);

        _currentExpectedReturnType = oldRet;
        _currentFunctionName = oldFnName;
        _currentScope = _currentScope.Parent!;
    }
}
