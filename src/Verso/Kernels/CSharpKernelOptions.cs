namespace Verso.Kernels;

/// <summary>
/// Configuration options for the C# language kernel.
/// </summary>
/// <param name="DefaultImports">Namespace imports automatically available in every cell.</param>
/// <param name="DefaultReferences">Assembly references automatically loaded for every cell.</param>
public sealed record CSharpKernelOptions(
    IReadOnlyList<string>? DefaultImports = null,
    IReadOnlyList<string>? DefaultReferences = null)
{
    /// <summary>
    /// Standard set of imports matching typical C# interactive sessions.
    /// </summary>
    public static readonly IReadOnlyList<string> StandardImports = new[]
    {
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Text",
        "System.Threading.Tasks"
    };
}
