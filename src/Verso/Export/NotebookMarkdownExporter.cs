using System.Text;
using Verso.Abstractions;

namespace Verso.Export;

/// <summary>
/// Exports a notebook as a plain Markdown document.
/// </summary>
internal static class NotebookMarkdownExporter
{
    /// <summary>
    /// Generates a Markdown document from the given notebook cells.
    /// </summary>
    public static byte[] Export(string? title, IReadOnlyList<CellModel> cells)
    {
        var sb = new StringBuilder();

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append("# ").AppendLine(title);
            sb.AppendLine();
        }

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];

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

            // Blank line between cells
            if (i < cells.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
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
