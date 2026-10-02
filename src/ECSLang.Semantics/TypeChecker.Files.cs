using System;
using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private bool TryCheckFileIoCall(CallExpression call, out TypeSymbol returnType)
    {
        switch (call.Callee)
        {
            case "file_exists":
                if (call.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Function 'file_exists' expects exactly 1 argument (path: string).", call.Span);
                }
                else
                {
                    var argType = CheckExpression(call.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_exists' expects 'string' as path, but got '{argType.Name}'.", call.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.Bool;
                return true;

            case "file_read_text":
                if (call.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Function 'file_read_text' expects exactly 1 argument (path: string).", call.Span);
                }
                else
                {
                    var argType = CheckExpression(call.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_read_text' expects 'string' as path, but got '{argType.Name}'.", call.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.FromName("Result<string, string>");
                return true;

            case "file_write_text":
                if (call.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Function 'file_write_text' expects exactly 2 arguments (path: string, content: string).", call.Span);
                }
                else
                {
                    var pathType = CheckExpression(call.Arguments[0]);
                    if (pathType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_write_text' expects 'string' as path, but got '{pathType.Name}'.", call.Arguments[0].Span);
                    }
                    var contentType = CheckExpression(call.Arguments[1]);
                    if (contentType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_write_text' expects 'string' as content, but got '{contentType.Name}'.", call.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.FromName("Result<bool, string>");
                return true;

            case "file_append_text":
                if (call.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Function 'file_append_text' expects exactly 2 arguments (path: string, line: string).", call.Span);
                }
                else
                {
                    var pathType = CheckExpression(call.Arguments[0]);
                    if (pathType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_append_text' expects 'string' as path, but got '{pathType.Name}'.", call.Arguments[0].Span);
                    }
                    var lineType = CheckExpression(call.Arguments[1]);
                    if (lineType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_append_text' expects 'string' as line, but got '{lineType.Name}'.", call.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.FromName("Result<bool, string>");
                return true;

            case "file_read_bin":
                if (call.Arguments.Count != 1)
                {
                    _diagnostics.ReportError("Function 'file_read_bin' expects exactly 1 argument (path: string).", call.Span);
                }
                else
                {
                    var argType = CheckExpression(call.Arguments[0]);
                    if (argType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_read_bin' expects 'string' as path, but got '{argType.Name}'.", call.Arguments[0].Span);
                    }
                }
                returnType = TypeSymbol.FromName("Result<Vec<u8>, string>");
                return true;

            case "file_write_bin":
                if (call.Arguments.Count != 2)
                {
                    _diagnostics.ReportError("Function 'file_write_bin' expects exactly 2 arguments (path: string, bytes: Vec<u8>).", call.Span);
                }
                else
                {
                    var pathType = CheckExpression(call.Arguments[0]);
                    if (pathType != TypeSymbol.String)
                    {
                        _diagnostics.ReportError($"Function 'file_write_bin' expects 'string' as path, but got '{pathType.Name}'.", call.Arguments[0].Span);
                    }
                    var bytesType = CheckExpression(call.Arguments[1]);
                    bool isBytes = (bytesType.TryGetDynamicArrayElement(out var elem) && (elem == TypeSymbol.U8 || elem.Name is "u8" or "byte" or "i8"))
                                || (bytesType.TryGetArrayInfo(out var fixedElem, out _) && (fixedElem == TypeSymbol.U8 || fixedElem.Name is "u8" or "byte" or "i8"))
                                || bytesType.Name is "[u8]" or "Vec<u8>" or "[byte]" or "Vec<byte>";
                    if (!isBytes)
                    {
                        _diagnostics.ReportError($"Function 'file_write_bin' expects 'Vec<u8>' as bytes, but got '{bytesType.Name}'.", call.Arguments[1].Span);
                    }
                }
                returnType = TypeSymbol.FromName("Result<bool, string>");
                return true;

            default:
                returnType = TypeSymbol.Unknown;
                return false;
        }
    }
}
