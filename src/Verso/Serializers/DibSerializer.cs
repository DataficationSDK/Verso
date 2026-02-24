using System.Text.Json;
using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Serializers;

/// <summary>
/// Import-only serializer for Polyglot Notebooks <c>.dib</c> files.
/// </summary>
[VersoExtension]
public sealed class DibSerializer : INotebookSerializer
{
    private static readonly Regex MagicDirectivePattern = new(
        @"^#!(\w[\w#-]*)$",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, (string Type, string? Language)> DirectiveMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["markdown"] = ("markdown", null),
            ["csharp"] = ("code", "csharp"),
            ["c#"] = ("code", "csharp"),
            ["fsharp"] = ("code", "fsharp"),
            ["f#"] = ("code", "fsharp"),
            ["pwsh"] = ("code", "powershell"),
            ["powershell"] = ("code", "powershell"),
            ["javascript"] = ("code", "javascript"),
            ["js"] = ("code", "javascript"),
            ["html"] = ("html", null),
            ["mermaid"] = ("mermaid", null),
            ["sql"] = ("sql", null),
            ["value"] = ("code", "value"),
        };

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.dib";
    public string Name => "Polyglot Notebook Serializer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Import-only serializer for Polyglot Notebooks .dib files.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- INotebookSerializer ---

    public string FormatId => "dib";
    public IReadOnlyList<string> FileExtensions => new[] { ".dib" };

    public bool CanImport(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return filePath.EndsWith(".dib", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> SerializeAsync(NotebookModel notebook)
    {
        throw new NotSupportedException("Polyglot Notebook .dib export is not supported. Use the Verso native format.");
    }

    public Task<NotebookModel> DeserializeAsync(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var notebook = new NotebookModel { FormatVersion = "1.0" };
        string? defaultKernel = null;

        var lines = content.Split('\n');
        int startIndex = 0;

        // Check for #!meta block at the top
        startIndex = TryParseMetaBlock(lines, out defaultKernel);
        if (defaultKernel is not null)
        {
            notebook.DefaultKernelId = NormalizeLanguage(defaultKernel);
        }

        // Split content into cells by magic directive lines
        var segments = SplitByDirectives(lines, startIndex, defaultKernel);

        foreach (var (type, language, source) in segments)
        {
            var trimmedSource = TrimBlankLines(source);
            if (string.IsNullOrEmpty(trimmedSource))
                continue;

            notebook.Cells.Add(new CellModel
            {
                Type = type,
                Language = language,
                Source = trimmedSource,
            });
        }

        return Task.FromResult(notebook);
    }

    // --- Parsing helpers ---

    private static int TryParseMetaBlock(string[] lines, out string? defaultKernel)
    {
        defaultKernel = null;

        // Skip leading blank lines
        int i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            i++;

        if (i >= lines.Length || lines[i].Trim() != "#!meta")
            return 0;

        // Collect JSON lines after #!meta using brace-depth tracking
        i++;
        var jsonLines = new List<string>();
        int braceDepth = 0;
        bool foundOpenBrace = false;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmedLine = line.TrimEnd('\r');

            // If we haven't found the opening brace yet, stop at a magic directive
            if (!foundOpenBrace && MagicDirectivePattern.IsMatch(trimmedLine))
                break;

            jsonLines.Add(line);

            foreach (char c in trimmedLine)
            {
                if (c == '{') { braceDepth++; foundOpenBrace = true; }
                else if (c == '}') { braceDepth--; }
            }

            i++;

            // Stop once the JSON object is complete
            if (foundOpenBrace && braceDepth <= 0)
                break;
        }

        var json = string.Join('\n', jsonLines).Trim();
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("kernelInfo", out var kernelInfo) &&
                    kernelInfo.TryGetProperty("defaultKernelName", out var kernelName) &&
                    kernelName.ValueKind == JsonValueKind.String)
                {
                    defaultKernel = kernelName.GetString();
                }
            }
            catch (JsonException)
            {
                // Invalid meta JSON — skip it
            }
        }

        return i;
    }

    private static List<(string Type, string? Language, string Source)> SplitByDirectives(
        string[] lines, int startIndex, string? defaultKernel)
    {
        var segments = new List<(string Type, string? Language, string Source)>();
        var currentLines = new List<string>();
        string currentType = "code";
        string? currentLanguage = NormalizeLanguage(defaultKernel) ?? "csharp";

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var match = MagicDirectivePattern.Match(line);

            if (match.Success)
            {
                // Flush the current segment
                if (currentLines.Count > 0)
                {
                    segments.Add((currentType, currentLanguage, string.Join('\n', currentLines)));
                    currentLines.Clear();
                }

                var directive = match.Groups[1].Value;

                if (DirectiveMap.TryGetValue(directive, out var mapped))
                {
                    currentType = mapped.Type;
                    currentLanguage = mapped.Language;
                }
                else
                {
                    // Unknown directive — preserve as code cell with directive name as language
                    currentType = "code";
                    currentLanguage = directive.ToLowerInvariant();
                }
            }
            else
            {
                currentLines.Add(line);
            }
        }

        // Flush final segment
        if (currentLines.Count > 0)
        {
            segments.Add((currentType, currentLanguage, string.Join('\n', currentLines)));
        }

        return segments;
    }

    private static string TrimBlankLines(string source)
    {
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

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        var lower = language.ToLowerInvariant();
        return lower switch
        {
            "c#" => "csharp",
            "f#" => "fsharp",
            _ => lower
        };
    }
}
