using System.Text.Json;
using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public sealed class JupyterSerializerTests
{
    private readonly JupyterSerializer _serializer = new();

    [TestMethod]
    public void ExtensionMetadata_IsCorrect()
    {
        Assert.AreEqual("verso.serializer.jupyter", _serializer.ExtensionId);
        Assert.AreEqual("jupyter", _serializer.FormatId);
        Assert.AreEqual(1, _serializer.FileExtensions.Count);
        Assert.AreEqual(".ipynb", _serializer.FileExtensions[0]);
    }

    [TestMethod]
    public void CanImport_Ipynb_ReturnsTrue()
    {
        Assert.IsTrue(_serializer.CanImport("notebook.ipynb"));
    }

    [TestMethod]
    public void CanImport_UpperCase_ReturnsTrue()
    {
        Assert.IsTrue(_serializer.CanImport("Notebook.IPYNB"));
    }

    [TestMethod]
    public void CanImport_Verso_ReturnsFalse()
    {
        Assert.IsFalse(_serializer.CanImport("notebook.verso"));
    }

    [TestMethod]
    public async Task Deserialize_CodeCell()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": { ""kernelspec"": { ""language"": ""python"" } },
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": ""print('hello')"",
                ""outputs"": [],
                ""metadata"": {},
                ""execution_count"": 1
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("python", notebook.Cells[0].Language);
        Assert.AreEqual("print('hello')", notebook.Cells[0].Source);
        Assert.AreEqual(1, (int)notebook.Cells[0].Metadata["execution_count"]);
    }

    [TestMethod]
    public async Task Deserialize_MarkdownCell()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""markdown"",
                ""source"": ""# Title"",
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        Assert.IsNull(notebook.Cells[0].Language);
        Assert.AreEqual("# Title", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_RawCell()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""raw"",
                ""source"": ""raw content"",
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("raw", notebook.Cells[0].Type);
    }

    [TestMethod]
    public async Task Deserialize_SourceAsArray()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": [""line1\n"", ""line2""],
                ""outputs"": [],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("line1\nline2", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_StreamOutput()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": ""print('hi')"",
                ""outputs"": [{
                    ""output_type"": ""stream"",
                    ""name"": ""stdout"",
                    ""text"": ""hi\n""
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, notebook.Cells[0].Outputs.Count);
        Assert.AreEqual("text/plain", notebook.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("hi\n", notebook.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public async Task Deserialize_ExecuteResultOutput_PrefersHtml()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [{
                    ""output_type"": ""execute_result"",
                    ""data"": {
                        ""text/plain"": ""42"",
                        ""text/html"": ""<b>42</b>""
                    },
                    ""metadata"": {},
                    ""execution_count"": 1
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, notebook.Cells[0].Outputs.Count);
        Assert.AreEqual("text/html", notebook.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("<b>42</b>", notebook.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public async Task Deserialize_DisplayDataOutput()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [{
                    ""output_type"": ""display_data"",
                    ""data"": { ""text/plain"": ""result"" },
                    ""metadata"": {}
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("text/plain", notebook.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("result", notebook.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public async Task Deserialize_ErrorOutput()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [{
                    ""output_type"": ""error"",
                    ""ename"": ""ValueError"",
                    ""evalue"": ""bad value"",
                    ""traceback"": [""line 1"", ""line 2""]
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, notebook.Cells[0].Outputs.Count);
        var output = notebook.Cells[0].Outputs[0];
        Assert.IsTrue(output.IsError);
        Assert.AreEqual("ValueError", output.ErrorName);
        Assert.IsTrue(output.Content.Contains("bad value"));
        Assert.IsTrue(output.ErrorStackTrace!.Contains("line 1"));
    }

    [TestMethod]
    public async Task Deserialize_KernelLanguage_FromKernelspec()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": { ""kernelspec"": { ""language"": ""python"", ""display_name"": ""Python 3"" } },
            ""cells"": []
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("python", notebook.DefaultKernelId);
    }

    [TestMethod]
    public async Task Deserialize_KernelLanguage_FromLanguageInfo()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": { ""language_info"": { ""name"": ""python"" } },
            ""cells"": []
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("python", notebook.DefaultKernelId);
    }

    [TestMethod]
    public async Task Deserialize_CSharpLanguage_Normalized()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": { ""kernelspec"": { ""language"": ""C#"" } },
            ""cells"": []
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("csharp", notebook.DefaultKernelId);
    }

    [TestMethod]
    public void Deserialize_NbFormatLessThan4_Throws()
    {
        var json = @"{ ""nbformat"": 3, ""nbformat_minor"": 0, ""metadata"": {}, ""cells"": [] }";

        Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => _serializer.DeserializeAsync(json));
    }

    [TestMethod]
    public async Task Deserialize_ExecutionCount_PreservedInMetadata()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [],
                ""metadata"": {},
                ""execution_count"": 42
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.IsTrue(notebook.Cells[0].Metadata.ContainsKey("execution_count"));
        Assert.AreEqual(42, (int)notebook.Cells[0].Metadata["execution_count"]);
    }

    [TestMethod]
    public async Task Deserialize_ImagePngOutput()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [{
                    ""output_type"": ""display_data"",
                    ""data"": { ""image/png"": ""iVBORw0KGgo="" },
                    ""metadata"": {}
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("image/png", notebook.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("iVBORw0KGgo=", notebook.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public async Task Deserialize_MultipleCells()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [
                { ""cell_type"": ""markdown"", ""source"": ""# Title"", ""metadata"": {} },
                { ""cell_type"": ""code"", ""source"": ""x = 1"", ""outputs"": [], ""metadata"": {} },
                { ""cell_type"": ""code"", ""source"": ""y = 2"", ""outputs"": [], ""metadata"": {} }
            ]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(3, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        Assert.AreEqual("code", notebook.Cells[1].Type);
        Assert.AreEqual("code", notebook.Cells[2].Type);
    }

    [TestMethod]
    public async Task Deserialize_StreamOutput_SourceAsArray()
    {
        var json = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": {},
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": """",
                ""outputs"": [{
                    ""output_type"": ""stream"",
                    ""name"": ""stdout"",
                    ""text"": [""line1\n"", ""line2\n""]
                }],
                ""metadata"": {}
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(json);

        Assert.AreEqual("line1\nline2\n", notebook.Cells[0].Outputs[0].Content);
    }

    // --- Serialize ---

    [TestMethod]
    public async Task Serialize_EmptyNotebook_ProducesNbformat4()
    {
        var notebook = new NotebookModel();
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(4, doc.RootElement.GetProperty("nbformat").GetInt32());
        Assert.AreEqual(5, doc.RootElement.GetProperty("nbformat_minor").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("cells").GetArrayLength());
    }

    [TestMethod]
    public async Task Serialize_KernelSpec_FromDefaultKernel()
    {
        var notebook = new NotebookModel { DefaultKernelId = "csharp" };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var kernelspec = doc.RootElement.GetProperty("metadata").GetProperty("kernelspec");
        Assert.AreEqual("csharp", kernelspec.GetProperty("name").GetString());
        Assert.AreEqual("C#", kernelspec.GetProperty("display_name").GetString());
        Assert.AreEqual("csharp", kernelspec.GetProperty("language").GetString());
        Assert.AreEqual("csharp",
            doc.RootElement.GetProperty("metadata").GetProperty("language_info").GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task Serialize_MultiLineSource_AsStringList()
    {
        var notebook = new NotebookModel
        {
            Cells = { new CellModel { Type = "code", Source = "line1\nline2\nline3" } }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var source = doc.RootElement.GetProperty("cells")[0].GetProperty("source");
        Assert.AreEqual(JsonValueKind.Array, source.ValueKind);
        Assert.AreEqual(3, source.GetArrayLength());
        Assert.AreEqual("line1\n", source[0].GetString());
        Assert.AreEqual("line2\n", source[1].GetString());
        Assert.AreEqual("line3", source[2].GetString());
    }

    [TestMethod]
    public async Task Serialize_StreamOutput_FromTextPlain()
    {
        var notebook = new NotebookModel
        {
            Cells =
            {
                new CellModel
                {
                    Type = "code",
                    Source = "x",
                    Outputs = { new CellOutput("text/plain", "hello\n") }
                }
            }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var output = doc.RootElement.GetProperty("cells")[0].GetProperty("outputs")[0];
        Assert.AreEqual("stream", output.GetProperty("output_type").GetString());
        Assert.AreEqual("stdout", output.GetProperty("name").GetString());
        Assert.AreEqual("hello\n", output.GetProperty("text")[0].GetString());
    }

    [TestMethod]
    public async Task Serialize_EmptyMimeType_TreatsAsTextPlain()
    {
        var notebook = new NotebookModel
        {
            Cells =
            {
                new CellModel
                {
                    Type = "code",
                    Source = "x",
                    Outputs = { new CellOutput("", "hello") }
                }
            }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var output = doc.RootElement.GetProperty("cells")[0].GetProperty("outputs")[0];
        Assert.AreEqual("stream", output.GetProperty("output_type").GetString(),
            "Empty MimeType should coerce to text/plain stream rather than emit an empty MIME key.");
        Assert.AreEqual("stdout", output.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task Serialize_ErrorOutput_PreservesENameAndTraceback()
    {
        var notebook = new NotebookModel
        {
            Cells =
            {
                new CellModel
                {
                    Type = "code",
                    Source = "x",
                    Outputs =
                    {
                        new CellOutput(
                            "text/plain",
                            "bad value",
                            IsError: true,
                            ErrorName: "ValueError",
                            ErrorStackTrace: "frame 1\nframe 2")
                    }
                }
            }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var output = doc.RootElement.GetProperty("cells")[0].GetProperty("outputs")[0];
        Assert.AreEqual("error", output.GetProperty("output_type").GetString());
        Assert.AreEqual("ValueError", output.GetProperty("ename").GetString());
        Assert.AreEqual("bad value", output.GetProperty("evalue").GetString());
        var tb = output.GetProperty("traceback");
        Assert.AreEqual(2, tb.GetArrayLength());
        Assert.AreEqual("frame 1", tb[0].GetString());
        Assert.AreEqual("frame 2", tb[1].GetString());
    }

    [TestMethod]
    public async Task Serialize_DisplayData_FromImagePng()
    {
        var notebook = new NotebookModel
        {
            Cells =
            {
                new CellModel
                {
                    Type = "code",
                    Source = "plot()",
                    Outputs = { new CellOutput("image/png", "iVBORw0KGgo=") }
                }
            }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var output = doc.RootElement.GetProperty("cells")[0].GetProperty("outputs")[0];
        Assert.AreEqual("display_data", output.GetProperty("output_type").GetString());
        var data = output.GetProperty("data");
        Assert.AreEqual("iVBORw0KGgo=", data.GetProperty("image/png")[0].GetString());
    }

    [TestMethod]
    public async Task Serialize_MarkdownCell_HasNoOutputs()
    {
        var notebook = new NotebookModel
        {
            Cells = { new CellModel { Type = "markdown", Source = "# Title" } }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var cell = doc.RootElement.GetProperty("cells")[0];
        Assert.AreEqual("markdown", cell.GetProperty("cell_type").GetString());
        Assert.IsFalse(cell.TryGetProperty("outputs", out _));
        Assert.IsFalse(cell.TryGetProperty("execution_count", out _));
    }

    [TestMethod]
    public async Task Serialize_NonStandardCellType_FallsBackToRaw()
    {
        var notebook = new NotebookModel
        {
            Cells = { new CellModel { Type = "sql", Source = "SELECT 1" } }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var cell = doc.RootElement.GetProperty("cells")[0];
        Assert.AreEqual("raw", cell.GetProperty("cell_type").GetString());
        Assert.AreEqual("sql", cell.GetProperty("metadata").GetProperty("verso_type").GetString());
    }

    [TestMethod]
    public async Task Serialize_ExecutionCount_FromMetadata()
    {
        var notebook = new NotebookModel
        {
            Cells =
            {
                new CellModel
                {
                    Type = "code",
                    Source = "x = 1",
                    Metadata = { ["execution_count"] = 7 }
                }
            }
        };
        var json = await _serializer.SerializeAsync(notebook);

        using var doc = JsonDocument.Parse(json);
        var cell = doc.RootElement.GetProperty("cells")[0];
        Assert.AreEqual(7, cell.GetProperty("execution_count").GetInt32());
        Assert.IsFalse(cell.GetProperty("metadata").TryGetProperty("execution_count", out _),
            "execution_count should not be duplicated in cell metadata.");
    }

    [TestMethod]
    public async Task RoundTrip_StreamOutput_PreservesContent()
    {
        var original = @"{
            ""nbformat"": 4, ""nbformat_minor"": 5,
            ""metadata"": { ""kernelspec"": { ""language"": ""python"" } },
            ""cells"": [{
                ""cell_type"": ""code"",
                ""source"": ""print('hi')"",
                ""outputs"": [{
                    ""output_type"": ""stream"",
                    ""name"": ""stdout"",
                    ""text"": ""hi\n""
                }],
                ""metadata"": {},
                ""execution_count"": 3
            }]
        }";

        var notebook = await _serializer.DeserializeAsync(original);
        var roundtripped = await _serializer.SerializeAsync(notebook);
        var reread = await _serializer.DeserializeAsync(roundtripped);

        Assert.AreEqual(1, reread.Cells.Count);
        Assert.AreEqual("code", reread.Cells[0].Type);
        Assert.AreEqual("python", reread.Cells[0].Language);
        Assert.AreEqual("print('hi')", reread.Cells[0].Source);
        Assert.AreEqual(1, reread.Cells[0].Outputs.Count);
        Assert.AreEqual("text/plain", reread.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("hi\n", reread.Cells[0].Outputs[0].Content);
        Assert.AreEqual(3, Convert.ToInt32(reread.Cells[0].Metadata["execution_count"]));
    }
}
