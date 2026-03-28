using System.Text.Json;
using System.Text.Json.Serialization;
using Verso.Abstractions;
using Verso.Execution;

namespace Verso.Cli.Execution;

/// <summary>
/// Produces structured JSON output matching the Verso CLI specification schema.
/// </summary>
public sealed class JsonOutputWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds the JSON output document from execution results.
    /// </summary>
    public static JsonOutputDocument Build(
        string notebookPath,
        IReadOnlyList<CellModel> cells,
        IReadOnlyList<ExecutionResult> results,
        TimeSpan totalElapsed,
        IReadOnlyList<VariableDescriptor>? variables = null,
        Dictionary<string, object>? parameters = null)
    {
        var cellOutputs = new List<JsonCellOutput>();

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var result = results.FirstOrDefault(r => r.CellId == cell.Id);

            cellOutputs.Add(new JsonCellOutput
            {
                Index = i,
                Id = cell.Id.ToString(),
                Language = cell.Language ?? "unknown",
                Status = result?.Status.ToString() ?? "Skipped",
                Elapsed = result?.Elapsed.ToString() ?? TimeSpan.Zero.ToString(),
                Outputs = cell.Outputs.Select(o => new JsonMimeOutput
                {
                    MimeType = o.MimeType,
                    Content = o.Content,
                    IsError = o.IsError ? true : null,
                    ErrorName = o.ErrorName,
                    ErrorStackTrace = o.ErrorStackTrace
                }).ToList()
            });
        }

        var succeeded = results.Count(r => r.Status == ExecutionResult.ExecutionStatus.Success);
        var failed = results.Count(r => r.Status == ExecutionResult.ExecutionStatus.Failed);

        var doc = new JsonOutputDocument
        {
            Notebook = notebookPath,
            Cells = cellOutputs,
            Summary = new JsonSummary
            {
                Total = cells.Count,
                Succeeded = succeeded,
                Failed = failed,
                Elapsed = totalElapsed.ToString()
            }
        };

        if (parameters is { Count: > 0 })
        {
            doc.Parameters = parameters.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value);
        }

        if (variables is { Count: > 0 })
        {
            doc.Variables = variables.ToDictionary(
                v => v.Name,
                v => v.Value);
        }

        return doc;
    }

    /// <summary>
    /// Serializes the output document to a JSON string.
    /// </summary>
    public static string Serialize(JsonOutputDocument document)
    {
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    /// <summary>
    /// Writes the JSON output to a TextWriter.
    /// </summary>
    public static void WriteTo(JsonOutputDocument document, TextWriter writer)
    {
        writer.Write(Serialize(document));
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the JSON output to a file.
    /// </summary>
    public static async Task WriteToFileAsync(JsonOutputDocument document, string filePath)
    {
        var json = Serialize(document);
        await File.WriteAllTextAsync(filePath, json);
    }
}

public sealed class JsonOutputDocument
{
    public string Notebook { get; set; } = "";
    public Dictionary<string, object?>? Parameters { get; set; }
    public List<JsonCellOutput> Cells { get; set; } = new();
    public JsonSummary Summary { get; set; } = new();
    public Dictionary<string, object?>? Variables { get; set; }
}

public sealed class JsonCellOutput
{
    public int Index { get; set; }
    public string Id { get; set; } = "";
    public string Language { get; set; } = "";
    public string Status { get; set; } = "";
    public string Elapsed { get; set; } = "";
    public List<JsonMimeOutput> Outputs { get; set; } = new();
}

public sealed class JsonMimeOutput
{
    public string MimeType { get; set; } = "";
    public string Content { get; set; } = "";
    public bool? IsError { get; set; }
    public string? ErrorName { get; set; }
    public string? ErrorStackTrace { get; set; }
}

public sealed class JsonSummary
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string Elapsed { get; set; } = "";
}
