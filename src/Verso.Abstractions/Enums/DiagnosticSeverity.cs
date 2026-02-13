namespace Verso.Abstractions;

/// <summary>
/// Indicates the severity level of a diagnostic message reported by the notebook or a cell.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// A diagnostic that is not displayed to the user but may be logged internally.
    /// </summary>
    Hidden,

    /// <summary>
    /// An informational message that does not indicate a problem.
    /// </summary>
    Info,

    /// <summary>
    /// A warning that indicates a potential issue which does not prevent execution.
    /// </summary>
    Warning,

    /// <summary>
    /// An error that indicates a problem which prevents correct execution.
    /// </summary>
    Error
}
