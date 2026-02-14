using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="IMagicCommandContext"/> implementation extending <see cref="VersoContext"/>
/// with magic-command-specific state: remaining code after the directive and suppression control.
/// </summary>
public sealed class MagicCommandContext : VersoContext, IMagicCommandContext
{
    public MagicCommandContext(
        string remainingCode,
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook,
        Func<CellOutput, Task> writeOutput)
        : base(variables, cancellationToken, theme, layoutCapabilities, extensionHost, notebookMetadata, notebook, writeOutput)
    {
        RemainingCode = remainingCode ?? "";
    }

    /// <inheritdoc />
    public string RemainingCode { get; }

    /// <inheritdoc />
    public bool SuppressExecution { get; set; }

    /// <summary>
    /// When set to <c>true</c>, the pipeline reports elapsed time after kernel execution.
    /// Used by the <c>#!time</c> magic command.
    /// </summary>
    internal bool ReportElapsedTime { get; set; }
}
