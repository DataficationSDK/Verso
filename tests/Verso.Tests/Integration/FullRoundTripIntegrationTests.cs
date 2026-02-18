using Verso.Abstractions;
using Verso.Extensions;
using Verso.Extensions.Themes;
using Verso.Extensions.Layouts;
using Verso.Serializers;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Integration;

[TestClass]
public sealed class FullRoundTripIntegrationTests
{
    [TestMethod]
    public async Task SerializeAndDeserialize_PreservesNotebookStructure()
    {
        var serializer = new VersoSerializer();

        var notebook = new NotebookModel
        {
            Title = "Integration Test",
            DefaultKernelId = "csharp",
            ActiveLayoutId = "notebook",
            PreferredThemeId = "verso-light",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            RequiredExtensions = new List<string> { "verso.kernel.csharp" },
            OptionalExtensions = new List<string> { "verso.theme.dark" }
        };

        notebook.Cells.Add(new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "var x = 42;",
            Outputs = new List<CellOutput>
            {
                new("text/plain", "42")
            }
        });

        notebook.Cells.Add(new CellModel
        {
            Type = "markdown",
            Source = "# Hello World"
        });

        // Serialize
        var json = await serializer.SerializeAsync(notebook);

        // Verify JSON schema
        Assert.IsTrue(json.Contains("\"verso\""));
        Assert.IsTrue(json.Contains("\"metadata\""));
        Assert.IsTrue(json.Contains("\"cells\""));
        Assert.IsTrue(json.Contains("\"title\""));
        Assert.IsTrue(json.Contains("\"defaultKernel\""));

        // Deserialize
        var result = await serializer.DeserializeAsync(json);

        // Verify equality
        Assert.AreEqual(notebook.Title, result.Title);
        Assert.AreEqual(notebook.DefaultKernelId, result.DefaultKernelId);
        Assert.AreEqual(notebook.ActiveLayoutId, result.ActiveLayoutId);
        Assert.AreEqual(notebook.PreferredThemeId, result.PreferredThemeId);
        Assert.AreEqual(notebook.Cells.Count, result.Cells.Count);

        Assert.AreEqual("code", result.Cells[0].Type);
        Assert.AreEqual("csharp", result.Cells[0].Language);
        Assert.AreEqual("var x = 42;", result.Cells[0].Source);
        Assert.AreEqual(1, result.Cells[0].Outputs.Count);
        Assert.AreEqual("42", result.Cells[0].Outputs[0].Content);

        Assert.AreEqual("markdown", result.Cells[1].Type);
        Assert.AreEqual("# Hello World", result.Cells[1].Source);
    }

    [TestMethod]
    public async Task ScaffoldExecution_SerializeDeserialize_OutputsSurvive()
    {
        // Build scaffold with fake kernel
        var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "fake";
        scaffold.RegisterKernel(new FakeLanguageKernel("fake"));

        var cell = scaffold.AddCell(source: "hello world");
        await scaffold.ExecuteCellAsync(cell.Id);

        // Verify execution produced output
        Assert.IsTrue(cell.Outputs.Count > 0);
        var originalOutput = cell.Outputs[0].Content;

        // Serialize
        var serializer = new VersoSerializer();
        var json = await serializer.SerializeAsync(scaffold.Notebook);

        // Deserialize
        var result = await serializer.DeserializeAsync(json);

        // Verify outputs survived
        Assert.AreEqual(1, result.Cells.Count);
        Assert.IsTrue(result.Cells[0].Outputs.Count > 0);
        Assert.AreEqual(originalOutput, result.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public async Task ScaffoldWithExtensionHost_InitializesSubsystems()
    {
        await using var extensionHost = new ExtensionHost();
        await extensionHost.LoadBuiltInExtensionsAsync();

        var notebook = new NotebookModel
        {
            PreferredThemeId = "verso-light",
            ActiveLayoutId = "notebook"
        };

        var scaffold = new Verso.Scaffold(notebook, extensionHost);
        scaffold.InitializeSubsystems();

        // ThemeEngine should be initialized
        Assert.IsNotNull(scaffold.ThemeEngine);
        Assert.IsNotNull(scaffold.ThemeEngine.ActiveTheme);
        Assert.AreEqual("verso-light", scaffold.ThemeEngine.ActiveTheme.ThemeId);

        // LayoutManager should be initialized
        Assert.IsNotNull(scaffold.LayoutManager);
        Assert.IsNotNull(scaffold.LayoutManager.ActiveLayout);
        Assert.AreEqual("notebook", scaffold.LayoutManager.ActiveLayout.LayoutId);

        // ThemeContext should delegate to ThemeEngine
        Assert.AreEqual(ThemeKind.Light, scaffold.ThemeContext.ThemeKind);
        Assert.AreEqual("#FFFFFF", scaffold.ThemeContext.GetColor("EditorBackground"));

        // LayoutCapabilities should delegate to LayoutManager
        Assert.IsTrue(scaffold.LayoutCapabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(scaffold.LayoutCapabilities.HasFlag(LayoutCapabilities.CellInsert));
    }

    [TestMethod]
    public void ThemeEngine_ResolvesFromVersoLightTheme()
    {
        var light = new VersoLightTheme();
        var engine = new ThemeEngine(new ITheme[] { light }, "verso-light");

        // Colors
        Assert.AreEqual("#FFFFFF", engine.GetColor("EditorBackground"));
        Assert.AreEqual("#1E1E1E", engine.GetColor("EditorForeground"));

        // Fonts
        var editorFont = engine.GetFont("EditorFont");
        Assert.AreEqual("Cascadia Code", editorFont.Family);
        Assert.AreEqual(14, editorFont.SizePx);

        // Spacing
        Assert.AreEqual(12, engine.GetSpacing("CellPadding"));
        Assert.AreEqual(8, engine.GetSpacing("CellGap"));

        // Syntax
        Assert.AreEqual("#0000FF", engine.GetSyntaxColor("keyword"));
        Assert.AreEqual("#008000", engine.GetSyntaxColor("comment"));
    }

    [TestMethod]
    public void LayoutManager_ReportsNotebookLayoutCapabilities()
    {
        var layout = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { layout }, "notebook");

        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.MultiSelect));
    }

    [TestMethod]
    public async Task ExtensionHost_DiscoversToolbarActions()
    {
        await using var extensionHost = new ExtensionHost();
        await extensionHost.LoadBuiltInExtensionsAsync();

        var actions = extensionHost.GetToolbarActions();
        Assert.IsTrue(actions.Count >= 4,
            $"Expected at least 4 toolbar actions, found {actions.Count}");

        var actionIds = actions.Select(a => a.ActionId).ToList();
        CollectionAssert.Contains(actionIds, "verso.action.run-all");
        CollectionAssert.Contains(actionIds, "verso.action.run-cell");
        CollectionAssert.Contains(actionIds, "verso.action.clear-outputs");
        CollectionAssert.Contains(actionIds, "verso.action.restart-kernel");
    }

    [TestMethod]
    public async Task ExtensionHost_DiscoversVersoSerializer()
    {
        await using var extensionHost = new ExtensionHost();
        await extensionHost.LoadBuiltInExtensionsAsync();

        var serializers = extensionHost.GetSerializers();
        Assert.IsTrue(serializers.Any(s => s.FormatId == "verso"),
            "VersoSerializer should be discovered by ExtensionHost");
    }
}
