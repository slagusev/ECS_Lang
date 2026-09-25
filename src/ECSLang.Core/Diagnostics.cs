namespace ECSLang.Core;

public enum DiagnosticSeverity
{
    Warning,
    Error
}

public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, SourceSpan Span)
{
    public override string ToString() =>
        $"[{Severity.ToString().ToUpperInvariant()}] {Span}: {Message}";
}

public sealed class DiagnosticsBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Items => _diagnostics;
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Report(DiagnosticSeverity severity, string message, SourceSpan span)
    {
        _diagnostics.Add(new Diagnostic(severity, message, span));
    }

    public void ReportError(string message, SourceSpan span) =>
        Report(DiagnosticSeverity.Error, message, span);

    public void ReportWarning(string message, SourceSpan span) =>
        Report(DiagnosticSeverity.Warning, message, span);

    public void PrintToConsole()
    {
        foreach (var diag in _diagnostics)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = diag.Severity switch
            {
                DiagnosticSeverity.Error => ConsoleColor.Red,
                DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };

            Console.WriteLine(diag);
            Console.ForegroundColor = originalColor;
        }
    }
}
