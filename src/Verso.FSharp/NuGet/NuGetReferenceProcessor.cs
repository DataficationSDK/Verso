using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.FSharp.Kernel;

namespace Verso.FSharp.NuGet;

/// <summary>
/// Result of processing NuGet directives in a code cell.
/// </summary>
internal sealed record NuGetProcessResult(
    string ProcessedCode,
    List<FSharpNuGetResolveResult> ResolvedPackages,
    List<string> NewAssemblyPaths);

/// <summary>
/// Orchestrates NuGet package resolution for the F# kernel. Detects whether FSI natively
/// supports <c>#r "nuget:"</c> directives and routes to either the FSI built-in handler
/// or the standalone <see cref="NuGetFallbackResolver"/>.
/// </summary>
internal sealed class NuGetReferenceProcessor
{
    /// <summary>
    /// Duplicated from NuGetMagicCommand.AssemblyStoreKey (which is in Verso core,
    /// not referenced by Verso.FSharp).
    /// </summary>
    internal const string AssemblyStoreKey = "__verso_nuget_assemblies";

    /// <summary>
    /// Duplicated from NuGetMagicCommand.ResolvedPackagesStoreKey.
    /// </summary>
    internal const string ResolvedPackagesStoreKey = "__verso_nuget_resolved_packages";

    private static readonly Regex NuGetDirectiveRegex = new(
        @"^#r\s+""nuget:\s*([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly NuGetFallbackResolver _resolver = new();
    private readonly HashSet<string> _resolvedAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _useFsiNuGet;

    /// <summary>
    /// Whether FSI natively supports <c>#r "nuget:"</c> directives.
    /// </summary>
    internal bool UsesFsiNuGet => _useFsiNuGet;

    /// <summary>
    /// All assembly paths resolved across all executions.
    /// </summary>
    public IReadOnlyCollection<string> ResolvedAssemblyPaths => _resolvedAssemblyPaths;

    /// <summary>
    /// Probes whether the current FSI session supports native NuGet resolution.
    /// Called once during kernel initialization.
    /// </summary>
    public void ProbeNuGetSupport(FsiSessionManager session)
    {
        try
        {
            // Try evaluating a NuGet directive for a package that's always available.
            // Use EvalInteraction (not EvalSilent) so we can inspect whether it produced
            // compilation errors — EvalSilent uses NonThrowing and swallows errors.
            var result = session.EvalInteraction("#r \"nuget: FSharp.Core\"", CancellationToken.None);
            _useFsiNuGet = !result.HasCompilationErrors;
        }
        catch
        {
            _useFsiNuGet = false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the code contains any <c>#r "nuget:"</c> directives.
    /// </summary>
    public static bool ContainsNuGetDirectives(string code)
    {
        return NuGetDirectiveRegex.IsMatch(code);
    }

    /// <summary>
    /// Processes NuGet directives in the code cell. If FSI supports native NuGet, the
    /// directives are left in place. Otherwise, packages are resolved via the fallback
    /// resolver and injected into the session via <c>#r</c> directives.
    /// </summary>
    public async Task<NuGetProcessResult> ProcessAsync(
        string code, FsiSessionManager session, CancellationToken ct)
    {
        var matches = NuGetDirectiveRegex.Matches(code);
        if (matches.Count == 0)
            return new NuGetProcessResult(code, new List<FSharpNuGetResolveResult>(), new List<string>());

        if (_useFsiNuGet)
        {
            // Leave directives in code for FSI to handle natively
            return new NuGetProcessResult(code, new List<FSharpNuGetResolveResult>(), new List<string>());
        }

        // Fallback: resolve via our standalone resolver
        var results = new List<FSharpNuGetResolveResult>();
        var newPaths = new List<string>();

        foreach (Match match in matches)
        {
            var directive = match.Groups[1].Value;
            var parsed = NuGetFallbackResolver.ParseNuGetReference(directive);
            if (parsed is null) continue;

            var result = await _resolver.ResolvePackageAsync(
                parsed.Value.PackageId, parsed.Value.Version, ct).ConfigureAwait(false);
            results.Add(result);

            // Inject each resolved assembly into the FSI session
            foreach (var assemblyPath in result.AssemblyPaths)
            {
                if (_resolvedAssemblyPaths.Add(assemblyPath))
                {
                    newPaths.Add(assemblyPath);
                    session.EvalSilent($"#r \"{assemblyPath.Replace('\\', '/')}\"");
                }
            }
        }

        // Strip NuGet directives from the code
        var processedCode = NuGetDirectiveRegex.Replace(code, "").Trim();
        return new NuGetProcessResult(processedCode, results, newPaths);
    }

    /// <summary>
    /// Checks the variable store for assembly paths deposited by the <c>#!nuget</c>
    /// magic command and returns them for injection into the FSI session.
    /// </summary>
    public List<string> CheckMagicCommandResults(IVariableStore variables)
    {
        var paths = new List<string>();

        if (variables.TryGet<List<string>>(AssemblyStoreKey, out var assemblyPaths)
            && assemblyPaths is { Count: > 0 })
        {
            foreach (var path in assemblyPaths)
            {
                if (_resolvedAssemblyPaths.Add(path))
                    paths.Add(path);
            }

            variables.Remove(AssemblyStoreKey);
        }

        return paths;
    }
}
