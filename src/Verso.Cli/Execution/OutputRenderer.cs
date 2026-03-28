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
    private readonly bool _supportsAnsi;

    public OutputRenderer(TextWriter stdout, TextWriter stderr, bool verbose)
    {
        _stdout = stdout;
        _stderr = stderr;
        _verbose = verbose;
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
    public void RenderCell(int index, CellModel cell, ExecutionResult result)
    {
        if (cell.Type is not "code") return;

        var language = cell.Language ?? "unknown";
        _stdout.WriteLine($"\u2500\u2500\u2500 Cell {index} ({language}) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");

        foreach (var output in cell.Outputs)
        {
            RenderOutput(output);
        }

        _stdout.WriteLine();
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
                // Skipped by default; Phase 1D adds --include-markdown
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
