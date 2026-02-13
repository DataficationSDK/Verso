using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// Base <see cref="IVersoContext"/> implementation. Output routing is provided via an injected delegate.
/// </summary>
public class VersoContext : IVersoContext
{
    private readonly Func<CellOutput, Task> _writeOutput;

    public VersoContext(
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        Func<CellOutput, Task> writeOutput)
    {
        Variables = variables ?? throw new ArgumentNullException(nameof(variables));
        CancellationToken = cancellationToken;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        LayoutCapabilities = layoutCapabilities;
        ExtensionHost = extensionHost ?? throw new ArgumentNullException(nameof(extensionHost));
        NotebookMetadata = notebookMetadata ?? throw new ArgumentNullException(nameof(notebookMetadata));
        _writeOutput = writeOutput ?? throw new ArgumentNullException(nameof(writeOutput));
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
    public Task WriteOutputAsync(CellOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _writeOutput(output);
    }
}
