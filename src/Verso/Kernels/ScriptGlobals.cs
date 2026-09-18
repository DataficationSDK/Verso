using Verso.Abstractions;

namespace Verso.Kernels;

/// <summary>
/// Host object provided to Roslyn C# scripts, exposing the shared
/// <see cref="IVariableStore"/> as a top-level <c>Variables</c> identifier
/// so that C# cells can read data stored by other kernels (e.g. SQL results).
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>
    /// The shared variable store for this notebook session.
    /// </summary>
    public IVariableStore Variables { get; }

    /// <summary>
    /// The context of the cell that is running now, or <c>null</c> between cells.
    /// </summary>
    internal IExecutionContext? Context { get; set; }

    internal ScriptGlobals(IVariableStore variables)
    {
        Variables = variables ?? throw new ArgumentNullException(nameof(variables));
    }

    /// <summary>
    /// Asks the user for a line of text and waits for the answer.
    /// </summary>
    /// <param name="prompt">Text shown beside the input box.</param>
    /// <returns>The entered text, or <c>null</c> when the user cancels.</returns>
    /// <exception cref="NotSupportedException">The host has no interactive input, e.g. a headless run.</exception>
    public Task<string?> GetInputAsync(string prompt = "") => RequestAsync(prompt, isPassword: false);

    /// <summary>
    /// Asks the user for a line of text with the typed characters masked.
    /// </summary>
    /// <param name="prompt">Text shown beside the input box.</param>
    /// <returns>The entered text, or <c>null</c> when the user cancels.</returns>
    /// <exception cref="NotSupportedException">The host has no interactive input, e.g. a headless run.</exception>
    public Task<string?> GetPasswordAsync(string prompt = "") => RequestAsync(prompt, isPassword: true);

    private Task<string?> RequestAsync(string prompt, bool isPassword)
    {
        var context = Context
            ?? throw new InvalidOperationException("Input can only be requested while a cell is running.");

        return context.RequestInputAsync(prompt ?? "", isPassword, context.CancellationToken);
    }
}
