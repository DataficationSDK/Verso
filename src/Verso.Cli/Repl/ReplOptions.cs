namespace Verso.Cli.Repl;

/// <summary>
/// Parsed, validated options for a single <c>verso repl</c> invocation.
/// </summary>
public sealed class ReplOptions
{
    public string? NotebookPath { get; init; }
    public string? KernelId { get; init; }
    public string? ThemeName { get; init; }
    public string? LayoutId { get; init; }
    public string? ExtensionsDirectory { get; init; }
    public bool Execute { get; init; }
    public bool NoColor { get; init; }
    public bool Plain { get; init; }
    public string? HistoryPath { get; init; }
    public bool HistoryDisabled { get; init; }
}
