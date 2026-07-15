using System.Text.Json;
using Verso.Abstractions;
using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public sealed class VersoSerializerTests
{
    private readonly VersoSerializer _serializer = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.AreEqual("verso.serializer.verso", _serializer.ExtensionId);
    }

    [TestMethod]
    public void FormatId_IsVerso()
    {
        Assert.AreEqual("verso", _serializer.FormatId);
    }

    [TestMethod]
    public void FileExtensions_ContainsDotVerso()
    {
        CollectionAssert.Contains(_serializer.FileExtensions.ToList(), ".verso");
    }

    [TestMethod]
    public void CanImport_TrueForVersoExtension()
    {
        Assert.IsTrue(_serializer.CanImport("notebook.verso"));
        Assert.IsTrue(_serializer.CanImport("path/to/file.VERSO"));
    }

    [TestMethod]
    public void CanImport_FalseForOtherExtensions()
    {
        Assert.IsFalse(_serializer.CanImport("notebook.ipynb"));
        Assert.IsFalse(_serializer.CanImport("notebook.txt"));
    }

    [TestMethod]
    public async Task RoundTrip_EmptyNotebook_PreservesDefaults()
    {
        var notebook = new NotebookModel();
        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(NotebookFormatVersion.Current, result.FormatVersion);
        Assert.AreEqual(0, result.Cells.Count);
    }

    [TestMethod]
    public async Task Serialize_AlwaysStampsCurrentFormatVersion()
    {
        // Even a model still carrying an older version is written as the current format.
        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Initial };

        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(NotebookFormatVersion.Current, doc.RootElement.GetProperty("verso").GetString());
    }

    [TestMethod]
    public async Task Deserialize_LegacyCellMetadataKey_MigratedAndStampedCurrent()
    {
        const string json = """
        {
          "verso": "1.0",
          "cells": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "type": "code",
              "language": "csharp",
              "source": "x",
              "metadata": { "verso:visibility": "hidden" }
            }
          ]
        }
        """;

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(NotebookFormatVersion.Current, notebook.FormatVersion);
        var cell = notebook.Cells.Single();
        Assert.IsFalse(cell.Metadata.ContainsKey(CellLayoutVisibilityMetadata.LegacyMetadataKey));
        Assert.IsTrue(cell.Metadata.ContainsKey(CellLayoutVisibilityMetadata.MetadataKey));
        Assert.AreEqual("hidden", cell.Metadata[CellLayoutVisibilityMetadata.MetadataKey]?.ToString());
    }

    [TestMethod]
    public async Task Deserialize_NewerFormatVersion_LoadsBestEffortWithoutChangingVersion()
    {
        const string json = """
        {
          "verso": "99.0",
          "metadata": { "title": "From the future" },
          "cells": []
        }
        """;

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("99.0", notebook.FormatVersion);
        Assert.AreEqual("From the future", notebook.Title);
    }

    [TestMethod]
    public async Task Deserialize_LegacyBareActiveLayout_LoadsAsUnqualifiedAndFlagsResolution()
    {
        const string json = """
        {
          "verso": "1.0",
          "metadata": { "activeLayout": "dashboard" },
          "cells": []
        }
        """;

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.IsNotNull(notebook.ActiveLayout);
        Assert.AreEqual(string.Empty, notebook.ActiveLayout!.Value.ExtensionId);
        Assert.AreEqual("dashboard", notebook.ActiveLayout!.Value.LayoutId);
        Assert.IsTrue(notebook.RequiresLegacyLayoutResolution);
    }

    [TestMethod]
    public async Task Deserialize_QualifiedActiveLayout_LoadsDirectlyWithoutLegacyFlag()
    {
        const string json = """
        {
          "verso": "1.0",
          "metadata": {
            "activeLayout": { "extensionId": "verso.layout.dashboard", "layoutId": "dashboard" }
          },
          "cells": []
        }
        """;

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.IsNotNull(notebook.ActiveLayout);
        Assert.AreEqual("verso.layout.dashboard", notebook.ActiveLayout!.Value.ExtensionId);
        Assert.AreEqual("dashboard", notebook.ActiveLayout!.Value.LayoutId);
        Assert.IsFalse(notebook.RequiresLegacyLayoutResolution);
    }

    [TestMethod]
    public async Task Serialize_QualifiedActiveLayout_EmitsObjectForm()
    {
        var notebook = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.dashboard", "dashboard")
        };

        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var activeLayout = doc.RootElement.GetProperty("metadata").GetProperty("activeLayout");
        Assert.AreEqual(JsonValueKind.Object, activeLayout.ValueKind);
        Assert.AreEqual("verso.layout.dashboard", activeLayout.GetProperty("extensionId").GetString());
        Assert.AreEqual("dashboard", activeLayout.GetProperty("layoutId").GetString());
    }

    [TestMethod]
    public async Task Serialize_UnqualifiedActiveLayout_EmitsBareStringForRoundTrip()
    {
        // When resolution failed (no matching extension loaded), the unqualified reference
        // round-trips as a bare string so a later load with the extension present can resolve it.
        var notebook = new NotebookModel
        {
            ActiveLayout = new LayoutReference(string.Empty, "missing-layout")
        };

        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var activeLayout = doc.RootElement.GetProperty("metadata").GetProperty("activeLayout");
        Assert.AreEqual(JsonValueKind.String, activeLayout.ValueKind);
        Assert.AreEqual("missing-layout", activeLayout.GetString());
    }

    [TestMethod]
    public async Task RoundTrip_QualifiedActiveLayout_Preserves()
    {
        var notebook = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook")
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsNotNull(result.ActiveLayout);
        Assert.AreEqual("verso.layout.notebook", result.ActiveLayout!.Value.ExtensionId);
        Assert.AreEqual("notebook", result.ActiveLayout!.Value.LayoutId);
        Assert.IsFalse(result.RequiresLegacyLayoutResolution);
    }

    [TestMethod]
    public async Task RoundTrip_FullNotebook_PreservesAllFields()
    {
        var created = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 20, 14, 0, 0, TimeSpan.Zero);

        var notebook = new NotebookModel
        {
            FormatVersion = "1.0",
            Title = "Test Notebook",
            Created = created,
            Modified = modified,
            DefaultKernelId = "csharp",
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook"),
            PreferredThemeId = "verso-light",
            RequiredExtensions = new List<string> { "verso.kernel.csharp" },
            OptionalExtensions = new List<string> { "verso.theme.dark" }
        };

        var cellId = Guid.NewGuid();
        notebook.Cells.Add(new CellModel
        {
            Id = cellId,
            Type = "code",
            Language = "csharp",
            Source = "Console.WriteLine(\"Hello\");",
            Outputs = new List<CellOutput>
            {
                new("text/plain", "Hello")
            },
            Metadata = new Dictionary<string, object> { ["collapsed"] = true }
        });

        notebook.Cells.Add(new CellModel
        {
            Type = "markdown",
            Source = "# Header"
        });

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        // The serializer always stamps the current format version on save, so a stale in-model
        // version is normalized rather than preserved.
        Assert.AreEqual(NotebookFormatVersion.Current, result.FormatVersion);
        Assert.AreEqual("Test Notebook", result.Title);
        Assert.AreEqual(created, result.Created);
        Assert.AreEqual(modified, result.Modified);
        Assert.AreEqual("csharp", result.DefaultKernelId);
        Assert.AreEqual("notebook", result.ActiveLayoutId);
        Assert.AreEqual("verso-light", result.PreferredThemeId);
        Assert.AreEqual(1, result.RequiredExtensions.Count);
        Assert.AreEqual("verso.kernel.csharp", result.RequiredExtensions[0]);
        Assert.AreEqual(1, result.OptionalExtensions.Count);
        Assert.AreEqual("verso.theme.dark", result.OptionalExtensions[0]);

        Assert.AreEqual(2, result.Cells.Count);

        var cell1 = result.Cells[0];
        Assert.AreEqual(cellId, cell1.Id);
        Assert.AreEqual("code", cell1.Type);
        Assert.AreEqual("csharp", cell1.Language);
        Assert.AreEqual("Console.WriteLine(\"Hello\");", cell1.Source);
        Assert.AreEqual(1, cell1.Outputs.Count);
        Assert.AreEqual("text/plain", cell1.Outputs[0].MimeType);
        Assert.AreEqual("Hello", cell1.Outputs[0].Content);
        Assert.IsFalse(cell1.Outputs[0].IsError);

        var cell2 = result.Cells[1];
        Assert.AreEqual("markdown", cell2.Type);
        Assert.AreEqual("# Header", cell2.Source);
    }

    [TestMethod]
    public async Task RoundTrip_ErrorOutput_PreservesErrorFields()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel
        {
            Type = "code",
            Source = "throw new Exception();",
            Outputs = new List<CellOutput>
            {
                new("text/plain", "Error occurred",
                    IsError: true,
                    ErrorName: "InvalidOperationException",
                    ErrorStackTrace: "at Line 1")
            }
        });

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        var output = result.Cells[0].Outputs[0];
        Assert.IsTrue(output.IsError);
        Assert.AreEqual("InvalidOperationException", output.ErrorName);
        Assert.AreEqual("at Line 1", output.ErrorStackTrace);
    }

    [TestMethod]
    public async Task RoundTrip_NullFields_HandledGracefully()
    {
        var notebook = new NotebookModel
        {
            Title = null,
            DefaultKernelId = null,
            ActiveLayout = null,
            PreferredThemeId = null
        };
        notebook.Cells.Add(new CellModel
        {
            Language = null,
            Source = ""
        });

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsNull(result.Title);
        Assert.IsNull(result.DefaultKernelId);
        Assert.AreEqual(1, result.Cells.Count);
        Assert.IsNull(result.Cells[0].Language);
    }

    [TestMethod]
    public async Task Serialize_ProducesCamelCaseJson()
    {
        var notebook = new NotebookModel { Title = "Test" };
        notebook.Cells.Add(new CellModel { Source = "x" });

        var json = await _serializer.SerializeAsync(notebook);

        Assert.IsTrue(json.Contains("\"verso\""));
        Assert.IsTrue(json.Contains("\"metadata\""));
        Assert.IsTrue(json.Contains("\"cells\""));
        Assert.IsTrue(json.Contains("\"title\""));
        Assert.IsTrue(json.Contains("\"source\""));
    }

    [TestMethod]
    public async Task Serialize_LayoutMetadata_Included()
    {
        var notebook = new NotebookModel();
        notebook.Layouts["grid"] = new Dictionary<string, object>
        {
            ["columns"] = 3,
            ["rows"] = 2
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsTrue(result.Layouts.ContainsKey("grid"));
    }

    [TestMethod]
    public async Task RoundTrip_ExtensionLists_BothEmpty()
    {
        var notebook = new NotebookModel();
        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(0, result.RequiredExtensions.Count);
        Assert.AreEqual(0, result.OptionalExtensions.Count);
    }

    [TestMethod]
    public void IExtension_Metadata_IsValid()
    {
        Assert.AreEqual("Verso Serializer", _serializer.Name);
        Assert.AreEqual("1.0.0", _serializer.Version);
        Assert.AreEqual("Datafication", _serializer.Author);
        Assert.IsNotNull(_serializer.Description);
    }

    // --- Parameters round-trip tests ---

    [TestMethod]
    public async Task RoundTrip_Parameters_PreservesAllFields()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new()
                {
                    Type = "string",
                    Description = "AWS region",
                    Default = "us-west-2",
                    Required = true,
                    Order = 1
                },
                ["batchSize"] = new()
                {
                    Type = "int",
                    Default = 1000L,
                    Order = 2
                },
                ["dryRun"] = new()
                {
                    Type = "bool",
                    Default = false,
                    Description = "Dry run mode"
                }
            }
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsNotNull(result.Parameters);
        Assert.AreEqual(3, result.Parameters.Count);

        var region = result.Parameters["region"];
        Assert.AreEqual("string", region.Type);
        Assert.AreEqual("AWS region", region.Description);
        Assert.AreEqual("us-west-2", region.Default);
        Assert.IsTrue(region.Required);
        Assert.AreEqual(1, region.Order);

        var batch = result.Parameters["batchSize"];
        Assert.AreEqual("int", batch.Type);
        Assert.AreEqual(1000L, batch.Default);
        Assert.IsFalse(batch.Required);
        Assert.AreEqual(2, batch.Order);

        var dry = result.Parameters["dryRun"];
        Assert.AreEqual("bool", dry.Type);
        Assert.AreEqual(false, dry.Default);
        Assert.AreEqual("Dry run mode", dry.Description);
    }

    [TestMethod]
    public async Task RoundTrip_Parameters_FloatDefault()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["threshold"] = new() { Type = "float", Default = 0.95 }
            }
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(0.95, result.Parameters!["threshold"].Default);
    }

    [TestMethod]
    public async Task RoundTrip_Parameters_DateDefault()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["date"] = new() { Type = "date", Default = "2024-01-15" }
            }
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("2024-01-15", result.Parameters!["date"].Default);
    }

    [TestMethod]
    public async Task RoundTrip_NoParameters_IsNull()
    {
        var notebook = new NotebookModel();

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsNull(result.Parameters);
    }

    [TestMethod]
    public async Task Serialize_Parameters_ProducesCorrectJson()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new()
                {
                    Type = "string",
                    Description = "AWS region",
                    Default = "us-west-2",
                    Required = true
                }
            }
        };

        var json = await _serializer.SerializeAsync(notebook);

        Assert.IsTrue(json.Contains("\"parameters\""));
        Assert.IsTrue(json.Contains("\"region\""));
        Assert.IsTrue(json.Contains("\"us-west-2\""));
        Assert.IsTrue(json.Contains("\"required\": true"));
    }

    [TestMethod]
    public async Task RoundTrip_Parameters_RequiredFalse_NotSerialized()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["optional"] = new() { Type = "string", Required = false }
            }
        };

        var json = await _serializer.SerializeAsync(notebook);

        // Required=false should be omitted (WhenWritingNull for the nullable bool)
        Assert.IsFalse(json.Contains("\"required\": false"));

        var result = await _serializer.DeserializeAsync(json);
        Assert.IsFalse(result.Parameters!["optional"].Required);
    }

    [TestMethod]
    public async Task Serialize_TransientOutputCellType_StripsOutputs()
    {
        var notebook = new NotebookModel();
        var markdown = new CellModel { Type = "markdown", Source = "# hi" };
        markdown.Outputs.Add(new CellOutput("text/html", "<h1>hi</h1>"));
        var code = new CellModel { Type = "code", Language = "csharp", Source = "1 + 1" };
        code.Outputs.Add(new CellOutput("text/plain", "2"));
        notebook.Cells.Add(markdown);
        notebook.Cells.Add(code);

        var serializer = new VersoSerializer(
            new ICellType[] { new Verso.Extensions.CellTypes.MarkdownCellType() });
        var json = await serializer.SerializeAsync(notebook);

        // Markdown outputs are transient (re-rendered on open), so they never reach disk;
        // code outputs persist as before.
        Assert.IsFalse(json.Contains("<h1>hi</h1>"), "transient markdown output must be stripped");
        Assert.IsTrue(json.Contains("\"2\""), "code output must still be persisted");

        // A round-trip loads the markdown cell with no outputs; the open path re-renders it.
        var result = await serializer.DeserializeAsync(json);
        Assert.AreEqual(0, result.Cells[0].Outputs.Count);
        Assert.AreEqual(1, result.Cells[1].Outputs.Count);
    }
}
