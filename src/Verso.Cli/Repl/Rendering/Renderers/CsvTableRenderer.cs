using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

/// <summary>
/// Parses a <c>text/csv</c> output and renders it as a Spectre <see cref="Table"/>.
/// Respects the row cap from <see cref="TruncationPolicy.MaxRows"/> and appends a
/// <c>… N more rows</c> footer line when content was truncated. Falls back to raw
/// text when colour is off so piped output stays machine-readable.
/// </summary>
internal static class CsvTableRenderer
{
    public static IRenderable AsRenderable(CellOutput output, TruncationPolicy policy, bool useColor)
    {
        var text = output.Content ?? string.Empty;

        if (!useColor)
            return new Text(text);

        var rows = ParseCsv(text).ToList();
        if (rows.Count == 0)
            return new Text(text);

        var header = rows[0];
        var body = rows.Skip(1).ToList();

        var table = new Table().Border(TableBorder.Rounded);
        foreach (var h in header)
            table.AddColumn(new TableColumn(Markup.Escape(h)));

        var rowCap = Math.Min(body.Count, policy.MaxRows);
        for (int i = 0; i < rowCap; i++)
        {
            var row = body[i];
            var cells = new string[header.Count];
            for (int j = 0; j < header.Count; j++)
                cells[j] = Markup.Escape(j < row.Count ? row[j] : string.Empty);
            table.AddRow(cells);
        }

        if (body.Count > rowCap)
            return new Rows(table, new Markup($"[dim]… {body.Count - rowCap} more rows[/]"));
        return table;
    }

    private static IEnumerable<List<string>> ParseCsv(string text)
    {
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    yield return row;
                    row = new List<string>();
                }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
