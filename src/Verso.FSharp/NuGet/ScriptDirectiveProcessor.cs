using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.FSharp.NuGet;

/// <summary>
/// Processes non-NuGet F# script directives (<c>#r</c>, <c>#load</c>, <c>#I</c>,
/// <c>#nowarn</c>, <c>#time</c>) by resolving relative paths and tracking state
/// for IntelliSense integration.
/// </summary>
internal sealed class ScriptDirectiveProcessor
{
    private static readonly Regex AssemblyRefRegex = new(
        @"^#r\s+""(?!nuget:)([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex LoadRegex = new(
        @"^#load\s+""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex IncludePathRegex = new(
        @"^#I\s+""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NowarnRegex = new(
        @"^#nowarn\s+""(\d+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly List<string> _resolvedAssemblyPaths = new();
    private readonly List<string> _loadedFilePaths = new();
    private readonly HashSet<int> _suppressedWarnings = new();

    /// <summary>
    /// Assembly paths resolved from <c>#r "assembly.dll"</c> directives, for IntelliSense.
    /// </summary>
    public IReadOnlyList<string> ResolvedAssemblyPaths => _resolvedAssemblyPaths;

    /// <summary>
    /// File paths loaded via <c>#load "script.fsx"</c> directives, for IntelliSense context.
    /// </summary>
    public IReadOnlyList<string> LoadedFilePaths => _loadedFilePaths;

    /// <summary>
    /// Warning numbers suppressed by <c>#nowarn</c> directives, for diagnostic filtering.
    /// </summary>
    public IReadOnlySet<int> SuppressedWarnings => _suppressedWarnings;

    /// <summary>
    /// Processes script directives in the code, resolving relative paths against the
    /// notebook's location and tracking state for IntelliSense.
    /// </summary>
    /// <returns>The processed code with absolute paths substituted.</returns>
    public string ProcessDirectives(string code, INotebookMetadata? metadata)
    {
        var processed = code;

        // Process #r "assembly.dll" (non-NuGet)
        processed = AssemblyRefRegex.Replace(processed, match =>
        {
            var relativePath = match.Groups[1].Value;
            var resolved = ResolvePath(relativePath, metadata);
            if (File.Exists(resolved) && !_resolvedAssemblyPaths.Contains(resolved))
                _resolvedAssemblyPaths.Add(resolved);
            return $"#r \"{resolved}\"";
        });

        // Process #load "script.fsx"
        processed = LoadRegex.Replace(processed, match =>
        {
            var relativePath = match.Groups[1].Value;
            var resolved = ResolvePath(relativePath, metadata);
            if (File.Exists(resolved) && !_loadedFilePaths.Contains(resolved))
                _loadedFilePaths.Add(resolved);
            return $"#load \"{resolved}\"";
        });

        // Process #I "path"
        processed = IncludePathRegex.Replace(processed, match =>
        {
            var relativePath = match.Groups[1].Value;
            var resolved = ResolvePath(relativePath, metadata);
            return $"#I \"{resolved}\"";
        });

        // Process #nowarn "number"
        foreach (Match match in NowarnRegex.Matches(processed))
        {
            if (int.TryParse(match.Groups[1].Value, out var warningNumber))
                _suppressedWarnings.Add(warningNumber);
        }

        // #time directives pass through unchanged (FSI handles timing)

        return processed;
    }

    /// <summary>
    /// Resolves a potentially relative path against the notebook's directory.
    /// </summary>
    internal static string ResolvePath(string relativePath, INotebookMetadata? metadata)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        if (metadata?.FilePath is not null)
        {
            var notebookDir = Path.GetDirectoryName(metadata.FilePath);
            if (notebookDir is not null)
                return Path.GetFullPath(Path.Combine(notebookDir, relativePath));
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
    }
}
