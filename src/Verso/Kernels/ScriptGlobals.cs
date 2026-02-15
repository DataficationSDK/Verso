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

    internal ScriptGlobals(IVariableStore variables)
    {
        Variables = variables ?? throw new ArgumentNullException(nameof(variables));
    }
}
