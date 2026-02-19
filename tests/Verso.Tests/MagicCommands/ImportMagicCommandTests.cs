using Verso.Abstractions;
using Verso.Contexts;
using Verso.MagicCommands;
using Verso.Serializers;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;
using StubNotebookOperations = Verso.Testing.Stubs.StubNotebookOperations;

namespace Verso.Tests.MagicCommands;

[TestClass]
public sealed class ImportMagicCommandTests
{
    // --- Metadata ---

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        var command = new ImportMagicCommand();

        Assert.AreEqual("import", command.Name);
        Assert.AreEqual("verso.magic.import", command.ExtensionId);
        Assert.AreEqual("1.0.0", command.Version);
        Assert.AreEqual(1, command.Parameters.Count);
        Assert.AreEqual("path", command.Parameters[0].Name);
        Assert.IsTrue(command.Parameters[0].IsRequired);
    }

    // --- Argument validation ---

    [TestMethod]
    public async Task EmptyArguments_SuppressesAndWritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("#!import"));
    }

    [TestMethod]
    public async Task WhitespaceArguments_SuppressesAndWritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("   ", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
    }

    // --- File not found ---

    [TestMethod]
    public async Task FileNotFound_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = CreateContextWithSerializer();

        await command.ExecuteAsync("/nonexistent/path/notebook.verso", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("File not found"));
    }

    // --- Unsupported format ---

    [TestMethod]
    public async Task UnsupportedExtension_WritesNoSerializerError()
    {
        var command = new ImportMagicCommand();
        var context = CreateContextWithSerializer();

        // Create a temp .dib file (unsupported format)
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.dib");
        try
        {
            await File.WriteAllTextAsync(tempFile, "some content");

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(1, context.WrittenOutputs.Count);
            Assert.IsTrue(context.WrittenOutputs[0].IsError);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("No serializer"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Successful import ---

    [TestMethod]
    public async Task SuccessfulImport_ExecutesOnlyCodeCells()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        // Create a temp .verso file with mixed cell types
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });
            notebook.Cells.Add(new CellModel { Type = "markdown", Source = "# Header" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var y = 2;" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "" }); // empty — should be skipped
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "   " }); // whitespace — should be skipped

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(2, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("var x = 1;", notebookOps.ExecutedCodeCalls[0].Code);
            Assert.AreEqual("csharp", notebookOps.ExecutedCodeCalls[0].Language);
            Assert.AreEqual("var y = 2;", notebookOps.ExecutedCodeCalls[1].Code);

            // Confirmation message
            Assert.AreEqual(1, context.WrittenOutputs.Count);
            Assert.IsFalse(context.WrittenOutputs[0].IsError);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("2 code cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task SuccessfulImport_SingleCell_UsesSingular()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("1 code cell"));
            Assert.IsFalse(context.WrittenOutputs[0].Content.Contains("cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Path resolution ---

    [TestMethod]
    public void ResolvePath_AbsolutePath_ReturnsAsIs()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "notebook.verso");
        var result = ImportMagicCommand.ResolvePath(absolutePath, "/some/dir/current.verso");

        Assert.AreEqual(Path.GetFullPath(absolutePath), result);
    }

    [TestMethod]
    public void ResolvePath_RelativePath_ResolvesAgainstNotebookDir()
    {
        var notebookPath = Path.Combine(Path.GetTempPath(), "notebooks", "current.verso");
        var result = ImportMagicCommand.ResolvePath("helpers/setup.verso", notebookPath);

        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "notebooks", "helpers", "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_NullNotebookPath_ResolvesAgainstCwd()
    {
        var result = ImportMagicCommand.ResolvePath("setup.verso", null);

        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_EmptyNotebookPath_ResolvesAgainstCwd()
    {
        var result = ImportMagicCommand.ResolvePath("setup.verso", "");

        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    // --- Helpers ---

    private static StubMagicCommandContext CreateContextWithSerializer(
        StubNotebookOperations? notebookOps = null)
    {
        var extensionHost = new SerializerAwareStubExtensionHost();
        var context = new StubMagicCommandContext
        {
            ExtensionHost = extensionHost
        };
        if (notebookOps is not null)
            context.Notebook = notebookOps;
        return context;
    }

    /// <summary>
    /// Stub extension host that returns a <see cref="VersoSerializer"/> from <see cref="GetSerializers"/>.
    /// </summary>
    private sealed class SerializerAwareStubExtensionHost : IExtensionHostContext
    {
        private readonly IReadOnlyList<INotebookSerializer> _serializers = new INotebookSerializer[]
        {
            new VersoSerializer()
        };

        public IReadOnlyList<IExtension> GetLoadedExtensions() => Array.Empty<IExtension>();
        public IReadOnlyList<ILanguageKernel> GetKernels() => Array.Empty<ILanguageKernel>();
        public IReadOnlyList<ICellRenderer> GetRenderers() => Array.Empty<ICellRenderer>();
        public IReadOnlyList<IDataFormatter> GetFormatters() => Array.Empty<IDataFormatter>();
        public IReadOnlyList<ICellType> GetCellTypes() => Array.Empty<ICellType>();
        public IReadOnlyList<INotebookSerializer> GetSerializers() => _serializers;
        public IReadOnlyList<ILayoutEngine> GetLayouts() => Array.Empty<ILayoutEngine>();
        public IReadOnlyList<ITheme> GetThemes() => Array.Empty<ITheme>();
        public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => Array.Empty<INotebookPostProcessor>();
        public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => Array.Empty<ExtensionInfo>();
        public Task EnableExtensionAsync(string extensionId) => Task.CompletedTask;
        public Task DisableExtensionAsync(string extensionId) => Task.CompletedTask;
    }
}
