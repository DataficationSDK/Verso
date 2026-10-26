namespace Verso.Abstractions;

/// <summary>
/// Augmentation interface for language kernels that can classify the symbols in a cell, so the
/// editor can color a type, a function and a value differently where a lexer alone sees the same
/// identifier. A kernel implements this alongside <see cref="ILanguageKernel"/>. Implementing
/// <see cref="ISemanticTokenProvider"/> alone is not a valid extension capability.
/// </summary>
/// <remarks>
/// The editor asks after the user pauses typing and when the cell's kernel state may have changed
/// (its run finished, the kernel restarted), and keeps the colors it already shows while a cell is
/// running. A classification is usually a by-product of the type check that
/// <see cref="ILanguageKernel.GetDiagnosticsAsync"/> runs on the same text, and the editor asks for
/// both after an edit, so a kernel answers both from one check of that text.
/// </remarks>
public interface ISemanticTokenProvider
{
    /// <summary>
    /// Classifies the symbols in <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The full source text of the cell.</param>
    /// <returns>
    /// The cell's tokens, in any order, or <c>null</c> when the code could not be analyzed. The
    /// editor keeps the colors it already shows on <c>null</c>; an empty list clears them.
    /// </returns>
    Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(string code);
}
