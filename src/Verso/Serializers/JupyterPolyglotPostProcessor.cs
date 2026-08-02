using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Resources;

namespace Verso.Serializers;

/// <summary>
/// Translates Polyglot Notebook language-switching directives in imported Jupyter
/// notebooks into the equivalent Verso cell types and languages, splitting cells
/// when a directive appears mid-source. Mirrors the behavior of <see cref="DibSerializer"/>
/// for <c>.ipynb</c> imports so notebooks authored in Polyglot Notebooks open
/// without "Unknown magic command" failures at run time.
/// </summary>
[VersoExtension]
public sealed class JupyterPolyglotPostProcessor : INotebookPostProcessor
{
    // Bare directive of the form `#!token`, optionally indented and with trailing whitespace.
    // Anything with arguments (e.g. `#!set --name X`) intentionally does not match here so
    // it can be handled by the runtime magic dispatcher or a more specific post-processor.
    private static readonly Regex DirectivePattern = new(
        @"^[ \t]*#!(\w[\w#-]*)\s*$",
        RegexOptions.Compiled);

    // Language switchers known to Polyglot Notebooks.
    private static readonly Dictionary<string, (string Type, string? Language)> LanguageDirectives =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["markdown"] = ("markdown", null),
            ["csharp"] = ("code", "csharp"),
            ["cs"] = ("code", "csharp"),
            ["c#"] = ("code", "csharp"),
            ["fsharp"] = ("code", "fsharp"),
            ["f#"] = ("code", "fsharp"),
            ["pwsh"] = ("code", "powershell"),
            ["powershell"] = ("code", "powershell"),
            ["python"] = ("code", "python"),
            ["py"] = ("code", "python"),
            ["javascript"] = ("code", "javascript"),
            ["js"] = ("code", "javascript"),
            ["typescript"] = ("code", "typescript"),
            ["ts"] = ("code", "typescript"),
            ["html"] = ("html", null),
            ["mermaid"] = ("mermaid", null),
            ["sql"] = ("code", "sql"),
            ["value"] = ("code", "value"),
        };

    // Bare directives that are operations rather than language switchers. These are
    // left in the cell source so the runtime magic dispatcher (or a future extension)
    // can handle them; soft-degrading them to a "language" would break the cell.
    private static readonly HashSet<string> NonLanguageDirectives =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "about", "lsmagic", "time", "who", "whos", "restart",
            "extension", "import", "nuget", "set", "share", "connect",
            "meta", "r", "i",
        };

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.jupyter-polyglot";
    string IExtension.Name => Strings.PostProcessor_JupyterPolyglot;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.PostProcessor_JupyterPolyglot_Description;

    // --- INotebookPostProcessor ---

    // Runs before JupyterFSharpPostProcessor (10) and JupyterSqlImportHook (100) so
    // that leading-directive cells are already split into per-language cells before
    // those processors inspect cell-level patterns (#!set, #!share, #!connect,
    // dotnet_interactive metadata).
    public int Priority => 5;

    public bool CanProcess(string? filePath, string formatId)
    {
        if (string.Equals(formatId, "jupyter", StringComparison.OrdinalIgnoreCase))
            return true;

        return filePath is not null
            && filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase);
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath)
    {
        var producedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newCells = new List<CellModel>(notebook.Cells.Count);

        foreach (var cell in notebook.Cells)
        {
            if (cell.Type != "code" || string.IsNullOrEmpty(cell.Source))
            {
                newCells.Add(cell);
                continue;
            }

            var splits = SplitCellOnDirectives(cell);
            if (splits.Count == 0)
            {
                newCells.Add(cell);
                continue;
            }

            foreach (var split in splits)
            {
                if (split.Language is { } lang)
                    producedLanguages.Add(lang);
                newCells.Add(split);
            }
        }

        notebook.Cells = newCells;

        // Mirror the RequiredExtensions hints that the per-language post-processors
        // would have added for first-line cases we now handle here.
        if (producedLanguages.Contains("fsharp")
            && !notebook.RequiredExtensions.Contains("verso.fsharp"))
        {
            notebook.RequiredExtensions.Add("verso.fsharp");
        }
        if (producedLanguages.Contains("sql")
            && !notebook.RequiredExtensions.Contains("verso.ado"))
        {
            notebook.RequiredExtensions.Add("verso.ado");
        }

        return Task.FromResult(notebook);
    }

    public Task<NotebookModel> PreSerializeAsync(NotebookModel notebook, string? filePath)
        => Task.FromResult(notebook);

    // --- Splitting ---

    /// <summary>
    /// Splits a single cell along Polyglot language-switching directives.
    /// The first segment reuses the original <see cref="CellModel"/> instance so its
    /// outputs, metadata, and Id are preserved; subsequent segments are fresh cells
    /// with no outputs or execution metadata.
    /// Returns an empty list if the cell contains no recognized directives, signaling
    /// the caller to keep the original cell as-is.
    /// </summary>
    private static List<CellModel> SplitCellOnDirectives(CellModel original)
    {
        var lines = original.Source.Split('\n');
        var segments = new List<Segment>
        {
            new(original.Type, original.Language, new List<string>(), DirectiveSeen: false),
        };

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var match = DirectivePattern.Match(line);

            if (!match.Success)
            {
                segments[^1].Lines.Add(line);
                continue;
            }

            var directive = match.Groups[1].Value;

            // Operation-style bare directives stay in source for the runtime dispatcher.
            if (NonLanguageDirectives.Contains(directive))
            {
                segments[^1].Lines.Add(line);
                continue;
            }

            string type;
            string? language;
            if (LanguageDirectives.TryGetValue(directive, out var mapped))
            {
                type = mapped.Type;
                language = mapped.Language;
            }
            else
            {
                // Unknown bare directive — soft-degrade to a code cell whose language
                // is the raw token. The kernel-resolution layer surfaces a clearer
                // "no kernel for X" error than the magic-dispatcher's "unknown command".
                type = "code";
                language = directive.ToLowerInvariant();
            }

            // If the current segment has no body yet, the directive collapses onto it
            // rather than creating an empty trailing cell. This handles both leading
            // directives (which simply change the original cell's language) and
            // back-to-back directives (where the last one before a body wins).
            var current = segments[^1];
            if (current.Lines.Count == 0)
            {
                segments[^1] = new Segment(type, language, current.Lines, DirectiveSeen: true);
            }
            else
            {
                segments.Add(new Segment(type, language, new List<string>(), DirectiveSeen: true));
            }
        }

        // No language directive ever fired — caller should keep the cell unchanged.
        if (segments.Count == 1 && !segments[0].DirectiveSeen)
            return new List<CellModel>();

        var result = new List<CellModel>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var source = TrimBlankLines(string.Join('\n', seg.Lines));

            // Drop trailing-empty segments produced by a directive with no body,
            // but always keep the first segment so the original cell is preserved.
            if (string.IsNullOrEmpty(source) && i > 0)
                continue;

            if (i == 0)
            {
                original.Type = seg.Type;
                original.Language = seg.Language;
                original.Source = source;
                result.Add(original);
            }
            else
            {
                result.Add(new CellModel
                {
                    Type = seg.Type,
                    Language = seg.Language,
                    Source = source,
                });
            }
        }

        return result;
    }

    private static string TrimBlankLines(string source)
    {
        if (string.IsNullOrEmpty(source))
            return "";

        var lines = source.Split('\n');
        int start = 0;
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
            start++;
        int end = lines.Length - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
            end--;
        if (start > end)
            return "";
        return string.Join('\n', lines, start, end - start + 1);
    }

    private sealed record Segment(string Type, string? Language, List<string> Lines, bool DirectiveSeen);
}
