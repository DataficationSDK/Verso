using System.Text.Json;
using System.Text.Json.Serialization;
using Verso.Abstractions;

namespace Verso.Serializers;

/// <summary>
/// Serializer for the native <c>.verso</c> file format using <c>System.Text.Json</c>.
/// </summary>
[VersoExtension]
public sealed class VersoSerializer : INotebookSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.verso";
    public string Name => "Verso Serializer";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Native .verso file format serializer.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- INotebookSerializer ---

    public string FormatId => "verso";
    public IReadOnlyList<string> FileExtensions => new[] { ".verso" };

    public bool CanImport(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return filePath.EndsWith(".verso", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> SerializeAsync(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);

        var doc = new VersoDocument
        {
            Verso = notebook.FormatVersion,
            Metadata = new VersoMetadata
            {
                Title = notebook.Title,
                Created = notebook.Created,
                Modified = notebook.Modified,
                DefaultKernel = notebook.DefaultKernelId,
                ActiveLayout = notebook.ActiveLayoutId,
                PreferredTheme = notebook.PreferredThemeId,
                Extensions = (notebook.RequiredExtensions.Count > 0 || notebook.OptionalExtensions.Count > 0)
                    ? new VersoExtensions
                    {
                        Required = notebook.RequiredExtensions.Count > 0 ? notebook.RequiredExtensions : null,
                        Optional = notebook.OptionalExtensions.Count > 0 ? notebook.OptionalExtensions : null
                    }
                    : null
            },
            Cells = notebook.Cells.Select(c => new VersoCell
            {
                Id = c.Id.ToString(),
                Type = c.Type,
                Language = c.Language,
                Source = c.Source,
                Outputs = c.Outputs.Count > 0
                    ? c.Outputs.Select(o => new VersoCellOutput
                    {
                        MimeType = o.MimeType,
                        Content = o.Content,
                        IsError = o.IsError ? true : null,
                        ErrorName = o.ErrorName,
                        ErrorStackTrace = o.ErrorStackTrace
                    }).ToList()
                    : null,
                Metadata = c.Metadata.Count > 0 ? SerializeMetadata(c.Metadata) : null
            }).ToList(),
            Layouts = notebook.Layouts.Count > 0 ? SerializeLayouts(notebook.Layouts) : null
        };

        var json = JsonSerializer.Serialize(doc, WriteOptions);
        return Task.FromResult(json);
    }

    public Task<NotebookModel> DeserializeAsync(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var doc = JsonSerializer.Deserialize<VersoDocument>(content, ReadOptions)
            ?? throw new JsonException("Failed to deserialize .verso document.");

        var notebook = new NotebookModel
        {
            FormatVersion = doc.Verso ?? "1.0",
            Title = doc.Metadata?.Title,
            Created = doc.Metadata?.Created,
            Modified = doc.Metadata?.Modified,
            DefaultKernelId = doc.Metadata?.DefaultKernel,
            ActiveLayoutId = doc.Metadata?.ActiveLayout,
            PreferredThemeId = doc.Metadata?.PreferredTheme,
            RequiredExtensions = doc.Metadata?.Extensions?.Required ?? new List<string>(),
            OptionalExtensions = doc.Metadata?.Extensions?.Optional ?? new List<string>()
        };

        if (doc.Cells is not null)
        {
            foreach (var cell in doc.Cells)
            {
                var cellModel = new CellModel
                {
                    Id = Guid.TryParse(cell.Id, out var id) ? id : Guid.NewGuid(),
                    Type = cell.Type ?? "code",
                    Language = cell.Language,
                    Source = cell.Source ?? ""
                };

                if (cell.Outputs is not null)
                {
                    foreach (var output in cell.Outputs)
                    {
                        cellModel.Outputs.Add(new CellOutput(
                            output.MimeType ?? "text/plain",
                            output.Content ?? "",
                            output.IsError ?? false,
                            output.ErrorName,
                            output.ErrorStackTrace));
                    }
                }

                if (cell.Metadata is not null)
                {
                    cellModel.Metadata = DeserializeMetadata(cell.Metadata);
                }

                notebook.Cells.Add(cellModel);
            }
        }

        if (doc.Layouts is not null)
        {
            notebook.Layouts = DeserializeLayouts(doc.Layouts);
        }

        return Task.FromResult(notebook);
    }

    // --- Metadata serialization helpers ---

    private static Dictionary<string, JsonElement> SerializeMetadata(Dictionary<string, object> metadata)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in metadata)
        {
            var json = JsonSerializer.SerializeToElement(value, WriteOptions);
            result[key] = json;
        }
        return result;
    }

    private static Dictionary<string, object> DeserializeMetadata(Dictionary<string, JsonElement> elements)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, element) in elements)
        {
            result[key] = ConvertJsonElement(element);
        }
        return result;
    }

    private static Dictionary<string, JsonElement>? SerializeLayouts(Dictionary<string, object> layouts)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in layouts)
        {
            var json = JsonSerializer.SerializeToElement(value, WriteOptions);
            result[key] = json;
        }
        return result;
    }

    private static Dictionary<string, object> DeserializeLayouts(Dictionary<string, JsonElement> elements)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, element) in elements)
        {
            result[key] = ConvertJsonElement(element);
        }
        return result;
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => ConvertJsonArray(element),
            _ => element.GetRawText()
        };
    }

    private static Dictionary<string, object> ConvertJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElement(prop.Value);
        }
        return dict;
    }

    private static List<object> ConvertJsonArray(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElement(item));
        }
        return list;
    }

    // --- Internal DTOs ---

    private sealed class VersoDocument
    {
        public string? Verso { get; set; }
        public VersoMetadata? Metadata { get; set; }
        public List<VersoCell>? Cells { get; set; }
        public Dictionary<string, JsonElement>? Layouts { get; set; }
    }

    private sealed class VersoMetadata
    {
        public string? Title { get; set; }
        public DateTimeOffset? Created { get; set; }
        public DateTimeOffset? Modified { get; set; }
        public string? DefaultKernel { get; set; }
        public string? ActiveLayout { get; set; }
        public string? PreferredTheme { get; set; }
        public VersoExtensions? Extensions { get; set; }
    }

    private sealed class VersoExtensions
    {
        public List<string>? Required { get; set; }
        public List<string>? Optional { get; set; }
    }

    private sealed class VersoCell
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Language { get; set; }
        public string? Source { get; set; }
        public List<VersoCellOutput>? Outputs { get; set; }
        public Dictionary<string, JsonElement>? Metadata { get; set; }
    }

    private sealed class VersoCellOutput
    {
        public string? MimeType { get; set; }
        public string? Content { get; set; }
        public bool? IsError { get; set; }
        public string? ErrorName { get; set; }
        public string? ErrorStackTrace { get; set; }
    }
}
