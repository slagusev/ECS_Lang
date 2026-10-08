using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
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
            CastExpressionNode castExpr => CheckCastExpression(castExpr),
            ResourceGetExpressionNode resGet => CheckResourceGetExpression(resGet),
            ErrorPropagationExpressionNode tryExpr => CheckErrorPropagationExpression(tryExpr),
            BulkSpawnExpressionNode bulkSpawn => CheckBulkSpawnExpression(bulkSpawn),
            _ => TypeSymbol.Unknown
        };

        _nodeTypes[expr] = type;
        return type;
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
        if (sym != null)
        {
            return sym.Type;
        }

        if (_constants.TryGetValue(ident.Name, out var constSym))
        {
            return constSym.Type;
        }

        _diagnostics.ReportError($"Undefined variable '{ident.Name}'.", ident.Span);
        return TypeSymbol.Unknown;
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
        if ((targetType == TypeSymbol.String || targetType == TypeSymbol.StrView) && mem.MemberName is "len" or "length")
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

        if (TryCheckTaggedUnionConstructor(call, out var tuType))
        {
            return tuType!;
        }

        if (TryCheckCollectionConstructor(call, out var collType))
        {
            return collType!;
        }

        if (call.Callee is "println" or "print")
        {
            return TypeSymbol.Void;
        }

        if (call.Callee is "readln" or "wait_key")
        {
            return TypeSymbol.I32;
        }

        if (TryCheckRaylibCall(call, out var rlType))
        {
            return rlType!;
        }

        // Built-in string functions
        if (call.Callee is "to_string" or "str_concat")
        {
            foreach (var arg in call.Arguments)
            {
                CheckExpression(arg);
            }
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

        // File I/O built-ins
        if (TryCheckFileIoCall(call, out var fileIoType))
        {
            return fileIoType;
        }

        // High-precision Time / Stopwatch built-ins
        if (TryCheckTimeCall(call, out var timeType))
        {
            return timeType;
        }

        // Networking built-ins
        if (call.Callee.StartsWith("raw_net_") ||
            (call.Callee.StartsWith("net_") && !_functions.ContainsKey(call.Callee)))
        {
            if (call.Callee is "net_init" or "raw_net_init" or "net_tcp_listen" or "raw_net_tcp_listen" or
                "net_tcp_connect" or "raw_net_tcp_connect" or "net_tcp_accept" or "raw_net_tcp_accept" or
                "net_tcp_send" or "raw_net_tcp_send" or "net_tcp_send_framed" or "raw_net_tcp_send_framed" or
                "net_buffer_read_i32" or "raw_net_buffer_read_i32" or
                "net_udp_bind" or "raw_net_udp_bind" or
                "net_udp_connect" or "raw_net_udp_connect" or "net_udp_send_to" or "raw_net_udp_send_to" or
                "net_get_last_error" or "raw_net_get_last_error")
            {
                return TypeSymbol.I32;
            }

            if (call.Callee is "net_tcp_recv" or "raw_net_tcp_recv" or "net_udp_recv_from" or "raw_net_udp_recv_from" or
                "net_buffer_extract_str" or "raw_net_buffer_extract_str")
            {
                return TypeSymbol.String;
            }

            if (call.Callee is "net_tcp_recv_append" or "raw_net_tcp_recv_append" or
                "net_buffer_drain" or "raw_net_buffer_drain")
            {
                return TypeSymbol.FromName("Vec<u8>");
            }

            if (call.Callee is "net_cleanup" or "raw_net_cleanup" or "net_close" or "raw_net_close" or "net_poll_wait" or "raw_net_poll_wait")
            {
                return TypeSymbol.Void;
            }
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

        if (call.Callee.Contains("<"))
        {
            EnsureMonomorphizedType(call.Callee, call.Span);
        }

        var argTypesList = call.Arguments.Select(GetNodeType).ToList();
        var monomorphizedFn = EnsureMonomorphizedFunction(call.Callee, null, argTypesList, call.Span);
        if (monomorphizedFn != null)
        {
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argType = GetNodeType(call.Arguments[i]);
                if (argType.ContainsCapturingClosure())
                {
                    _diagnostics.ReportError($"Capturing closure cannot be passed as an argument to function '{call.Callee}'. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Arguments[i].Span);
                }
            }
            return TypeSymbol.FromName(monomorphizedFn.ReturnType);
        }

        if (_structs.TryGetValue(call.Callee, out var stSym) ||
            _structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out stSym))
        {
            if (call.Arguments.Count != stSym.Fields.Count)
            {
                _diagnostics.ReportError($"Struct '{call.Callee}' constructor expects {stSym.Fields.Count} arguments, but got {call.Arguments.Count}.", call.Span);
            }
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argType = GetNodeType(call.Arguments[i]);
                if (argType.ContainsCapturingClosure())
                {
                    _diagnostics.ReportError("Capturing closure cannot be stored in a struct field. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Arguments[i].Span);
                }
            }
            return TypeSymbol.FromName(stSym.Name);
        }

        if (_components.TryGetValue(call.Callee, out var compSym) ||
            _components.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out compSym))
        {
            if (call.Arguments.Count != compSym.Fields.Count)
            {
                _diagnostics.ReportError($"Component '{call.Callee}' constructor expects {compSym.Fields.Count} arguments, but got {call.Arguments.Count}.", call.Span);
            }
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argType = GetNodeType(call.Arguments[i]);
                if (argType.ContainsCapturingClosure())
                {
                    _diagnostics.ReportError("Capturing closure cannot be stored in a component field. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Arguments[i].Span);
                }
            }
            return TypeSymbol.FromName(compSym.Name);
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
                        if (argType.ContainsCapturingClosure())
                        {
                            _diagnostics.ReportError("Capturing closure cannot be passed as an argument. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Arguments[i].Span);
                        }
                        else if (!AreTypesCompatible(paramTypes[i], argType))
                        {
                            _diagnostics.ReportError($"Argument {i + 1} of '{call.Callee}' expects '{paramTypes[i].Name}', but got '{argType.Name}'.", call.Arguments[i].Span);
                        }
                    }
                }
                return retType;
            }
        }

        if (_functions.TryGetValue(call.Callee, out var fnDecl) ||
            _functions.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out fnDecl))
        {
            if (call.Arguments.Count != fnDecl.Parameters.Count)
            {
                _diagnostics.ReportError($"Function '{call.Callee}' expects {fnDecl.Parameters.Count} arguments, but got {call.Arguments.Count}.", call.Span);
            }
            else
            {
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    var argType = GetNodeType(call.Arguments[i]);
                    if (argType.ContainsCapturingClosure())
                    {
                        _diagnostics.ReportError($"Capturing closure cannot be passed as an argument to function '{call.Callee}'. Closures with stack-allocated captures cannot escape their enclosing frame.", call.Arguments[i].Span);
                    }
                }
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

        if (targetType.IsGenericInstantiation)
        {
            EnsureMonomorphizedType(targetType.Name, methodCall.Span);
        }

        if ((_methods.TryGetValue(targetType.Name, out var methodMap) ||
             _methods.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(targetType.Name), out methodMap)) &&
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
                    if (argType.ContainsCapturingClosure())
                    {
                        _diagnostics.ReportError(
                            $"Capturing closure cannot be passed as an argument to method '{methodCall.MethodName}'. Closures with stack-allocated captures cannot escape their enclosing frame.",
                            methodCall.Arguments[i].Span);
                    }
                    else
                    {
                        var expectedParamType = TypeSymbol.FromName(methodDecl.Parameters[i + pOffset].TypeName);
                        if (!AreTypesCompatible(expectedParamType, argType))
                        {
                            _diagnostics.ReportError(
                                $"Argument {i + 1} of method '{methodCall.MethodName}' expects type '{expectedParamType.Name}', but got '{argType.Name}'.",
                                methodCall.Arguments[i].Span);
                        }
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
            if (_inSystemBody && targetType == TypeSymbol.World)
            {
                if (!methodCall.MethodName.StartsWith("emit_") && methodCall.MethodName != "emit")
                {
                    _diagnostics.ReportError(
                        $"Calling 'world.{methodCall.MethodName}' is forbidden inside system bodies. In systems, direct world access is restricted to 'world.emit_*' only. Use 'cmd.*' (Commands) for deferred mutations or query components via system parameters.",
                        methodCall.Span);
                    _nodeTypes[methodCall] = TypeSymbol.Unknown;
                    return TypeSymbol.Unknown;
                }
            }

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

        if (targetType == TypeSymbol.String && TryCheckStringMethod(methodCall, targetType, out var strMethodType))
        {
            _nodeTypes[methodCall] = strMethodType;
            return strMethodType;
        }

        if (targetType == TypeSymbol.StrView && TryCheckStrViewMethod(methodCall, targetType, out var strViewMethodType))
        {
            _nodeTypes[methodCall] = strViewMethodType;
            return strViewMethodType;
        }

        if (TryCheckCollectionMethodCall(methodCall, targetType, out var collMethodType))
        {
            return collMethodType!;
        }

        if (TryCheckTaggedUnionMethodCall(methodCall, targetType, out var tuMethodType))
        {
            return tuMethodType!;
        }

        _diagnostics.ReportError($"Type '{targetType.Name}' does not have a method '{methodCall.MethodName}'.", methodCall.Span);
        return TypeSymbol.Unknown;
    }
}
