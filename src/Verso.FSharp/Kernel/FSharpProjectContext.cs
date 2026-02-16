using System.Text;
using FSharp.Compiler.CodeAnalysis;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Verso.FSharp.Kernel;

/// <summary>
/// Builds virtual F# documents for IntelliSense analysis.
/// Accumulates executed cell sources and constructs combined source text
/// with appropriate <see cref="FSharpProjectOptions"/> for the checker.
/// </summary>
internal sealed class FSharpProjectContext
{
    private const string VirtualFileName = "/verso/notebook.fsx";

    private static readonly HashSet<string> IgnoredArgs = new(StringComparer.OrdinalIgnoreCase)
    {
        "fsi.exe", "--noninteractive", "--nologo", "--gui-"
    };

    private readonly List<string> _executedSources = new();
    private readonly IReadOnlyList<string> _defaultOpens;
    private readonly List<string> _additionalReferences = new();
    private readonly string[] _sessionArgs;

    /// <summary>
    /// Creates a new project context for IntelliSense analysis.
    /// </summary>
    /// <param name="defaultOpens">Namespaces to open by default (e.g. System, System.Linq).</param>
    /// <param name="sessionArgs">The resolved FSI session arguments (compiler flags + references).</param>
    public FSharpProjectContext(IReadOnlyList<string> defaultOpens, string[] sessionArgs)
    {
        _defaultOpens = defaultOpens;
        _sessionArgs = sessionArgs;
    }

    /// <summary>
    /// Records a successfully executed cell source for future IntelliSense context.
    /// </summary>
    public void AppendExecutedCode(string code)
    {
        _executedSources.Add(code);
    }

    /// <summary>
    /// Adds a reference path for future IntelliSense resolution.
    /// </summary>
    public void AddReference(string assemblyPath)
    {
        _additionalReferences.Add(assemblyPath);
    }

    /// <summary>
    /// Builds a virtual document combining all previous executions with the current cell code.
    /// </summary>
    /// <returns>
    /// A tuple of (SourceText, PrefixLineCount, FSharpProjectOptions) where PrefixLineCount
    /// is the number of lines before the current cell code begins.
    /// </returns>
    public (string SourceText, int PrefixLineCount, FSharpProjectOptions Options) BuildDocument(string currentCellCode)
    {
        var prefixBuilder = new StringBuilder();

        // Add default opens
        foreach (var ns in _defaultOpens)
        {
            prefixBuilder.AppendLine($"open {ns}");
        }

        // Add previously executed sources
        foreach (var source in _executedSources)
        {
            prefixBuilder.AppendLine(source);
        }

        var prefix = prefixBuilder.ToString();
        var combinedSource = prefix + currentCellCode;

        // Count lines in prefix
        int prefixLineCount = 0;
        foreach (char c in prefix)
        {
            if (c == '\n') prefixLineCount++;
        }

        var options = BuildProjectOptions();

        return (combinedSource, prefixLineCount, options);
    }

    /// <summary>
    /// Clears all accumulated state (for kernel restart).
    /// </summary>
    public void Reset()
    {
        _executedSources.Clear();
        _additionalReferences.Clear();
    }

    private FSharpProjectOptions BuildProjectOptions()
    {
        var otherOptions = new List<string>();

        // Filter session args to only include compiler-relevant options
        foreach (var arg in _sessionArgs)
        {
            if (!IgnoredArgs.Contains(arg))
                otherOptions.Add(arg);
        }

        // Add additional references
        foreach (var refPath in _additionalReferences)
        {
            otherOptions.Add($"-r:{refPath}");
        }

        return new FSharpProjectOptions(
            projectFileName: VirtualFileName,
            projectId: FSharpOption<string>.None,
            sourceFiles: new[] { VirtualFileName },
            otherOptions: otherOptions.ToArray(),
            referencedProjects: Array.Empty<FSharpReferencedProject>(),
            isIncompleteTypeCheckEnvironment: false,
            useScriptResolutionRules: true,
            loadTime: DateTime.UtcNow,
            unresolvedReferences: FSharpOption<FSharpUnresolvedReferencesSet>.None,
            originalLoadReferences: FSharpList<Tuple<global::FSharp.Compiler.Text.Range, string, string>>.Empty,
            stamp: FSharpOption<long>.None);
    }
}
