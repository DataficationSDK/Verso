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
    public void SerializeAsync_ThrowsNotSupported()
    {
        Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => _serializer.SerializeAsync(new NotebookModel()));
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
}
