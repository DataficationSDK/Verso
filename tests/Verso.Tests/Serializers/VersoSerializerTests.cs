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

        Assert.AreEqual("1.0", result.FormatVersion);
        Assert.AreEqual(0, result.Cells.Count);
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
            ActiveLayoutId = "notebook",
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

        Assert.AreEqual("1.0", result.FormatVersion);
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
            ActiveLayoutId = null,
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
        Assert.AreEqual("0.1.0", _serializer.Version);
        Assert.AreEqual("Verso Contributors", _serializer.Author);
        Assert.IsNotNull(_serializer.Description);
    }
}
