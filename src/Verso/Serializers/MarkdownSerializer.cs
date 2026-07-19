using Markdig;
using Markdig.Syntax;
using Verso.Abstractions;

namespace Verso.Serializers;

/// <summary>
/// Serializer for Markdown (<c>.md</c>) notebooks. Continuous prose becomes markdown cells and
/// top-level fenced code blocks with a recognized language tag become code cells; everything else
/// (bare or unknown fences, indented code, fences nested in quotes or lists) stays inline as
/// prose. Saving writes plain Markdown back, so the file remains readable anywhere Markdown
/// renders. Cell outputs are not persisted in this format.
/// </summary>
[VersoExtension]
public sealed class MarkdownSerializer : INotebookSerializer
{
    private const string SettingsExtensionId = "verso.serializer.markdown";
    private const string LineEndingSettingKey = "lineEnding";

    /// <summary>
    /// Cell metadata key holding the verbatim opening fence line a code cell was parsed from,
    /// reused on save so the author's fence style and tag never churn.
    /// </summary>
    public const string FenceMetadataKey = "md.fence";

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.markdown";
    public string Name => "Markdown Serializer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Serializer for Markdown (.md) notebooks with fenced code cells.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- INotebookSerializer ---

    public string FormatId => "markdown";
    public IReadOnlyList<string> FileExtensions => new[] { ".md" };
    public bool PreservesFormatByDefault => true;

    public bool CanImport(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    public Task<NotebookModel> DeserializeAsync(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hadCrLf = content.Contains("\r\n", StringComparison.Ordinal);
        var text = hadCrLf ? content.Replace("\r\n", "\n") : content;

        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Current };
        var lineStarts = ComputeLineStarts(text);
        var document = Markdown.Parse(text, Pipeline);

        int regionStart = 0;

        void FlushMarkdown(int endExclusive)
        {
            if (endExclusive <= regionStart)
                return;

            var trimmed = TrimBlankLines(text.Substring(regionStart, endExclusive - regionStart));
            if (trimmed.Length > 0)
                notebook.Cells.Add(new CellModel { Type = "markdown", Source = trimmed });
        }

        // Only the document's direct children are considered, so fences nested inside quotes,
        // lists, or other containers never split the surrounding prose.
        foreach (var block in document)
        {
            if (block is not FencedCodeBlock fenced)
                continue;

            var info = (fenced.Info ?? "").Trim();
            if (info.Length == 0 || !LanguageDirectiveMap.Aliases.TryGetValue(info, out var mapped))
                continue;

            // A fence tagged "markdown" is a literal Markdown example, not a cell boundary.
            // Turning it into a markdown cell would dissolve the fence into prose on save.
            if (string.Equals(mapped.Type, "markdown", StringComparison.OrdinalIgnoreCase))
                continue;

            int openLineIdx = fenced.Line;
            FlushMarkdown(lineStarts[openLineIdx]);

            var (openLine, interior, nextLineStart) = SliceFence(text, lineStarts, fenced);

            var cell = new CellModel
            {
                Type = mapped.Type,
                Language = mapped.Language,
                Source = interior,
            };
            cell.Metadata[FenceMetadataKey] = openLine;
            notebook.Cells.Add(cell);

            regionStart = nextLineStart;
        }

        FlushMarkdown(text.Length);

        if (hadCrLf)
        {
            notebook.ExtensionSettings[SettingsExtensionId] =
                new Dictionary<string, object?> { [LineEndingSettingKey] = "crlf" };
        }

        return Task.FromResult(notebook);
    }

    public Task<string> SerializeAsync(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);

        var parts = new List<string>();
        foreach (var cell in notebook.Cells)
        {
            if (string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = TrimBlankLines(cell.Source ?? "");
                if (trimmed.Length > 0)
                    parts.Add(trimmed);
                continue;
            }

            parts.Add(RenderFence(cell));
        }

        var body = string.Join("\n\n", parts);
        var result = body.Length > 0 ? body + "\n" : "";

        if (UsesCrLf(notebook))
            result = result.Replace("\n", "\r\n");

        return Task.FromResult(result);
    }

    // --- Parsing helpers ---

    private static (string OpenLine, string Interior, int NextLineStart) SliceFence(
        string text, int[] lineStarts, FencedCodeBlock fenced)
    {
        int openLineIdx = fenced.Line;
        int openLineEnd = LineEnd(text, lineStarts, openLineIdx);
        var openLine = text[lineStarts[openLineIdx]..openLineEnd];

        int interiorStart = Math.Min(openLineEnd + 1, text.Length);

        if (fenced.ClosingFencedCharCount == 0)
        {
            // Unterminated fence: the content runs to the end of the file, and the block's
            // Span is not reliable in this case, so slice directly to the end of the text.
            var tail = interiorStart < text.Length ? text[interiorStart..] : "";
            return (openLine, tail, text.Length);
        }

        // Closed fence: the span ends on the closing fence line, which is excluded from the
        // cell source and rederived on save from the opening fence characters.
        int closeLineIdx = LineIndexOfOffset(lineStarts, Math.Min(fenced.Span.End, Math.Max(text.Length - 1, 0)));
        int interiorEnd = Math.Max(lineStarts[closeLineIdx] - 1, interiorStart);
        var interior = interiorEnd > interiorStart ? text[interiorStart..interiorEnd] : "";
        int nextLineStart = closeLineIdx + 1 < lineStarts.Length ? lineStarts[closeLineIdx + 1] : text.Length;
        return (openLine, interior, nextLineStart);
    }

    private static int LineEnd(string text, int[] lineStarts, int lineIdx) =>
        lineIdx + 1 < lineStarts.Length ? lineStarts[lineIdx + 1] - 1 : text.Length;

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }

        return starts.ToArray();
    }

    private static int LineIndexOfOffset(int[] lineStarts, int offset)
    {
        int idx = Array.BinarySearch(lineStarts, offset);
        return idx >= 0 ? idx : ~idx - 1;
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

    // --- Serialization helpers ---

    private static string RenderFence(CellModel cell)
    {
        var openLine = cell.Metadata.TryGetValue(FenceMetadataKey, out var stored)
            && stored is string s && s.Length > 0
            ? s
            : "```" + LanguageDirectiveMap.ToFenceTag(cell.Type, cell.Language);

        var (fenceChar, count) = ParseFenceChars(openLine);
        var closeLine = new string(fenceChar, count);

        var source = cell.Source ?? "";
        var bodyText = source.Length == 0 ? "" : source + "\n";
        return openLine + "\n" + bodyText + closeLine;
    }

    private static (char Char, int Count) ParseFenceChars(string openLine)
    {
        var trimmed = openLine.TrimStart(' ');
        char fenceChar = trimmed.Length > 0 && trimmed[0] == '~' ? '~' : '`';
        int count = 0;
        while (count < trimmed.Length && trimmed[count] == fenceChar)
            count++;

        // Floor of three keeps the output a valid fence even for a malformed stored line.
        return (fenceChar, Math.Max(count, 3));
    }

    private static bool UsesCrLf(NotebookModel notebook) =>
        notebook.ExtensionSettings.TryGetValue(SettingsExtensionId, out var settings)
        && settings.TryGetValue(LineEndingSettingKey, out var value)
        && value is string ending
        && string.Equals(ending, "crlf", StringComparison.OrdinalIgnoreCase);
}
