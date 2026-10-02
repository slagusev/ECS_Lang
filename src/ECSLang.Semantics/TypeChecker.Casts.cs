using System;
using System.Globalization;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private TypeSymbol CheckCastExpression(CastExpressionNode cast)
    {
        var srcType = CheckExpression(cast.Expr);
        var targetType = TypeSymbol.FromName(cast.TargetTypeName);

        if (srcType == TypeSymbol.Unknown || targetType == TypeSymbol.Unknown)
        {
            return targetType;
        }

        bool srcIsNumeric = srcType.IsInteger || srcType.IsFloatingPoint || srcType == TypeSymbol.Bool || srcType == TypeSymbol.Entity;
        bool targetIsNumeric = targetType.IsInteger || targetType.IsFloatingPoint || targetType == TypeSymbol.Bool || targetType == TypeSymbol.Entity;

        if (srcType == targetType || (srcIsNumeric && targetIsNumeric))
        {
            return targetType;
        }

        _diagnostics.ReportError($"Cannot cast expression of type '{srcType.Name}' to '{targetType.Name}'.", cast.Span);
        return targetType;
    }

    private bool EvaluateConstantExpression(ExpressionNode expr, out TypeSymbol type, out object value)
    {
        switch (expr)
        {
            case NumberLiteralExpression num:
            {
                string text = num.RawValue;
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexVal))
                    {
                        if (hexVal >= int.MinValue && hexVal <= int.MaxValue)
                        {
                            type = TypeSymbol.I32;
                            value = (int)hexVal;
                            return true;
                        }
                        type = TypeSymbol.I64;
                        value = hexVal;
                        return true;
                    }
                    if (ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong uhexVal))
                    {
                        type = TypeSymbol.I64;
                        value = (long)uhexVal;
                        return true;
                    }
                }
                else if (text.Contains('.'))
                {
                    if (double.TryParse(text, CultureInfo.InvariantCulture, out double dVal))
                    {
                        type = TypeSymbol.F32;
                        value = (float)dVal;
                        return true;
                    }
                }
                else
                {
                    if (int.TryParse(text, CultureInfo.InvariantCulture, out int iVal))
                    {
                        type = TypeSymbol.I32;
                        value = iVal;
                        return true;
                    }
                    if (long.TryParse(text, CultureInfo.InvariantCulture, out long lVal))
                    {
                        type = TypeSymbol.I64;
                        value = lVal;
                        return true;
                    }
                }
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
            }

            case BooleanLiteralExpression b:
            {
                type = TypeSymbol.Bool;
                value = b.Value;
                return true;
            }

            case StringLiteralExpression s:
            {
                type = TypeSymbol.String;
                value = s.Value;
                return true;
            }

            case UnaryExpression u when u.Operator == UnaryOperator.Negate:
            {
                if (EvaluateConstantExpression(u.Operand, out var subType, out var subVal))
                {
                    if (subVal is int iv)
                    {
                        type = subType;
                        value = -iv;
                        return true;
                    }
                    if (subVal is long lv)
                    {
                        type = subType;
                        value = -lv;
                        return true;
                    }
                    if (subVal is float fv)
                    {
                        type = subType;
                        value = -fv;
                        return true;
                    }
                    if (subVal is double dv)
                    {
                        type = subType;
                        value = -dv;
                        return true;
                    }
                }
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
            }

            case UnaryExpression u when u.Operator == UnaryOperator.LogicalNot:
            {
                if (EvaluateConstantExpression(u.Operand, out var subType, out var subVal) && subVal is bool bv)
                {
                    type = TypeSymbol.Bool;
                    value = !bv;
                    return true;
                }
                type = TypeSymbol.Unknown;
                value = false;
                return false;
            }

            case IdentifierExpression ident:
            {
                if (_constants.TryGetValue(ident.Name, out var existing))
                {
                    type = existing.Type;
                    value = existing.Value;
                    return true;
                }
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
            }

            case BinaryExpression bin:
            {
                if (EvaluateConstantExpression(bin.Left, out var lType, out var lVal) &&
                    EvaluateConstantExpression(bin.Right, out var rType, out var rVal))
                {
                    if (lVal is int li && rVal is int ri)
                    {
                        type = TypeSymbol.I32;
                        value = bin.Operator switch
                        {
                            BinaryOperator.Add => li + ri,
                            BinaryOperator.Subtract => li - ri,
                            BinaryOperator.Multiply => li * ri,
                            BinaryOperator.Divide => ri != 0 ? li / ri : 0,
                            BinaryOperator.Modulo => ri != 0 ? li % ri : 0,
                            _ => 0
                        };
                        return true;
                    }
                    if (lVal is float lf && rVal is float rf)
                    {
                        type = TypeSymbol.F32;
                        value = bin.Operator switch
                        {
                            BinaryOperator.Add => lf + rf,
                            BinaryOperator.Subtract => lf - rf,
                            BinaryOperator.Multiply => lf * rf,
                            BinaryOperator.Divide => rf != 0 ? lf / rf : 0.0f,
                            _ => 0.0f
                        };
                        return true;
                    }
                }
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
            }

            case CastExpressionNode castNode:
            {
                if (EvaluateConstantExpression(castNode.Expr, out var cType, out var cVal))
                {
                    var targetType = TypeSymbol.FromName(castNode.TargetTypeName);
                    if (targetType == TypeSymbol.I32)
                    {
                        type = TypeSymbol.I32;
                        value = Convert.ToInt32(cVal);
                        return true;
                    }
                    if (targetType == TypeSymbol.I64)
                    {
                        type = TypeSymbol.I64;
                        value = Convert.ToInt64(cVal);
                        return true;
                    }
                    if (targetType == TypeSymbol.F32)
                    {
                        type = TypeSymbol.F32;
                        value = Convert.ToSingle(cVal);
                        return true;
                    }
                    if (targetType == TypeSymbol.F64)
                    {
                        type = TypeSymbol.F64;
                        value = Convert.ToDouble(cVal);
                        return true;
                    }
                    if (targetType == TypeSymbol.Bool)
                    {
                        type = TypeSymbol.Bool;
                        value = Convert.ToBoolean(cVal);
                        return true;
                    }
                }
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
            }

            default:
                type = TypeSymbol.Unknown;
                value = 0;
                return false;
        }
    }
}
