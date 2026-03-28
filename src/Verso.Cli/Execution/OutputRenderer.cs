using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Execution;

namespace Verso.Cli.Execution;

/// <summary>
/// Renders cell execution results to the terminal in human-readable text format.
/// </summary>
public sealed partial class OutputRenderer
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _verbose;
    private readonly bool _includeMarkdown;
    private readonly bool _showParameters;
    private readonly bool _supportsAnsi;

    public OutputRenderer(TextWriter stdout, TextWriter stderr, bool verbose,
        bool includeMarkdown = false, bool showParameters = false)
    {
        _stdout = stdout;
        _stderr = stderr;
        _verbose = verbose;
        _includeMarkdown = includeMarkdown;
        _showParameters = showParameters;
        _supportsAnsi = !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Writes a progress line to stderr when verbose mode is active.
    /// </summary>
    public void WriteProgress(int completedCount, int totalCount, int cellIndex, string? language, string message)
    {
        if (!_verbose) return;
        _stderr.WriteLine($"[{completedCount}/{totalCount}] {message}");
    }

    /// <summary>
    /// Renders outputs for a single cell execution result.
    /// </summary>
    public void RenderCell(int index, CellModel cell, ExecutionResult result,
        Dictionary<string, object>? resolvedParameters = null)
    {
        if (cell.Type is "code")
        {
            var language = cell.Language ?? "unknown";
            _stdout.WriteLine($"\u2500\u2500\u2500 Cell {index} ({language}) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

            foreach (var output in cell.Outputs)
            {
                RenderOutput(output);
            }

            _stdout.WriteLine();
        }
        else if (_showParameters && cell.Type is "parameters")
        {
            _stdout.WriteLine($"\u2500\u2500\u2500 Cell {index} (parameters) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

            if (resolvedParameters is { Count: > 0 })
            {
                var maxKey = resolvedParameters.Keys.Max(k => k.Length);
                foreach (var (name, value) in resolvedParameters)
                {
                    _stdout.WriteLine($"  {name.PadRight(maxKey)}  {value}");
                }
            }
            else
            {
                _stdout.WriteLine("  (no parameters)");
            }

            _stdout.WriteLine();
        }
        else if (_includeMarkdown && cell.Type is "markdown")
        {
            _stdout.WriteLine($"\u2500\u2500\u2500 Cell {index} (markdown) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

            if (!string.IsNullOrWhiteSpace(cell.Source))
                _stdout.WriteLine(cell.Source);

            _stdout.WriteLine();
        }
        else if (_includeMarkdown && cell.Type is "html")
        {
            _stdout.WriteLine($"\u2500\u2500\u2500 Cell {index} (html) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

            var stripped = StripHtmlTags(cell.Source);
            if (!string.IsNullOrWhiteSpace(stripped))
                _stdout.WriteLine(stripped);

            _stdout.WriteLine();
        }
    }

    /// <summary>
    /// Writes the summary footer with execution counts and total elapsed time.
    /// </summary>
    public void WriteSummary(IReadOnlyList<ExecutionResult> results, TimeSpan totalElapsed)
    {
        var succeeded = results.Count(r => r.Status == ExecutionResult.ExecutionStatus.Success);
        var failed = results.Count(r => r.Status == ExecutionResult.ExecutionStatus.Failed);
        var total = results.Count;

        _stdout.WriteLine($"\u2500\u2500\u2500 Summary \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
        _stdout.WriteLine($"Cells: {total} total, {succeeded} succeeded, {failed} failed");
        _stdout.WriteLine($"Time:  {totalElapsed.TotalSeconds:F1}s");
    }

    private void RenderOutput(CellOutput output)
    {
        switch (output.MimeType)
        {
            case "text/plain":
                if (output.IsError)
                    WriteError(output.Content);
                else
                    _stdout.WriteLine(output.Content);
                break;

            case "text/html":
                var stripped = StripHtmlTags(output.Content);
                if (!string.IsNullOrWhiteSpace(stripped))
                    _stdout.WriteLine(stripped);
                break;

            case "application/json":
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(output.Content);
                    var pretty = System.Text.Json.JsonSerializer.Serialize(
                        doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    _stdout.WriteLine(pretty);
                }
                catch
                {
                    _stdout.WriteLine(output.Content);
                }
                break;

            case "text/x-error":
                WriteError(output.Content);
                break;

            case "text/markdown":
                if (_includeMarkdown && !string.IsNullOrWhiteSpace(output.Content))
                    _stdout.WriteLine(output.Content);
                break;

            default:
                if (output.MimeType.StartsWith("image/"))
                    break; // Skip images in text mode
                if (output.IsError)
                    WriteError(output.Content);
                else
                    _stdout.WriteLine(output.Content);
                break;
        }
    }

    private void WriteError(string content)
    {
        if (_supportsAnsi)
            _stderr.WriteLine($"\x1b[31m[error] {content}\x1b[0m");
        else
            _stderr.WriteLine($"[error] {content}");
    }

    private static string StripHtmlTags(string html)
    {
        return HtmlTagRegex().Replace(html, "").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
