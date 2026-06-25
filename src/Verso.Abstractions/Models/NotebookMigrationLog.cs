namespace Verso.Abstractions;

/// <summary>
/// Collects the outcome of running notebook format migrations: an ordered record of the steps
/// applied and any warnings (for example, a document declaring a newer format than this build
/// supports). Callers surface these to logs or diagnostics.
/// </summary>
public sealed class NotebookMigrationLog
{
    private readonly List<string> _steps = new();
    private readonly List<string> _warnings = new();

    /// <summary>Descriptions of the migration steps that were applied, in order.</summary>
    public IReadOnlyList<string> Steps => _steps;

    /// <summary>Warnings raised while migrating, if any.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>True when at least one warning was recorded.</summary>
    public bool HasWarnings => _warnings.Count > 0;

    /// <summary>Records an applied migration step.</summary>
    public void AddStep(string description) => _steps.Add(description);

    /// <summary>Records a warning.</summary>
    public void AddWarning(string message) => _warnings.Add(message);
}
