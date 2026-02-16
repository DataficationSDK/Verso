using FSharp.Compiler.Diagnostics;
using Verso.Abstractions;

using VersoDiagnostic = Verso.Abstractions.Diagnostic;
using VersoDiagnosticSeverity = Verso.Abstractions.DiagnosticSeverity;

namespace Verso.FSharp.Helpers;

/// <summary>
/// Maps FSharp.Compiler.Service diagnostics to Verso diagnostic model objects.
/// Handles coordinate system conversion (FCS 1-based lines → Verso 0-based) and
/// prefix offset adjustment for virtual documents.
/// </summary>
internal static class DiagnosticMapper
{
    /// <summary>
    /// Maps FCS severity to Verso severity. Returns <c>null</c> for Hidden diagnostics.
    /// </summary>
    public static VersoDiagnosticSeverity? MapSeverity(FSharpDiagnosticSeverity severity)
    {
        if (severity.IsError) return VersoDiagnosticSeverity.Error;
        if (severity.IsWarning) return VersoDiagnosticSeverity.Warning;
        if (severity.IsInfo) return VersoDiagnosticSeverity.Info;
        return null; // Hidden
    }

    /// <summary>
    /// Formats an FCS error number as an F# diagnostic code (e.g. "FS0039").
    /// </summary>
    public static string FormatCode(int errorNumber) => $"FS{errorNumber:D4}";

    /// <summary>
    /// Maps an <see cref="FSharpDiagnostic"/> to a Verso <see cref="VersoDiagnostic"/>,
    /// adjusting line numbers to be relative to the current cell.
    /// Returns <c>null</c> if the diagnostic is hidden or outside the current cell region.
    /// </summary>
    /// <param name="diag">The FCS diagnostic.</param>
    /// <param name="prefixLineCount">Number of prefix lines (default opens + previously executed code).</param>
    /// <param name="cellLineCount">Number of lines in the current cell code.</param>
    public static VersoDiagnostic? MapDiagnostic(FSharpDiagnostic diag, int prefixLineCount, int cellLineCount)
    {
        var severity = MapSeverity(diag.Severity);
        if (severity is null) return null;

        // FCS lines are 1-based; convert to 0-based
        int startLine = diag.StartLine - 1;
        int endLine = diag.EndLine - 1;

        // Filter: only include diagnostics within the current cell region
        if (startLine < prefixLineCount) return null;
        if (startLine >= prefixLineCount + cellLineCount) return null;

        // Adjust lines relative to cell start
        int cellStartLine = startLine - prefixLineCount;
        int cellEndLine = endLine - prefixLineCount;

        // Clamp end line to cell bounds
        if (cellEndLine >= cellLineCount)
            cellEndLine = cellLineCount - 1;

        // FCS columns are 0-based — no change needed
        return new VersoDiagnostic(
            Severity: severity.Value,
            Message: diag.Message,
            StartLine: cellStartLine,
            StartColumn: diag.StartColumn,
            EndLine: cellEndLine,
            EndColumn: diag.EndColumn,
            Code: FormatCode(diag.ErrorNumber));
    }
}
