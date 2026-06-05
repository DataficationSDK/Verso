using System.Text.Json;
using System.Text.Json.Serialization;
using Verso.Abstractions;

namespace Verso.Serializers;

/// <summary>
/// Serializer for Jupyter <c>.ipynb</c> notebooks (nbformat v4).
/// </summary>
[VersoExtension]
public sealed class JupyterSerializer : INotebookSerializer
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // --- IExtension ---

    public string ExtensionId => "verso.serializer.jupyter";
    public string Name => "Jupyter Serializer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Serializer for Jupyter .ipynb notebooks (nbformat v4).";

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
        ArgumentNullException.ThrowIfNull(notebook);

        var doc = new JupyterWriteNotebook
        {
            NbFormat = 4,
            NbFormatMinor = 5,
            Metadata = BuildMetadata(notebook.DefaultKernelId),
            Cells = notebook.Cells.Select(BuildCell).ToList()
        };

        return Task.FromResult(JsonSerializer.Serialize(doc, WriteOptions));
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

        // Polyglot Notebooks record a per-cell kernel via metadata.polyglot_notebook.kernelName
        // and a notebook-level kernel table under metadata.polyglot_notebook.kernelInfo. The
        // table maps a kernel name (e.g. "csharp", "sql-PrimaryServer") to its language; we use
        // it to give each cell its own language rather than collapsing everything onto the
        // notebook default.
        var kernelLanguages = ExtractPolyglotKernelLanguages(jupyterDoc.Metadata);

        if (jupyterDoc.Cells is not null)
        {
            foreach (var jCell in jupyterDoc.Cells)
            {
                var isCode = string.Equals(jCell.CellType, "code", StringComparison.OrdinalIgnoreCase);
                var kernelName = isCode ? ExtractCellKernelName(jCell.Metadata) : null;

                var cellModel = new CellModel
                {
                    Type = MapCellType(jCell.CellType),
                    Language = isCode
                        ? ResolveCellLanguage(kernelName, kernelLanguages, notebook.DefaultKernelId)
                        : null,
                    Source = JoinSource(jCell.Source)
                };

                // Carry the raw Polyglot kernel name for SQL cells so the SQL import
                // post-processor can recover the connection name (e.g. "sql-PrimaryServer"
                // selects the "PrimaryServer" connection). The post-processor strips this
                // key once consumed so it never reaches the output document.
                if (kernelName is not null &&
                    kernelName.StartsWith("sql-", StringComparison.OrdinalIgnoreCase))
                {
                    cellModel.Metadata["polyglotKernelName"] = kernelName;
                }

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

    // --- Write helpers ---

    // Code/markdown round-trip cleanly. Other cell types collapse to "raw" so structure
    // survives — the Verso type label is preserved in cell.metadata.verso_type.
    private static JupyterWriteCell BuildCell(CellModel cell)
    {
        var jupyterType = cell.Type?.ToLowerInvariant() switch
        {
            "code" => "code",
            "markdown" => "markdown",
            _ => "raw"
        };

        var writeCell = new JupyterWriteCell
        {
            CellType = jupyterType,
            Source = SplitSourceForJupyter(cell.Source ?? "")
        };

        if (!string.Equals(jupyterType, cell.Type, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(cell.Type))
        {
            writeCell.Metadata["verso_type"] = cell.Type!;
        }

        foreach (var (key, value) in cell.Metadata)
        {
            if (key == "execution_count") continue;
            writeCell.Metadata[key] = value;
        }

        if (string.Equals(jupyterType, "code", StringComparison.OrdinalIgnoreCase))
        {
            writeCell.ExecutionCount = TryGetExecutionCount(cell.Metadata);
            writeCell.Outputs = cell.Outputs.Select(BuildOutput).ToList();
        }

        return writeCell;
    }

    private static JupyterWriteOutput BuildOutput(CellOutput output)
    {
        if (output.IsError)
        {
            var lines = string.IsNullOrEmpty(output.ErrorStackTrace)
                ? new List<string>()
                : output.ErrorStackTrace.Split('\n').ToList();
            return new JupyterWriteOutput
            {
                OutputType = "error",
                EName = output.ErrorName ?? "Error",
                EValue = output.Content ?? "",
                Traceback = lines
            };
        }

        // Null/empty MimeType is treated as text/plain rather than producing a
        // display_data with an invalid empty MIME key.
        var mimeType = string.IsNullOrEmpty(output.MimeType) ? "text/plain" : output.MimeType;

        if (string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return new JupyterWriteOutput
            {
                OutputType = "stream",
                Name = "stdout",
                Text = SplitSourceForJupyter(output.Content ?? "")
            };
        }

        return new JupyterWriteOutput
        {
            OutputType = "display_data",
            Data = new Dictionary<string, List<string>>
            {
                [mimeType] = SplitSourceForJupyter(output.Content ?? "")
            },
            Metadata = new Dictionary<string, object>()
        };
    }

    private static JupyterWriteMetadata BuildMetadata(string? defaultKernelId)
    {
        if (string.IsNullOrWhiteSpace(defaultKernelId))
            return new JupyterWriteMetadata();

        var (name, displayName, language) = ReverseNormalizeKernel(defaultKernelId);
        return new JupyterWriteMetadata
        {
            Kernelspec = new JupyterKernelSpec
            {
                Name = name,
                DisplayName = displayName,
                Language = language
            },
            LanguageInfo = new JupyterLanguageInfo { Name = language }
        };
    }

    private static (string Name, string DisplayName, string Language) ReverseNormalizeKernel(string kernelId)
    {
        return kernelId.ToLowerInvariant() switch
        {
            "csharp" => ("csharp", "C#", "csharp"),
            "fsharp" => ("fsharp", "F#", "fsharp"),
            "python" => ("python3", "Python 3", "python"),
            _ => (kernelId, kernelId, kernelId)
        };
    }

    private static List<string> SplitSourceForJupyter(string source)
    {
        if (string.IsNullOrEmpty(source))
            return new List<string>();

        var lines = source.Split('\n');
        var result = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length - 1; i++)
            result.Add(lines[i] + "\n");
        result.Add(lines[lines.Length - 1]);
        return result;
    }

    private static int? TryGetExecutionCount(Dictionary<string, object> metadata)
    {
        if (!metadata.TryGetValue("execution_count", out var value))
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v) => v,
            _ => null
        };
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
            "t-sql" => "sql",
            "tsql" => "sql",
            "pwsh" => "powershell",
            _ => language.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Builds a kernel-name to language map from <c>metadata.polyglot_notebook.kernelInfo.items</c>.
    /// Each item carries a <c>name</c> and an optional <c>languageName</c>; when the language is
    /// absent the kernel name doubles as the language (e.g. "csharp"). SQL kernels are named
    /// <c>sql-&lt;connection&gt;</c> with a <c>languageName</c> of "T-SQL", which normalizes to "sql".
    /// </summary>
    private static Dictionary<string, string> ExtractPolyglotKernelLanguages(JsonElement? metadata)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (metadata is null || metadata.Value.ValueKind != JsonValueKind.Object)
            return map;

        if (!metadata.Value.TryGetProperty("polyglot_notebook", out var polyglot) ||
            polyglot.ValueKind != JsonValueKind.Object ||
            !polyglot.TryGetProperty("kernelInfo", out var kernelInfo) ||
            kernelInfo.ValueKind != JsonValueKind.Object ||
            !kernelInfo.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("name", out var nameEl) ||
                nameEl.ValueKind != JsonValueKind.String)
                continue;

            var name = nameEl.GetString();
            if (string.IsNullOrEmpty(name)) continue;

            string? language = name;
            if (item.TryGetProperty("languageName", out var langEl) &&
                langEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(langEl.GetString()))
            {
                language = langEl.GetString();
            }

            var normalized = NormalizeLanguage(language);
            if (normalized is not null)
                map[name] = normalized;
        }

        return map;
    }

    /// <summary>
    /// Reads <c>metadata.polyglot_notebook.kernelName</c> from a cell, returning the raw kernel
    /// name (e.g. "csharp", "sql-PrimaryServer") or <c>null</c> when absent.
    /// </summary>
    private static string? ExtractCellKernelName(JsonElement? metadata)
    {
        if (metadata is null || metadata.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (metadata.Value.TryGetProperty("polyglot_notebook", out var polyglot) &&
            polyglot.ValueKind == JsonValueKind.Object &&
            polyglot.TryGetProperty("kernelName", out var kernelName) &&
            kernelName.ValueKind == JsonValueKind.String)
        {
            var value = kernelName.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    /// <summary>
    /// Resolves a code cell's language from its Polyglot kernel name, falling back to the
    /// notebook default when the cell carries no per-cell kernel metadata.
    /// </summary>
    private static string? ResolveCellLanguage(
        string? kernelName,
        Dictionary<string, string> kernelLanguages,
        string? defaultKernelId)
    {
        if (kernelName is null)
            return defaultKernelId;

        if (kernelLanguages.TryGetValue(kernelName, out var mapped))
            return mapped;

        // SQL connection kernels follow the "sql-<connection>" convention even when the
        // notebook omits a kernelInfo table.
        if (kernelName.StartsWith("sql-", StringComparison.OrdinalIgnoreCase))
            return "sql";

        return NormalizeLanguage(kernelName) ?? defaultKernelId;
    }

    // --- Internal write DTOs ---

    private sealed class JupyterWriteNotebook
    {
        [JsonPropertyName("cells")]
        public List<JupyterWriteCell> Cells { get; set; } = new();

        [JsonPropertyName("metadata")]
        public JupyterWriteMetadata Metadata { get; set; } = new();

        [JsonPropertyName("nbformat")]
        public int NbFormat { get; set; }

        [JsonPropertyName("nbformat_minor")]
        public int NbFormatMinor { get; set; }
    }

    private sealed class JupyterWriteMetadata
    {
        [JsonPropertyName("kernelspec")]
        public JupyterKernelSpec? Kernelspec { get; set; }

        [JsonPropertyName("language_info")]
        public JupyterLanguageInfo? LanguageInfo { get; set; }
    }

    private sealed class JupyterKernelSpec
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("language")]
        public string Language { get; set; } = "";
    }

    private sealed class JupyterLanguageInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class JupyterWriteCell
    {
        [JsonPropertyName("cell_type")]
        public string CellType { get; set; } = "code";

        [JsonPropertyName("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new();

        [JsonPropertyName("source")]
        public List<string> Source { get; set; } = new();

        [JsonPropertyName("execution_count")]
        public int? ExecutionCount { get; set; }

        [JsonPropertyName("outputs")]
        public List<JupyterWriteOutput>? Outputs { get; set; }
    }

    private sealed class JupyterWriteOutput
    {
        [JsonPropertyName("output_type")]
        public string OutputType { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("text")]
        public List<string>? Text { get; set; }

        [JsonPropertyName("data")]
        public Dictionary<string, List<string>>? Data { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }

        [JsonPropertyName("ename")]
        public string? EName { get; set; }

        [JsonPropertyName("evalue")]
        public string? EValue { get; set; }

        [JsonPropertyName("traceback")]
        public List<string>? Traceback { get; set; }
    }

    // --- Internal read DTOs ---

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
