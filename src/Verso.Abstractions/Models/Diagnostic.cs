namespace Verso.Abstractions;

/// <summary>
/// Represents a code diagnostic (error, warning, or informational message) reported by a kernel's language service.
/// </summary>
/// <param name="Severity">The severity level of the diagnostic.</param>
/// <param name="Message">A human-readable description of the issue.</param>
/// <param name="StartLine">The zero-based line number where the diagnostic span begins.</param>
/// <param name="StartColumn">The zero-based column number where the diagnostic span begins.</param>
/// <param name="EndLine">The zero-based line number where the diagnostic span ends.</param>
/// <param name="EndColumn">The zero-based column number where the diagnostic span ends.</param>
/// <param name="Code">An optional diagnostic code or identifier (e.g. "CS1002").</param>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? Code = null);
