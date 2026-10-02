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

        if (call.Callee is "Ok" or "Result::Ok")
        {
            if (call.Arguments.Count != 1)
            {
                _diagnostics.ReportError("Constructor 'Ok' expects 1 argument.", call.Span);
                return TypeSymbol.CreateResult(TypeSymbol.Unknown, TypeSymbol.String);
            }
            var argType = GetNodeType(call.Arguments[0]);
            return TypeSymbol.CreateResult(argType, TypeSymbol.String);
        }

        if (call.Callee is "Err" or "Result::Err")
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
            return TypeSymbol.FromName(monomorphizedFn.ReturnType);
        }

        if (_structs.TryGetValue(call.Callee, out var stSym) ||
            _structs.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(call.Callee), out stSym))
        {
            if (call.Arguments.Count != stSym.Fields.Count)
            {
                _diagnostics.ReportError($"Struct '{call.Callee}' constructor expects {stSym.Fields.Count} arguments, but got {call.Arguments.Count}.", call.Span);
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
                        if (!AreTypesCompatible(paramTypes[i], argType))
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
