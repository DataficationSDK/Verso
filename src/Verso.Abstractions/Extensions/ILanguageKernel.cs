namespace Verso.Abstractions;

/// <summary>
/// Represents a language execution kernel that can run code, provide completions,
/// diagnostics, and hover information. Each kernel targets a single language
/// and manages its own runtime state.
/// </summary>
public interface ILanguageKernel : IExtension, IAsyncDisposable
{
    /// <summary>
    /// Language identifier used to associate cells with this kernel (e.g. "csharp", "python").
    /// </summary>
    string LanguageId { get; }

    /// <summary>
    /// Human-readable name for the language shown in cell type selectors and status displays.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// File extensions associated with this language (e.g. ".cs", ".py"), used for import and export.
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Performs one-time initialization of the kernel runtime. Called once before any execution requests.
    /// </summary>
    /// <returns>A task that completes when the kernel is ready to execute code.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Executes the supplied code and returns the resulting outputs.
    /// </summary>
    /// <param name="code">Source code to execute.</param>
    /// <param name="context">Execution context providing cancellation, display, and variable-sharing APIs.</param>
    /// <returns>An ordered list of cell outputs produced by the execution.</returns>
    Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context);

    /// <summary>
    /// Returns completion suggestions for the given code at the specified cursor position.
    /// </summary>
    /// <param name="code">The full source text of the cell.</param>
    /// <param name="cursorPosition">Zero-based character offset of the cursor within <paramref name="code"/>.</param>
    /// <returns>A list of completion items applicable at the cursor position.</returns>
    Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition);

    /// <summary>
    /// Analyzes the code and returns any diagnostics (errors, warnings, hints).
    /// </summary>
    /// <param name="code">The full source text to analyze.</param>
    /// <returns>A list of diagnostics found in the code.</returns>
    Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code);

    /// <summary>
    /// Returns hover information (type details, documentation) for the symbol at the cursor position.
    /// </summary>
    /// <param name="code">The full source text of the cell.</param>
    /// <param name="cursorPosition">Zero-based character offset of the cursor within <paramref name="code"/>.</param>
    /// <returns>Hover information for the symbol, or <c>null</c> if nothing is available at that position.</returns>
    Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition);
}
