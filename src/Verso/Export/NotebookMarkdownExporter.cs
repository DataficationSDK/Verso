using System.Text;
using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Extensions.Utilities;
using Verso.Resources;

namespace Verso.Export;

/// <summary>
/// Exports a notebook as a plain Markdown document.
/// </summary>
internal static class NotebookMarkdownExporter
{
    private static readonly ICellRenderer FallbackRenderer = new ContentFallbackRenderer();

    /// <summary>
    /// Generates a Markdown document from the given notebook cells.
    /// </summary>
    public static byte[] Export(string? title, IReadOnlyList<CellModel> cells)
        => Export(title, cells, options: null);

    /// <summary>
    /// Generates a Markdown document from the given notebook cells.
    /// When <paramref name="options"/> provides a layout ID, cells are filtered by visibility.
    /// </summary>
    public static byte[] Export(string? title, IReadOnlyList<CellModel> cells, ExportOptions? options)
    {
        var sb = new StringBuilder();

        var useVisibility = options?.LayoutId is not null
            && options.SupportedVisibilityStates is not null
            && options.Renderers is not null;
        var respectViewState = options?.RespectCellViewState ?? true;

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append("# ").AppendLine(title);
            sb.AppendLine();
        }

        var renderedCount = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];

            var layoutState = CellVisibilityState.Visible;
            if (useVisibility)
            {
                var renderer = options!.Renderers!.FirstOrDefault(r =>
                    string.Equals(r.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase)) ?? FallbackRenderer;
                layoutState = CellVisibilityResolver.Resolve(cell, renderer, options.LayoutId!, options.SupportedVisibilityStates!);

                if (layoutState == CellVisibilityState.Hidden)
                    continue;

                if (layoutState == CellVisibilityState.Collapsed)
                {
                    if (renderedCount > 0) sb.AppendLine();
                    sb.Append("> [collapsed] ").Append(cell.Type ?? "cell").Append(": ");
                    var collapsedTitle = !string.IsNullOrWhiteSpace(cell.Source)
                        ? cell.Source.Split('\n')[0].TrimStart('#', ' ').Trim()
                        : "Untitled";
                    sb.AppendLine(collapsedTitle);
                    renderedCount++;
                    continue;
                }
            }

            var outputVisibility = respectViewState
                ? CellViewStateReader.ReadOutputVisibility(cell)
                : CellViewStateMetadata.OutputExpanded;
            var inputCollapsed = respectViewState && CellViewStateReader.ReadInputCollapsed(cell);
            var outputPreviewLineCount = CellViewStateReader.ReadOutputPreviewLineCount(cell);

            var hideSource = layoutState == CellVisibilityState.OutputOnly || inputCollapsed;
            var hideOutputs = string.Equals(outputVisibility, CellViewStateMetadata.OutputHidden, StringComparison.Ordinal);
            var previewOutputs = string.Equals(outputVisibility, CellViewStateMetadata.OutputPreview, StringComparison.Ordinal);

            if (hideSource && (hideOutputs || cell.Outputs.Count == 0))
                continue;

            if (renderedCount > 0) sb.AppendLine();

            if (!hideSource)
            {
                if (string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(cell.Source);
                }
                else
                {
                    // Fenced code block with language tag
                    sb.Append("```").AppendLine(cell.Language ?? "");
                    sb.AppendLine(cell.Source);
                    sb.AppendLine("```");
                }
            }

            // Markdown cells store their rendered HTML as an output during execution,
            // which would duplicate the source we just emitted. Skip the outputs loop
            // for markdown cells; the source is already the rendered content.
            var isMarkdown = string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase);
            if (!hideOutputs && !isMarkdown)
            {
                for (int j = 0; j < cell.Outputs.Count; j++)
                {
                    if (j > 0 || !hideSource) sb.AppendLine();
                    RenderOutput(sb, cell.Outputs[j], previewOutputs, outputPreviewLineCount);
                }
            }

            renderedCount++;
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void RenderOutput(StringBuilder sb, CellOutput output, bool previewText, int previewLineCount)
    {
        if (output.IsError)
        {
            if (!string.IsNullOrEmpty(output.ErrorName))
            {
                sb.Append("> **").Append(output.ErrorName).AppendLine(":**");
            }
            else
            {
                sb.AppendLine("> **Error:**");
            }
            sb.AppendLine(">");
            foreach (var line in output.Content.Split('\n'))
            {
                sb.Append("> ").AppendLine(line);
            }
            if (!string.IsNullOrEmpty(output.ErrorStackTrace))
            {
                sb.AppendLine(">");
                sb.AppendLine("> ```");
                foreach (var line in output.ErrorStackTrace.Split('\n'))
                {
                    sb.Append("> ").AppendLine(line);
                }
                sb.AppendLine("> ```");
            }
            return;
        }

        if (output.Channel == OutputChannel.Stderr)
        {
            sb.AppendLine("```text");
            sb.AppendLine("[stderr]");
            foreach (var line in output.Content.Split('\n'))
            {
                sb.AppendLine(line.TrimEnd('\r'));
            }
            sb.AppendLine("```");
            return;
        }

        switch (output.MimeType)
        {
            case "text/plain":
                sb.AppendLine("> Output:");
                sb.AppendLine(">");
                sb.AppendLine("> ```");
                WritePreviewableLines(sb, output.Content, previewText, previewLineCount);
                sb.AppendLine("> ```");
                break;

            case "text/html":
            case "image/svg+xml":
                sb.AppendLine("> Output (HTML):");
                sb.AppendLine(">");
                foreach (var line in output.Content.Split('\n'))
                {
                    sb.Append("> ").AppendLine(line);
                }
                break;

            case "image/png":
                sb.AppendLine("![Output](data:image/png;base64,");
                sb.Append(output.Content);
                sb.AppendLine(")");
                break;

            default:
                sb.AppendLine("> Output:");
                sb.AppendLine(">");
                sb.AppendLine("> ```");
                WritePreviewableLines(sb, output.Content, previewText, previewLineCount);
                sb.AppendLine("> ```");
                break;
        }
    }

    private static void WritePreviewableLines(StringBuilder sb, string content, bool previewText, int previewLineCount)
    {
        var lines = content.Split('\n');
        if (previewText && previewLineCount > 0 && lines.Length > previewLineCount)
        {
            for (int i = 0; i < previewLineCount; i++)
            {
                sb.Append("> ").AppendLine(lines[i]);
            }
            var omitted = lines.Length - previewLineCount;
            // The quote marker is Markdown rather than words, so it is written here and the sentence
            // after it is the same one the HTML export uses.
            sb.Append("> ").AppendLine(string.Format(
                Plural.Of(omitted, Strings.Export_MoreLines_One, Strings.Export_MoreLines_Other), omitted));
        }
        else
        {
            foreach (var line in lines)
            {
                sb.Append("> ").AppendLine(line);
            }
        }
    }
}
