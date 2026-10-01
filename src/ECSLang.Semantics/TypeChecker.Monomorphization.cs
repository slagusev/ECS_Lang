using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private void RegisterTrait(TraitDeclaration trait)
    {
        if (_traits.ContainsKey(trait.Name))
        {
            _diagnostics.ReportError($"Duplicate trait declaration '{trait.Name}'.", trait.Span);
            return;
        }

        _traits[trait.Name] = trait;
    }

    private void VerifyTraitImplementation(ImplDeclaration impl)
    {
        if (impl.TraitName == null)
            return;

        if (!_traits.TryGetValue(impl.TraitName, out var trait))
        {
            _diagnostics.ReportError($"Undefined trait '{impl.TraitName}'.", impl.Span);
            return;
        }

        // Check that each trait method is implemented
        foreach (var traitMethod in trait.Methods)
        {
            var matchingMethod = impl.Methods.FirstOrDefault(m => m.Name == traitMethod.Name);
            if (matchingMethod == null)
            {
                _diagnostics.ReportError($"Type '{impl.StructName}' does not implement required method '{traitMethod.Name}' of trait '{impl.TraitName}'.", impl.Span);
                continue;
            }

            // Verify parameter count
            if (matchingMethod.Parameters.Count != traitMethod.Parameters.Count)
            {
                _diagnostics.ReportError($"Method '{matchingMethod.Name}' in '{impl.StructName}' has {matchingMethod.Parameters.Count} parameters, expected {traitMethod.Parameters.Count} from trait '{impl.TraitName}'.", matchingMethod.Span);
                continue;
            }

            // Verify parameter types
            for (int i = 0; i < traitMethod.Parameters.Count; i++)
            {
                var tp = traitMethod.Parameters[i];
                var mp = matchingMethod.Parameters[i];

                string expectedType = tp.TypeName == "Self" ? impl.StructName : tp.TypeName;
                if (!expectedType.Equals(mp.TypeName, StringComparison.Ordinal))
                {
                    _diagnostics.ReportError($"Method '{matchingMethod.Name}' parameter '{mp.Name}' has type '{mp.TypeName}', expected '{expectedType}' from trait '{impl.TraitName}'.", mp.Span);
                }
            }

            // Verify return type
            string expectedRet = (traitMethod.ReturnType == null || traitMethod.ReturnType == "void") ? "void" : (traitMethod.ReturnType == "Self" ? impl.StructName : traitMethod.ReturnType);
            string actualRet = (matchingMethod.ReturnType == null || matchingMethod.ReturnType == "void") ? "void" : matchingMethod.ReturnType;
            if (!expectedRet.Equals(actualRet, StringComparison.Ordinal))
            {
                _diagnostics.ReportError($"Method '{matchingMethod.Name}' return type '{actualRet}' does not match expected '{expectedRet}' from trait '{impl.TraitName}'.", matchingMethod.Span);
            }
        }

        // Record trait implementation
        if (!_traitImpls.TryGetValue(impl.StructName, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _traitImpls[impl.StructName] = set;
        }
        set.Add(impl.TraitName);
    }

    public string SubstituteTypeName(string typeName, IReadOnlyDictionary<string, string> typeMap)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        if (typeMap.TryGetValue(typeName, out var directSub))
            return directSub;

        if (typeName.StartsWith("[") && typeName.EndsWith("]"))
        {
            var inner = typeName.Substring(1, typeName.Length - 2);
            var parts = inner.Split(';');
            if (parts.Length == 2)
            {
                return $"[{SubstituteTypeName(parts[0].Trim(), typeMap)}; {parts[1].Trim()}]";
            }
            return $"[{SubstituteTypeName(inner.Trim(), typeMap)}]";
        }

        var typeSym = TypeSymbol.FromName(typeName);
        if (typeSym.TryGetGenericInfo(out var baseName, out var typeArgs))
        {
            var substitutedArgs = typeArgs.Select(a => SubstituteTypeName(a.Name, typeMap));
            return $"{baseName}<{string.Join(", ", substitutedArgs)}>";
        }

        return typeName;
    }

    public ExpressionNode SubstituteExpression(ExpressionNode expr, IReadOnlyDictionary<string, string> typeMap)
    {
        switch (expr)
        {
            case IdentifierExpression id:
                if (typeMap.TryGetValue(id.Name, out var subId))
                    return new IdentifierExpression(subId, id.Span);
                if (id.Name.Contains("<"))
                    return new IdentifierExpression(SubstituteTypeName(id.Name, typeMap), id.Span);
                return id;

            case CallExpression call:
                string subCallee = SubstituteTypeName(call.Callee, typeMap);
                var subArgs = call.Arguments.Select(a => SubstituteExpression(a, typeMap)).ToList();
                return new CallExpression(subCallee, subArgs, call.Span);

            case MemberAccessExpression mem:
                return new MemberAccessExpression(SubstituteExpression(mem.Target, typeMap), mem.MemberName, mem.Span);

            case MethodCallExpression mc:
                var mcSubArgs = mc.Arguments.Select(a => SubstituteExpression(a, typeMap)).ToList();
                return new MethodCallExpression(SubstituteExpression(mc.Target, typeMap), mc.MethodName, mcSubArgs, mc.Span);

            case BinaryExpression bin:
                return new BinaryExpression(SubstituteExpression(bin.Left, typeMap), bin.Operator, SubstituteExpression(bin.Right, typeMap), bin.Span);

            case UnaryExpression un:
                return new UnaryExpression(un.Operator, SubstituteExpression(un.Operand, typeMap), un.Span);

            case ArrayLiteralExpression arr:
                return new ArrayLiteralExpression(arr.Elements.Select(e => SubstituteExpression(e, typeMap)).ToList(), arr.Span);

            case IndexExpression idx:
                return new IndexExpression(SubstituteExpression(idx.Target, typeMap), SubstituteExpression(idx.Index, typeMap), idx.Span);

            case LambdaExpression lambda:
                var subParams = lambda.Parameters.Select(p => new LambdaParameter(p.Name, p.TypeName != null ? SubstituteTypeName(p.TypeName, typeMap) : null, p.Span)).ToList();
                var subRet = lambda.ReturnType != null ? SubstituteTypeName(lambda.ReturnType, typeMap) : null;
                var subBody = (BlockStatement)SubstituteStatement(lambda.Body, typeMap);
                return new LambdaExpression(subParams, subRet, subBody, lambda.Span);

            case IndirectCallExpression ind:
                return new IndirectCallExpression(SubstituteExpression(ind.Callee, typeMap), ind.Arguments.Select(a => SubstituteExpression(a, typeMap)).ToList(), ind.Span);

            default:
                return expr;
        }
    }

    public StatementNode SubstituteStatement(StatementNode stmt, IReadOnlyDictionary<string, string> typeMap)
    {
        switch (stmt)
        {
            case BlockStatement block:
                return new BlockStatement(block.Statements.Select(s => SubstituteStatement(s, typeMap)).ToList(), block.Span);

            case VariableDeclarationStatement v:
                return new VariableDeclarationStatement(
                    v.Name,
                    v.IsMutable,
                    v.TypeName != null ? SubstituteTypeName(v.TypeName, typeMap) : null,
                    SubstituteExpression(v.Initializer, typeMap),
                    v.Span);

            case AssignmentStatement a:
                return new AssignmentStatement(
                    a.TargetName,
                    a.MemberName,
                    a.Index != null ? SubstituteExpression(a.Index, typeMap) : null,
                    a.Op,
                    SubstituteExpression(a.Value, typeMap),
                    a.Span);

            case IfStatement ifStmt:
                return new IfStatement(
                    SubstituteExpression(ifStmt.Condition, typeMap),
                    (BlockStatement)SubstituteStatement(ifStmt.ThenBranch, typeMap),
                    ifStmt.ElseBranch != null ? SubstituteStatement(ifStmt.ElseBranch, typeMap) : null,
                    ifStmt.Span);

            case WhileStatement w:
                return new WhileStatement(
                    SubstituteExpression(w.Condition, typeMap),
                    (BlockStatement)SubstituteStatement(w.Body, typeMap),
                    w.Span);

            case ForStatement f:
                return new ForStatement(
                    f.VariableName,
                    SubstituteExpression(f.Start, typeMap),
                    SubstituteExpression(f.End, typeMap),
                    (BlockStatement)SubstituteStatement(f.Body, typeMap),
                    f.Span);

            case ReturnStatement ret:
                return new ReturnStatement(ret.Value != null ? SubstituteExpression(ret.Value, typeMap) : null, ret.Span);

            case ExpressionStatement es:
                return new ExpressionStatement(SubstituteExpression(es.Expression, typeMap), es.Span);

            case MatchStatement m:
                var subArms = m.Arms.Select(arm => new MatchArm(
                    SubstituteExpression(arm.Pattern, typeMap),
                    (BlockStatement)SubstituteStatement(arm.Body, typeMap),
                    arm.Span)).ToList();
                return new MatchStatement(SubstituteExpression(m.Scrutinee, typeMap), subArms, m.Span);

            default:
                return stmt;
        }
    }

    public TypeSymbol EnsureMonomorphizedType(string typeName, SourceSpan span = default)
    {
        var typeSym = TypeSymbol.FromName(typeName);
        if (!typeSym.TryGetGenericInfo(out var baseName, out var typeArgs))
        {
            if (typeSym.IsDynamicArray && typeSym.TryGetDynamicArrayElement(out var elemSym))
            {
                EnsureMonomorphizedType(elemSym.Name, span);
            }
            return typeSym;
        }

        // Recursively monomorphize type arguments
        var substitutedArgs = new List<TypeSymbol>();
        foreach (var arg in typeArgs)
        {
            substitutedArgs.Add(EnsureMonomorphizedType(arg.Name, span));
        }

        string normalizedName = $"{baseName}<{string.Join(", ", substitutedArgs.Select(a => a.Name))}>";
        string mangledName = TypeSymbol.ToMonomorphizedIdentifier(normalizedName);

        // Check if already monomorphized
        if (_structs.TryGetValue(normalizedName, out var existingStruct))
            return existingStruct.Type;
        if (_components.TryGetValue(normalizedName, out var existingComp))
            return existingComp.Type;

        // Monomorphize Generic Struct
        if (_genericStructs.TryGetValue(baseName, out var genericStruct))
        {
            if (genericStruct.TypeParameters != null && genericStruct.TypeParameters.Count != substitutedArgs.Count)
            {
                _diagnostics.ReportError($"Generic struct '{baseName}' expects {genericStruct.TypeParameters.Count} type arguments, got {substitutedArgs.Count}.", span);
                return TypeSymbol.Unknown;
            }

            // Verify trait constraints
            var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < substitutedArgs.Count; i++)
            {
                var param = genericStruct.TypeParameters![i];
                var arg = substitutedArgs[i];
                typeMap[param.Name] = arg.Name;

                if (param.ConstraintTrait != null)
                {
                    if (!(_traitImpls.TryGetValue(arg.Name, out var traits) && traits.Contains(param.ConstraintTrait)))
                    {
                        _diagnostics.ReportError($"Type '{arg.Name}' does not implement required trait '{param.ConstraintTrait}' for parameter '{param.Name}' in '{baseName}'.", span);
                    }
                }
            }

            // Substitute fields
            var concreteFields = new List<ComponentFieldSymbol>();
            var concreteDefs = new List<FieldDefinition>();
            foreach (var f in genericStruct.Fields)
            {
                string subFieldType = SubstituteTypeName(f.TypeName, typeMap);
                concreteFields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(subFieldType), f.Span));
                concreteDefs.Add(new FieldDefinition(f.Name, subFieldType, f.Span));
            }

            var stSym = new StructSymbol(normalizedName, concreteFields, genericStruct.Span);
            _structs[normalizedName] = stSym;
            _structs[mangledName] = stSym;

            var monomorphizedDecl = new StructDeclaration(mangledName, concreteDefs, genericStruct.Span);
            MonomorphizedDeclarations.Add(monomorphizedDecl);

            // Monomorphize associated impl blocks
            foreach (var impl in _genericImpls)
            {
                if (impl.StructName == baseName || impl.StructName.StartsWith($"{baseName}<"))
                {
                    MonomorphizeImplForStruct(impl, normalizedName, mangledName, typeMap);
                }
            }

            return stSym.Type;
        }

        // Monomorphize Generic Component
        if (_genericComponents.TryGetValue(baseName, out var genericComp))
        {
            if (genericComp.TypeParameters != null && genericComp.TypeParameters.Count != substitutedArgs.Count)
            {
                _diagnostics.ReportError($"Generic component '{baseName}' expects {genericComp.TypeParameters.Count} type arguments, got {substitutedArgs.Count}.", span);
                return TypeSymbol.Unknown;
            }

            var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < substitutedArgs.Count; i++)
            {
                var param = genericComp.TypeParameters![i];
                var arg = substitutedArgs[i];
                typeMap[param.Name] = arg.Name;

                if (param.ConstraintTrait != null)
                {
                    if (!(_traitImpls.TryGetValue(arg.Name, out var traits) && traits.Contains(param.ConstraintTrait)))
                    {
                        _diagnostics.ReportError($"Type '{arg.Name}' does not implement required trait '{param.ConstraintTrait}' for parameter '{param.Name}' in '{baseName}'.", span);
                    }
                }
            }

            var concreteFields = new List<ComponentFieldSymbol>();
            var concreteDefs = new List<FieldDefinition>();
            foreach (var f in genericComp.Fields)
            {
                string subFieldType = SubstituteTypeName(f.TypeName, typeMap);
                concreteFields.Add(new ComponentFieldSymbol(f.Name, TypeSymbol.FromName(subFieldType), f.Span));
                concreteDefs.Add(new FieldDefinition(f.Name, subFieldType, f.Span));
            }

            var compSym = new ComponentSymbol(normalizedName, concreteFields, genericComp.Span);
            _components[normalizedName] = compSym;
            _components[mangledName] = compSym;

            var monomorphizedDecl = new ComponentDeclaration(mangledName, concreteDefs, genericComp.Span);
            MonomorphizedDeclarations.Add(monomorphizedDecl);

            return compSym.Type;
        }

        return typeSym;
    }

    private void MonomorphizeImplForStruct(
        ImplDeclaration impl,
        string normalizedStructName,
        string mangledStructName,
        IReadOnlyDictionary<string, string> typeMap)
    {
        if (!_methods.TryGetValue(normalizedStructName, out var methodMap))
        {
            methodMap = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
            _methods[normalizedStructName] = methodMap;
            _methods[mangledStructName] = methodMap;
        }

        var specializedMethods = new List<FunctionDeclaration>();
        foreach (var method in impl.Methods)
        {
            var subParams = method.Parameters.Select((p, idx) =>
            {
                string pType = (idx == 0 && p.Name == "self")
                    ? normalizedStructName
                    : SubstituteTypeName(p.TypeName, typeMap);
                return new FunctionParameter(p.Name, pType, p.Span, p.IsMutable);
            }).ToList();

            string? retType = method.ReturnType != null ? SubstituteTypeName(method.ReturnType, typeMap) : null;
            var subBody = (BlockStatement)SubstituteStatement(method.Body, typeMap);

            var specializedMethod = new FunctionDeclaration(
                method.Name,
                subParams,
                retType,
                subBody,
                method.Span);

            methodMap[method.Name] = specializedMethod;
            _functions[$"{normalizedStructName}::{method.Name}"] = specializedMethod;
            _functions[$"{mangledStructName}_{method.Name}"] = specializedMethod;
            specializedMethods.Add(specializedMethod);
        }

        var specializedImpl = new ImplDeclaration(
            mangledStructName,
            specializedMethods,
            impl.Span,
            impl.TraitName);
        MonomorphizedDeclarations.Add(specializedImpl);

        if (impl.TraitName != null)
        {
            VerifyTraitImplementation(new ImplDeclaration(normalizedStructName, specializedMethods, impl.Span, impl.TraitName));
        }

        foreach (var sm in specializedMethods)
        {
            CheckFunction(sm);
        }
    }

    public FunctionDeclaration? EnsureMonomorphizedFunction(
        string callee,
        IReadOnlyList<TypeSymbol>? explicitTypeArgs,
        IReadOnlyList<TypeSymbol>? argumentTypes,
        SourceSpan span = default)
    {
        string baseName = callee;
        IReadOnlyList<TypeSymbol>? typeArgs = explicitTypeArgs;

        if (callee.Contains("<"))
        {
            if (TypeSymbol.FromName(callee).TryGetGenericInfo(out var bName, out var parsedArgs))
            {
                baseName = bName;
                typeArgs = parsedArgs;
            }
        }

        if (!_genericFunctions.TryGetValue(baseName, out var genericFn))
            return null;

        if (genericFn.TypeParameters == null || genericFn.TypeParameters.Count == 0)
            return genericFn;

        // Type inference if no explicit type arguments given
        if (typeArgs == null || typeArgs.Count == 0)
        {
            var inferredArgs = new List<TypeSymbol>();
            foreach (var tp in genericFn.TypeParameters)
            {
                TypeSymbol? inferred = null;
                for (int i = 0; i < genericFn.Parameters.Count; i++)
                {
                    if (genericFn.Parameters[i].TypeName == tp.Name && argumentTypes != null && i < argumentTypes.Count)
                    {
                        inferred = argumentTypes[i];
                        break;
                    }
                }
                if (inferred == null)
                {
                    _diagnostics.ReportError($"Cannot infer generic type argument '{tp.Name}' for function '{baseName}'.", span);
                    return null;
                }
                inferredArgs.Add(inferred);
            }
            typeArgs = inferredArgs;
        }

        if (typeArgs.Count != genericFn.TypeParameters.Count)
        {
            _diagnostics.ReportError($"Generic function '{baseName}' expects {genericFn.TypeParameters.Count} type arguments, got {typeArgs.Count}.", span);
            return null;
        }

        // Verify trait constraints
        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var param = genericFn.TypeParameters[i];
            var arg = typeArgs[i];
            typeMap[param.Name] = arg.Name;

            if (param.ConstraintTrait != null)
            {
                if (!(_traitImpls.TryGetValue(arg.Name, out var traits) && traits.Contains(param.ConstraintTrait)))
                {
                    _diagnostics.ReportError($"Type '{arg.Name}' does not implement required trait '{param.ConstraintTrait}' for parameter '{param.Name}' in '{baseName}'.", span);
                }
            }
        }

        string mangledName = $"{baseName}_{string.Join("_", typeArgs.Select(t => TypeSymbol.ToMonomorphizedIdentifier(t.Name)))}";
        string normalizedCallee = $"{baseName}<{string.Join(", ", typeArgs.Select(t => t.Name))}>";

        if (_functions.TryGetValue(mangledName, out var existing))
            return existing;

        var concreteParams = genericFn.Parameters.Select(p => new FunctionParameter(
            p.Name,
            SubstituteTypeName(p.TypeName, typeMap),
            p.Span,
            p.IsMutable)).ToList();

        string? retType = genericFn.ReturnType != null ? SubstituteTypeName(genericFn.ReturnType, typeMap) : null;
        var concreteBody = (BlockStatement)SubstituteStatement(genericFn.Body, typeMap);

        var specializedFn = new FunctionDeclaration(
            mangledName,
            concreteParams,
            retType,
            concreteBody,
            genericFn.Span);

        _functions[mangledName] = specializedFn;
        _functions[normalizedCallee] = specializedFn;
        MonomorphizedDeclarations.Add(specializedFn);

        CheckFunction(specializedFn);

        return specializedFn;
    }

    public SystemDeclaration? EnsureMonomorphizedSystem(
        string systemName,
        IReadOnlyList<TypeSymbol>? explicitTypeArgs,
        SourceSpan span = default)
    {
        string baseName = systemName;
        IReadOnlyList<TypeSymbol>? typeArgs = explicitTypeArgs;

        if (systemName.Contains("<"))
        {
            if (TypeSymbol.FromName(systemName).TryGetGenericInfo(out var bName, out var parsedArgs))
            {
                baseName = bName;
                typeArgs = parsedArgs;
            }
        }

        if (!_genericSystems.TryGetValue(baseName, out var genericSys))
            return null;

        if (genericSys.TypeParameters == null || genericSys.TypeParameters.Count == 0 || typeArgs == null)
            return genericSys;

        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var param = genericSys.TypeParameters[i];
            var arg = typeArgs[i];
            typeMap[param.Name] = arg.Name;

            if (param.ConstraintTrait != null)
            {
                if (!(_traitImpls.TryGetValue(arg.Name, out var traits) && traits.Contains(param.ConstraintTrait)))
                {
                    _diagnostics.ReportError($"Type '{arg.Name}' does not implement required trait '{param.ConstraintTrait}' for parameter '{param.Name}' in '{baseName}'.", span);
                }
            }
        }

        string mangledName = $"{baseName}_{string.Join("_", typeArgs.Select(t => TypeSymbol.ToMonomorphizedIdentifier(t.Name)))}";
        string normalizedSysName = $"{baseName}<{string.Join(", ", typeArgs.Select(t => t.Name))}>";

        var concreteQueryParams = genericSys.QueryParams.Select(p => new QueryParameter(
            p.IsMutable,
            p.Name,
            SubstituteTypeName(p.TypeName, typeMap),
            p.Span)).ToList();

        var concreteReadParams = genericSys.ReadParams.Select(p => new QueryParameter(
            p.IsMutable,
            p.Name,
            SubstituteTypeName(p.TypeName, typeMap),
            p.Span)).ToList();

        var concreteBody = (BlockStatement)SubstituteStatement(genericSys.Body, typeMap);

        var specializedSys = new SystemDeclaration(
            mangledName,
            concreteQueryParams,
            concreteReadParams,
            genericSys.Filters,
            concreteBody,
            genericSys.Span);

        MonomorphizedDeclarations.Add(specializedSys);
        CheckSystem(specializedSys);

        return specializedSys;
    }
}
