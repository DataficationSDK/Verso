using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="IExecutionContext"/> implementation extending <see cref="VersoContext"/>
/// with cell-specific execution state and display output routing.
/// </summary>
public sealed class ExecutionContext : VersoContext, IExecutionContext
{
    private readonly Func<CellOutput, Task> _display;
    private readonly Func<string, bool, CancellationToken, Task<string?>>? _requestInput;

    /// <summary>
    /// Builds a context with no in-place output updating, which is what a host that cannot revise
    /// an output provides.
    /// </summary>
    /// <remarks>
    /// Kept as its own overload rather than folded into the one below. An optional parameter is
    /// resolved where the call is compiled, so adding one to a shipped constructor changes the
    /// signature every existing compiled caller looks for.
    /// </remarks>
    public ExecutionContext(
        Guid cellId,
        int executionCount,
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook,
        Func<CellOutput, Task> writeOutput,
        Func<CellOutput, Task> display,
        Func<string, bool, CancellationToken, Task<string?>>? requestInput = null)
        : this(cellId, executionCount, variables, cancellationToken, theme, layoutCapabilities,
               extensionHost, notebookMetadata, notebook, writeOutput, display, requestInput,
               updateOutput: null)
    {
    }

    public ExecutionContext(
        Guid cellId,
        int executionCount,
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook,
        Func<CellOutput, Task> writeOutput,
        Func<CellOutput, Task> display,
        Func<string, bool, CancellationToken, Task<string?>>? requestInput,
        Func<string, CellOutput, Task>? updateOutput)
        : base(variables, cancellationToken, theme, layoutCapabilities, extensionHost, notebookMetadata, notebook, writeOutput, updateOutput)
    {
        CellId = cellId;
        ExecutionCount = executionCount;
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _requestInput = requestInput;
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

    /// <inheritdoc />
    public Task<string?> RequestInputAsync(
        string prompt,
        bool isPassword = false,
        CancellationToken cancellationToken = default)
    {
        if (_requestInput is null)
            throw new NotSupportedException("Interactive input is not supported by this host.");

        return _requestInput(prompt, isPassword, cancellationToken);
    }
}
