using System.Text;
using FSharp.Compiler.CodeAnalysis;
using FSharp.Compiler.Tokenization;
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

    private readonly List<string> _executedFragments = new();
    private readonly IReadOnlyList<string> _defaultOpens;
    private readonly List<string> _additionalReferences = new();
    private readonly List<string> _loadedFiles = new();
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
        var fragment = new StringBuilder();
        AppendFragment(fragment, $"{ExecutedCellModulePrefix}{_executedFragments.Count + 1}", code);
        _executedFragments.Add(fragment.ToString());
    }

    /// <summary>
    /// Records the files an executed <c>#load</c> compiled, in the order FSI compiled them.
    /// </summary>
    /// <remarks>
    /// FSI compiles a loaded script as a file of its own, named for its module or namespace
    /// header, or without one for its file name (<c>lib.fsx</c> is <c>Lib</c>), and does not
    /// open it. The checker does the same when the files are part of the project ahead of the
    /// notebook, so they are kept as files rather than copied into the document. A file loaded
    /// again moves to the end, since its latest load is the one later cells see.
    /// </remarks>
    public void AddLoadedFiles(IEnumerable<string> filePaths)
    {
        var files = filePaths.ToList();
        _loadedFiles.RemoveAll(f => files.Contains(f, StringComparer.OrdinalIgnoreCase));
        _loadedFiles.AddRange(files);
    }

    /// <summary>
    /// Adds a reference path for future IntelliSense resolution.
    /// </summary>
    public void AddReference(string assemblyPath)
    {
        if (!_additionalReferences.Contains(assemblyPath, StringComparer.OrdinalIgnoreCase))
            _additionalReferences.Add(assemblyPath);
    }

    /// <summary>
    /// Builds a virtual document combining all previous executions with the current cell code.
    /// </summary>
    /// <returns>
    /// A tuple of (SourceText, PrefixLineCount, FSharpProjectOptions) where PrefixLineCount
    /// is the number of lines before the current cell code begins.
    /// </returns>
    /// <remarks>
    /// FSI compiles each execution as a fragment of its own and opens it for the ones after, so
    /// a later execution may define again a type or module an earlier one defined, as a cell does
    /// each time it is run again. In one file that is a duplicate definition, so each executed
    /// source is wrapped in a module of its own, which is then opened, the way FSI does it. Script
    /// directives are lifted out of the module, since they apply to the whole script, and the
    /// fragment's top-level opens are repeated after it, since in FSI they stay open.
    /// </remarks>
    public (string SourceText, int PrefixLineCount, FSharpProjectOptions Options) BuildDocument(string currentCellCode)
    {
        var prefixBuilder = new StringBuilder();

        // Add default opens
        foreach (var ns in _defaultOpens)
        {
            prefixBuilder.AppendLine($"open {ns}");
        }

        // Add previously executed sources, one fragment each
        foreach (var fragment in _executedFragments)
        {
            prefixBuilder.Append(fragment);
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
    /// The name every executed cell's module starts with, so completions can leave them out.
    /// </summary>
    public const string ExecutedCellModulePrefix = "VersoExecutedCell";

    private static readonly System.Text.RegularExpressions.Regex ExecutedCellModuleName =
        new($@"\.{ExecutedCellModulePrefix}\d+\b|\b{ExecutedCellModulePrefix}\d+\.", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Removes the executed cells' module names from compiler text (a tooltip, a diagnostic), so
    /// <c>Notebook.VersoExecutedCell1.Shape</c> reads as <c>Notebook.Shape</c>, as it would with
    /// the cells checked in one file.
    /// </summary>
    public static string HideExecutedCellModules(string text) =>
        text.Contains(ExecutedCellModulePrefix, StringComparison.Ordinal)
            ? ExecutedCellModuleName.Replace(text, "")
            : text;

    private static readonly string[] ScriptDirectives =
        { "#r", "#load", "#I", "#nowarn", "#warnon", "#time", "#help", "#quit", "#i" };

    // FSI defines INTERACTIVE, so the branches it compiled are the ones the tokenizer keeps.
    private static readonly FSharpList<string> InteractiveDefines = ListModule.OfArray(new[] { "INTERACTIVE" });

    private static void AppendFragment(StringBuilder builder, string moduleName, string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var body = new StringBuilder();
        var opens = new List<string>();
        var tokenizer = new FSharpSourceTokenizer(
            InteractiveDefines, FSharpOption<string>.None, FSharpOption<string>.None, FSharpOption<bool>.None);
        var state = FSharpTokenizerLexState.Initial;

        foreach (var line in lines)
        {
            // A line that starts inside a multi-line string or comment, or in a branch #if
            // leaves out, is not code, whatever it starts with: it is neither a directive to
            // lift nor an open to repeat.
            var startsInCode = StartsInCode(state);
            state = ScanLine(tokenizer, line, state);

            if (startsInCode && IsScriptDirective(line))
            {
                builder.AppendLine(line);
                continue;
            }

            if (startsInCode && line.StartsWith("open ", StringComparison.Ordinal))
                opens.Add(line);

            body.Append("    ").AppendLine(line);
        }

        builder.Append("module ").Append(moduleName).AppendLine(" =");
        builder.Append(body);
        // A fragment of nothing but directives, or a cell whose last line is a comment, still
        // needs a body.
        builder.AppendLine("    do ()");
        builder.Append("open ").AppendLine(moduleName);
        foreach (var open in opens)
            builder.AppendLine(open);
    }

    private static bool StartsInCode(FSharpTokenizerLexState state)
    {
        var color = FSharpLineTokenizer.ColorStateOfLexState(state);
        return color == FSharpTokenizerColorState.InitialState
            || color == FSharpTokenizerColorState.Token
            || color == FSharpTokenizerColorState.EndLineThenToken;
    }

    private static FSharpTokenizerLexState ScanLine(FSharpSourceTokenizer tokenizer, string line, FSharpTokenizerLexState state)
    {
        var lineTokenizer = tokenizer.CreateLineTokenizer(line);
        while (true)
        {
            var (token, next) = lineTokenizer.ScanToken(state);
            state = next;
            if (token is null)
                return state;
        }
    }

    private static bool IsScriptDirective(string line)
    {
        foreach (var directive in ScriptDirectives)
        {
            if (line.StartsWith(directive, StringComparison.Ordinal)
                && (line.Length == directive.Length || char.IsWhiteSpace(line[directive.Length])))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Clears all accumulated state (for kernel restart).
    /// </summary>
    public void Reset()
    {
        _executedFragments.Clear();
        _additionalReferences.Clear();
        _loadedFiles.Clear();
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
            sourceFiles: _loadedFiles.Append(VirtualFileName).ToArray(),
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
