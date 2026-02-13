using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="IExecutionContext"/> implementation extending <see cref="VersoContext"/>
/// with cell-specific execution state and display output routing.
/// </summary>
public sealed class ExecutionContext : VersoContext, IExecutionContext
{
    private readonly Func<CellOutput, Task> _display;

    public ExecutionContext(
        Guid cellId,
        int executionCount,
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        Func<CellOutput, Task> writeOutput,
        Func<CellOutput, Task> display)
        : base(variables, cancellationToken, theme, layoutCapabilities, extensionHost, notebookMetadata, writeOutput)
    {
        CellId = cellId;
        ExecutionCount = executionCount;
        _display = display ?? throw new ArgumentNullException(nameof(display));
    }

    /// <inheritdoc />
    public Guid CellId { get; }

    /// <inheritdoc />
    public int ExecutionCount { get; }

    /// <inheritdoc />
    public Task DisplayAsync(CellOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _display(output);
    }
}
