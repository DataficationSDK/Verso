using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// Base <see cref="IVersoContext"/> implementation. Output routing is provided via an injected delegate.
/// </summary>
public class VersoContext : IVersoContext
{
    private readonly Func<CellOutput, Task> _writeOutput;
    private readonly Func<string, CellOutput, Task>? _updateOutput;

    /// <summary>
    /// Builds a context with no in-place output updating, which is what a host that cannot revise
    /// an output provides.
    /// </summary>
    /// <remarks>
    /// Kept as its own overload rather than folded into the one below. An optional parameter is
    /// resolved where the call is compiled, so adding one to a shipped constructor changes the
    /// signature every existing compiled caller looks for.
    /// </remarks>
    public VersoContext(
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook,
        Func<CellOutput, Task> writeOutput)
        : this(variables, cancellationToken, theme, layoutCapabilities, extensionHost,
               notebookMetadata, notebook, writeOutput, updateOutput: null)
    {
    }

    public VersoContext(
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook,
        Func<CellOutput, Task> writeOutput,
        Func<string, CellOutput, Task>? updateOutput)
    {
        Variables = variables ?? throw new ArgumentNullException(nameof(variables));
        CancellationToken = cancellationToken;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        LayoutCapabilities = layoutCapabilities;
        ExtensionHost = extensionHost ?? throw new ArgumentNullException(nameof(extensionHost));
        NotebookMetadata = notebookMetadata ?? throw new ArgumentNullException(nameof(notebookMetadata));
        Notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _writeOutput = writeOutput ?? throw new ArgumentNullException(nameof(writeOutput));
        _updateOutput = updateOutput;
    }

    /// <inheritdoc />
    public IVariableStore Variables { get; }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public IThemeContext Theme { get; }

    /// <inheritdoc />
    public LayoutCapabilities LayoutCapabilities { get; }

    /// <inheritdoc />
    public IExtensionHostContext ExtensionHost { get; }

    /// <inheritdoc />
    public INotebookMetadata NotebookMetadata { get; }

    /// <inheritdoc />
    public INotebookOperations Notebook { get; }

    /// <inheritdoc />
    public Task WriteOutputAsync(CellOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _writeOutput(output);
    }

    /// <inheritdoc />
    public Task UpdateOutputAsync(string outputBlockId, CellOutput output)
    {
        ArgumentNullException.ThrowIfNull(outputBlockId);
        ArgumentNullException.ThrowIfNull(output);

        if (_updateOutput is null)
            throw new NotSupportedException("In-place output update is not supported by this host.");

        return _updateOutput(outputBlockId, output);
    }
}
