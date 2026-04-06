using System.Text;
using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Extensions.Utilities;

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

            if (useVisibility)
            {
                var renderer = options!.Renderers!.FirstOrDefault(r =>
                    string.Equals(r.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase)) ?? FallbackRenderer;
                var visibility = CellVisibilityResolver.Resolve(cell, renderer, options.LayoutId!, options.SupportedVisibilityStates!);

                switch (visibility)
                {
                    case CellVisibilityState.Hidden:
                        continue;
                    case CellVisibilityState.OutputOnly:
                        if (renderedCount > 0) sb.AppendLine();
                        RenderOutputsOnly(sb, cell);
                        renderedCount++;
                        continue;
                    case CellVisibilityState.Collapsed:
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

            // Blank line between cells
            if (renderedCount > 0) sb.AppendLine();

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

            // Outputs
            foreach (var output in cell.Outputs)
            {
                sb.AppendLine();
                RenderOutput(sb, output);
            }

            renderedCount++;
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void RenderOutputsOnly(StringBuilder sb, CellModel cell)
    {
        for (int j = 0; j < cell.Outputs.Count; j++)
        {
            if (j > 0) sb.AppendLine();
            RenderOutput(sb, cell.Outputs[j]);
        }
    }

    private static void RenderOutput(StringBuilder sb, CellOutput output)
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

        switch (output.MimeType)
        {
            case "text/plain":
                sb.AppendLine("> Output:");
                sb.AppendLine(">");
                sb.AppendLine("> ```");
                foreach (var line in output.Content.Split('\n'))
                {
                    sb.Append("> ").AppendLine(line);
                }
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
                foreach (var line in output.Content.Split('\n'))
                {
                    sb.Append("> ").AppendLine(line);
                }
                sb.AppendLine("> ```");
                break;
        }
    }
}
