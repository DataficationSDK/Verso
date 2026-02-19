using System.Text.Json;
using System.Text.Json.Serialization;
using Verso.Abstractions;

namespace Verso.Serializers;

/// <summary>
/// Import-only serializer for Jupyter <c>.ipynb</c> notebooks (nbformat v4).
/// </summary>
[VersoExtension]
public sealed class JupyterSerializer : INotebookSerializer
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.jupyter";
    public string Name => "Jupyter Serializer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Import-only serializer for Jupyter .ipynb notebooks.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- INotebookSerializer ---

    public string FormatId => "jupyter";
    public IReadOnlyList<string> FileExtensions => new[] { ".ipynb" };

    public bool CanImport(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> SerializeAsync(NotebookModel notebook)
    {
        throw new NotSupportedException("Jupyter .ipynb export is not supported. Use the Verso native format.");
    }

    public Task<NotebookModel> DeserializeAsync(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var jupyterDoc = JsonSerializer.Deserialize<JupyterNotebook>(content, ReadOptions)
            ?? throw new JsonException("Failed to parse Jupyter notebook.");

        if (jupyterDoc.NbFormat < 4)
            throw new NotSupportedException(
                $"Only nbformat >= 4 is supported. This notebook uses nbformat {jupyterDoc.NbFormat}.");

        var notebook = new NotebookModel
        {
            FormatVersion = "1.0",
            DefaultKernelId = ExtractKernelLanguage(jupyterDoc.Metadata)
        };

        if (jupyterDoc.Cells is not null)
        {
            foreach (var jCell in jupyterDoc.Cells)
            {
                var cellModel = new CellModel
                {
                    Type = MapCellType(jCell.CellType),
                    Language = string.Equals(jCell.CellType, "code", StringComparison.OrdinalIgnoreCase)
                        ? notebook.DefaultKernelId
                        : null,
                    Source = JoinSource(jCell.Source)
                };

                // Execution count
                if (jCell.ExecutionCount.HasValue)
                {
                    cellModel.Metadata["execution_count"] = jCell.ExecutionCount.Value;
                }

                // Outputs
                if (jCell.Outputs is not null)
                {
                    foreach (var jOutput in jCell.Outputs)
                    {
                        var mapped = MapOutput(jOutput);
                        if (mapped is not null)
                            cellModel.Outputs.Add(mapped);
                    }
                }

                notebook.Cells.Add(cellModel);
            }
        }

        return Task.FromResult(notebook);
    }

    // --- Mapping helpers ---

    private static string MapCellType(string? cellType)
    {
        return cellType?.ToLowerInvariant() switch
        {
            "code" => "code",
            "markdown" => "markdown",
            "raw" => "raw",
            _ => cellType ?? "code"
        };
    }

    private static string JoinSource(JsonElement? source)
    {
        if (source is null || source.Value.ValueKind == JsonValueKind.Null)
            return "";

        if (source.Value.ValueKind == JsonValueKind.String)
            return source.Value.GetString() ?? "";

        if (source.Value.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var element in source.Value.EnumerateArray())
            {
                parts.Add(element.GetString() ?? "");
            }
            return string.Join("", parts);
        }

        return "";
    }

    private static CellOutput? MapOutput(JupyterOutput output)
    {
        switch (output.OutputType?.ToLowerInvariant())
        {
            case "stream":
                var streamText = JoinSource(output.Text);
                return new CellOutput("text/plain", streamText);

            case "execute_result":
            case "display_data":
                return MapDataOutput(output.Data);

            case "error":
                var traceback = output.Traceback is not null
                    ? string.Join(Environment.NewLine, output.Traceback)
                    : null;
                var errorMessage = output.EValue ?? "Unknown error";
                var content = traceback is not null
                    ? $"{output.EName}: {errorMessage}{Environment.NewLine}{traceback}"
                    : $"{output.EName}: {errorMessage}";
                return new CellOutput(
                    "text/plain",
                    content,
                    IsError: true,
                    ErrorName: output.EName,
                    ErrorStackTrace: traceback);

            default:
                return null;
        }
    }

    private static CellOutput? MapDataOutput(JsonElement? data)
    {
        if (data is null || data.Value.ValueKind != JsonValueKind.Object)
            return null;

        // Prefer text/html > text/plain > image/png
        if (data.Value.TryGetProperty("text/html", out var html))
        {
            return new CellOutput("text/html", ExtractMimeContent(html));
        }

        if (data.Value.TryGetProperty("text/plain", out var plain))
        {
            return new CellOutput("text/plain", ExtractMimeContent(plain));
        }

        if (data.Value.TryGetProperty("image/png", out var png))
        {
            return new CellOutput("image/png", ExtractMimeContent(png));
        }

        // Fall back to first available property
        foreach (var prop in data.Value.EnumerateObject())
        {
            return new CellOutput(prop.Name, ExtractMimeContent(prop.Value));
        }

        return null;
    }

    private static string ExtractMimeContent(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? "";

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                parts.Add(item.GetString() ?? "");
            }
            return string.Join("", parts);
        }

        return element.GetRawText();
    }

    private static string? ExtractKernelLanguage(JsonElement? metadata)
    {
        if (metadata is null || metadata.Value.ValueKind != JsonValueKind.Object)
            return null;

        // Try metadata.kernelspec.language
        if (metadata.Value.TryGetProperty("kernelspec", out var kernelspec) &&
            kernelspec.ValueKind == JsonValueKind.Object)
        {
            if (kernelspec.TryGetProperty("language", out var language) &&
                language.ValueKind == JsonValueKind.String)
            {
                return NormalizeLanguage(language.GetString());
            }
        }

        // Try metadata.language_info.name
        if (metadata.Value.TryGetProperty("language_info", out var langInfo) &&
            langInfo.ValueKind == JsonValueKind.Object)
        {
            if (langInfo.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                return NormalizeLanguage(name.GetString());
            }
        }

        return null;
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        return language.ToLowerInvariant() switch
        {
            "c#" => "csharp",
            "f#" => "fsharp",
            _ => language.ToLowerInvariant()
        };
    }

    // --- Internal DTOs ---

    private sealed class JupyterNotebook
    {
        [JsonPropertyName("nbformat")]
        public int NbFormat { get; set; }

        [JsonPropertyName("nbformat_minor")]
        public int NbFormatMinor { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        [JsonPropertyName("cells")]
        public List<JupyterCell>? Cells { get; set; }
    }

    private sealed class JupyterCell
    {
        [JsonPropertyName("cell_type")]
        public string? CellType { get; set; }

        [JsonPropertyName("source")]
        public JsonElement? Source { get; set; }

        [JsonPropertyName("outputs")]
        public List<JupyterOutput>? Outputs { get; set; }

        [JsonPropertyName("execution_count")]
        public int? ExecutionCount { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }
    }

    private sealed class JupyterOutput
    {
        [JsonPropertyName("output_type")]
        public string? OutputType { get; set; }

        [JsonPropertyName("text")]
        public JsonElement? Text { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        [JsonPropertyName("ename")]
        public string? EName { get; set; }

        [JsonPropertyName("evalue")]
        public string? EValue { get; set; }

        [JsonPropertyName("traceback")]
        public List<string>? Traceback { get; set; }
    }
}
