using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Serializers;

namespace Verso.Host.Tests;

[TestClass]
public class HandlerTests
{
    private HostSession CreateSession()
    {
        var notifications = new List<string>();
        return new HostSession(n => notifications.Add(n));
    }

    private async Task<(HostSession Session, string NotebookId)> CreateOpenSession()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId);
    }

    private NotebookSession GetNs(HostSession session, string notebookId)
    {
        return session.GetSession(notebookId);
    }

    private static JsonElement RunParams(Guid cellId) =>
        JsonSerializer.SerializeToElement(
            new ExecutionRunParams { CellId = cellId.ToString() },
            JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task Run_ReportsDirtyPerCellTypePersistence()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        // Parameters and Markdown outputs are transient (re-rendered on open), so executing
        // them (including the auto-render on open) must not report the notebook as dirty.
        var paramCell = ns.Scaffold.AddCell("parameters");
        var mdCell = ns.Scaffold.AddCell("markdown", source: "# hi");
        // HTML outputs are persisted, so executing an HTML cell reports dirty.
        var htmlCell = ns.Scaffold.AddCell("html", source: "<p>hi</p>");

        var paramResult = await ExecutionHandler.HandleRunAsync(ns, RunParams(paramCell.Id));
        var mdResult = await ExecutionHandler.HandleRunAsync(ns, RunParams(mdCell.Id));
        var htmlResult = await ExecutionHandler.HandleRunAsync(ns, RunParams(htmlCell.Id));

        Assert.IsFalse(paramResult.Dirty, "parameters auto-render must not dirty the notebook");
        Assert.IsFalse(mdResult.Dirty, "markdown render must not dirty the notebook");
        Assert.IsTrue(htmlResult.Dirty, "a persisted-output cell must dirty the notebook when executed");
    }

    private static JsonElement InteractParams(
        Guid cellId, string interactionType, string payload) =>
        JsonSerializer.SerializeToElement(
            new CellInteractParams
            {
                CellId = cellId.ToString(),
                ExtensionId = "verso.renderer.parameters",
                InteractionType = interactionType,
                Payload = payload,
                Region = "Output"
            },
            JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task Interact_ReportsDirtyOnlyWhenStateChanges()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var paramCell = ns.Scaffold.AddCell("parameters");

        // Editing a parameter definition changes persisted state, so it must dirty the
        // notebook even though no cell executed. The out-of-process host learns this only
        // by reporting context.StateChanged back on the interaction result.
        var addResult = await InteractionHandler.HandleInteractAsync(
            ns, InteractParams(paramCell.Id, "parameter-add", "{\"name\":\"start\",\"type\":\"string\"}"));

        // Submitting the form only sets runtime variable values, which are not persisted,
        // so it must not dirty the notebook.
        var submitResult = await InteractionHandler.HandleInteractAsync(
            ns, InteractParams(paramCell.Id, "parameter-submit", "{\"values\":{}}"));

        Assert.IsTrue(addResult.Dirty, "a parameter-definition edit must dirty the notebook");
        Assert.IsFalse(submitResult.Dirty, "submitting parameter values must not dirty the notebook");
    }

    [TestMethod]
    public async Task GetTheme_IncludesLayoutPaletteTokens()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var engine = ns.Scaffold.ThemeEngine;
        Assert.IsNotNull(engine);
        var theme = engine!.AvailableThemes.FirstOrDefault();
        Assert.IsNotNull(theme, "expected at least one registered theme");
        engine.SetActiveTheme(theme!.ThemeId);

        var result = ThemeHandler.HandleGetTheme(ns);

        Assert.IsNotNull(result);
        // The layout-extension palette must round-trip through the host bridge so
        // out-of-process hosts do not fall back to default colors for these tokens.
        foreach (var key in new[] { "bgDefault", "bgElevated", "fgDefault", "fgMuted" })
            Assert.IsTrue(result!.Colors.ContainsKey(key), $"theme colors missing '{key}'");
    }

    [TestMethod]
    public async Task NotebookOpen_EmptyContent_CreatesEmptyNotebook()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);

        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Cells.Count);
        Assert.IsFalse(string.IsNullOrEmpty(result.NotebookId));
    }

    [TestMethod]
    public async Task NotebookOpen_ReturnsNotebookId()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);

        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        Assert.IsTrue(result.NotebookId.StartsWith("nb-"));
    }

    [TestMethod]
    public async Task MultipleNotebookOpen_ReturnsDifferentIds()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);

        var result1 = await NotebookHandler.HandleOpenAsync(session, openParams);
        var result2 = await NotebookHandler.HandleOpenAsync(session, openParams);

        Assert.AreNotEqual(result1.NotebookId, result2.NotebookId);
    }

    [TestMethod]
    public async Task Open_RendersMarkdownCells()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "markdown", Source = "# Title" });
        notebook.Cells.Add(new CellModel { Type = "markdown", Source = "   " });
        notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "1 + 1" });
        var content = await new VersoSerializer().SerializeAsync(notebook);

        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = content },
            JsonRpcMessage.SerializerOptions);

        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        // Markdown cells with source arrive already rendered so the client displays them
        // on first paint instead of showing raw source until each cell is run.
        Assert.AreEqual(1, result.Cells[0].Outputs.Count);
        Assert.AreEqual("text/html", result.Cells[0].Outputs[0].MimeType);
        StringAssert.Contains(result.Cells[0].Outputs[0].Content, "<h1");
        // Blank markdown cells have nothing to render, and code cells never run at open.
        Assert.AreEqual(0, result.Cells[1].Outputs.Count);
        Assert.AreEqual(0, result.Cells[2].Outputs.Count);
    }

    [TestMethod]
    public async Task NotebookClose_RemovesSession()
    {
        var (session, notebookId) = await CreateOpenSession();

        var closeParams = JsonSerializer.SerializeToElement(
            new NotebookCloseParams { NotebookId = notebookId },
            JsonRpcMessage.SerializerOptions);
        await NotebookHandler.HandleCloseAsync(session, closeParams);

        Assert.ThrowsException<InvalidOperationException>(() => session.GetSession(notebookId));
    }

    [TestMethod]
    public async Task Dispatch_MissingNotebookId_ReturnsError()
    {
        var (session, _) = await CreateOpenSession();

        // Call a method that requires notebookId but don't provide it
        var response = await session.DispatchAsync(1, "cell/list", null);
        using var doc = JsonDocument.Parse(response);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
    }

    [TestMethod]
    public async Task Dispatch_InvalidNotebookId_ReturnsError()
    {
        var (session, _) = await CreateOpenSession();

        var @params = JsonSerializer.SerializeToElement(
            new { notebookId = "nb-nonexistent" },
            JsonRpcMessage.SerializerOptions);

        var response = await session.DispatchAsync(1, "cell/list", @params);
        using var doc = JsonDocument.Parse(response);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
    }

    [TestMethod]
    public async Task CellAdd_OnNotebookA_NotVisibleOnNotebookB()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);

        var resultA = await NotebookHandler.HandleOpenAsync(session, openParams);
        var resultB = await NotebookHandler.HandleOpenAsync(session, openParams);

        var nsA = session.GetSession(resultA.NotebookId);
        var nsB = session.GetSession(resultB.NotebookId);

        // Add a cell to notebook A
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Source = "var x = 1;" },
            JsonRpcMessage.SerializerOptions);
        CellHandler.HandleAdd(nsA, addParams);

        // Notebook A should have 1 cell, notebook B should have 0
        Assert.AreEqual(1, nsA.Scaffold.Cells.Count);
        Assert.AreEqual(0, nsB.Scaffold.Cells.Count);
    }

    [TestMethod]
    public async Task NotebookGetLanguages_ReturnsRegisteredLanguages()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var result = NotebookHandler.HandleGetLanguages(ns);

        // CSharpKernel is loaded as a built-in extension
        Assert.IsTrue(result.Languages.Count > 0);
        Assert.IsTrue(result.Languages.Any(l => l.Id == "csharp"));
    }

    [TestMethod]
    public async Task CellAdd_AddsCodeCell()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Language = "csharp", Source = "var x = 1;" },
            JsonRpcMessage.SerializerOptions);

        var result = CellHandler.HandleAdd(ns, addParams);

        Assert.AreEqual("code", result.Type);
        Assert.AreEqual("csharp", result.Language);
        Assert.AreEqual("var x = 1;", result.Source);
        Assert.IsFalse(string.IsNullOrEmpty(result.Id));
    }

    [TestMethod]
    public async Task CellInsert_InsertsAtIndex()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        // Add two cells
        var addParams1 = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "first" }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleAdd(ns, addParams1);

        var addParams2 = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "third" }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleAdd(ns, addParams2);

        // Insert between them
        var insertParams = JsonSerializer.SerializeToElement(
            new CellInsertParams { Index = 1, Source = "second" },
            JsonRpcMessage.SerializerOptions);
        CellHandler.HandleInsert(ns, insertParams);

        var cells = ns.Scaffold.Cells;
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual("second", cells[1].Source);
    }

    [TestMethod]
    public async Task CellRemove_RemovesByGuid()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "to remove" }, JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var removeParams = JsonSerializer.SerializeToElement(
            new CellRemoveParams { CellId = cell.Id }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleRemove(ns, removeParams);

        Assert.AreEqual(0, ns.Scaffold.Cells.Count);
    }

    [TestMethod]
    public async Task CellUpdateSource_UpdatesContent()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "old" }, JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var updateParams = JsonSerializer.SerializeToElement(
            new CellUpdateSourceParams { CellId = cell.Id, Source = "new" },
            JsonRpcMessage.SerializerOptions);
        CellHandler.HandleUpdateSource(ns, updateParams);

        var fetched = ns.Scaffold.GetCell(Guid.Parse(cell.Id));
        Assert.AreEqual("new", fetched!.Source);
    }

    [TestMethod]
    public async Task CellGet_ReturnsCell()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "hello" }, JsonRpcMessage.SerializerOptions);
        var added = CellHandler.HandleAdd(ns, addParams);

        var getParams = JsonSerializer.SerializeToElement(
            new CellGetParams { CellId = added.Id }, JsonRpcMessage.SerializerOptions);
        var result = CellHandler.HandleGet(ns, getParams);

        Assert.IsNotNull(result);
        Assert.AreEqual("hello", result.Source);
    }

    [TestMethod]
    public async Task CellList_ReturnsAllCells()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "a" }, JsonRpcMessage.SerializerOptions));
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "b" }, JsonRpcMessage.SerializerOptions));

        var result = CellHandler.HandleList(ns);

        // Result is anonymous type with cells property; verify via JSON
        var json = JsonSerializer.Serialize(result, JsonRpcMessage.SerializerOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(2, doc.RootElement.GetProperty("cells").GetArrayLength());
    }

    [TestMethod]
    public async Task OutputClearAll_ClearsOutputs()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        OutputHandler.HandleClearAll(ns);
        // Should not throw
        Assert.AreEqual(0, ns.Scaffold.Cells.Count);
    }

    [TestMethod]
    public async Task ExecutionCancel_DoesNotThrow()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var result = ExecutionHandler.HandleCancel(ns);
        var json = JsonSerializer.Serialize(result, JsonRpcMessage.SerializerOptions);
        Assert.IsTrue(json.Contains("true"));
    }

    [TestMethod]
    public async Task Dispatch_UnknownMethod_ReturnsMethodNotFoundError()
    {
        var (session, notebookId) = await CreateOpenSession();
        var @params = JsonSerializer.SerializeToElement(
            new { notebookId },
            JsonRpcMessage.SerializerOptions);
        var response = await session.DispatchAsync(1, "unknown/method", @params);
        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.AreEqual(JsonRpcMessage.ErrorCodes.MethodNotFound, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task NotebookSave_NoParams_ReturnsVersoContent()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "Console.WriteLine(\"test\");" },
            JsonRpcMessage.SerializerOptions));

        var result = await NotebookHandler.HandleSaveAsync(ns, null);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Content));
        Assert.IsTrue(result.Content.Contains("Console.WriteLine"));
        Assert.IsTrue(result.Content.TrimStart().StartsWith("{"), "Verso format is JSON.");
        Assert.IsTrue(result.Content.Contains("\"verso\""), "Verso format includes a 'verso' format-version field.");
    }

    [TestMethod]
    public async Task NotebookSave_JupyterFormat_ReturnsIpynbContent()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "print hello" },
            JsonRpcMessage.SerializerOptions));

        var paramsEl = JsonSerializer.SerializeToElement(new { format = "jupyter" });
        var result = await NotebookHandler.HandleSaveAsync(ns, paramsEl);

        using var doc = JsonDocument.Parse(result.Content);
        Assert.AreEqual(4, doc.RootElement.GetProperty("nbformat").GetInt32());
        var source = doc.RootElement.GetProperty("cells")[0].GetProperty("source");
        Assert.AreEqual("print hello", source[0].GetString());
    }

    [TestMethod]
    public async Task NotebookSave_UnknownFormat_FallsBackToVerso()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var paramsEl = JsonSerializer.SerializeToElement(new { format = "no-such-format" });
        var result = await NotebookHandler.HandleSaveAsync(ns, paramsEl);

        Assert.IsTrue(result.Content.Contains("\"verso\""),
            "Unknown format should fall back to verso, not throw.");
    }

    [TestMethod]
    public async Task NotebookSave_NonObjectParams_FallsBackToVerso()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        // JSON null, an array, and a primitive are all legal JSON-RPC params shapes;
        // none should cause TryGetProperty to throw.
        var jsonNull = JsonDocument.Parse("null").RootElement;
        var jsonArray = JsonDocument.Parse("[1,2,3]").RootElement;
        var jsonNumber = JsonDocument.Parse("42").RootElement;

        var nullResult = await NotebookHandler.HandleSaveAsync(ns, jsonNull);
        var arrayResult = await NotebookHandler.HandleSaveAsync(ns, jsonArray);
        var numberResult = await NotebookHandler.HandleSaveAsync(ns, jsonNumber);

        Assert.IsTrue(nullResult.Content.Contains("\"verso\""));
        Assert.IsTrue(arrayResult.Content.Contains("\"verso\""));
        Assert.IsTrue(numberResult.Content.Contains("\"verso\""));
    }

    [TestMethod]
    public async Task CellMove_ReordersCells()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "first" }, JsonRpcMessage.SerializerOptions));
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "second" }, JsonRpcMessage.SerializerOptions));

        CellHandler.HandleMove(ns, JsonSerializer.SerializeToElement(
            new CellMoveParams { FromIndex = 0, ToIndex = 1 },
            JsonRpcMessage.SerializerOptions));

        Assert.AreEqual("second", ns.Scaffold.Cells[0].Source);
        Assert.AreEqual("first", ns.Scaffold.Cells[1].Source);
    }

    [TestMethod]
    public async Task ExtensionList_ReturnsLoadedExtensions()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var result = ExtensionHandler.HandleList(ns);

        Assert.IsTrue(result.Extensions.Count > 0);
        Assert.IsTrue(result.Extensions.All(e => !string.IsNullOrEmpty(e.ExtensionId)));
        Assert.IsTrue(result.Extensions.All(e => e.Status == "Enabled"));
    }

    [TestMethod]
    public async Task ExtensionList_ReportsThePackageSources()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var result = ExtensionHandler.HandleList(ns);

        // The panel says where results came from, so an out-of-process host has to send it.
        Assert.IsTrue(result.Sources.Count > 0);
        Assert.IsTrue(result.Sources.All(s => !string.IsNullOrWhiteSpace(s)));
    }

    [TestMethod]
    public async Task ExtensionList_InstalledPackage_CarriesWhatItRegistered()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        ns.Scaffold.Notebook.RequiredExtensions.Add("Acme.Layouts@1.0.0");
        ns.Scaffold.Notebook.RequiredExtensions.Add("Acme.NotLoaded@1.0.0");
        var loaded = ns.ExtensionHost.GetExtensionInfos()
            .First(e => e.Capabilities.Contains("LayoutEngine"));
        ns.ExtensionHost.AttributeExtensionsToPackage("Acme.Layouts", new[] { loaded.ExtensionId });

        var result = ExtensionHandler.HandleList(ns);

        var attributed = result.Installed.First(i => i.Id == "Acme.Layouts");
        Assert.IsNotNull(attributed.Capabilities);
        CollectionAssert.Contains(attributed.Capabilities, "LayoutEngine");

        // Null has to survive the round trip: a package nobody has loaded must not be
        // reported as one that contributed nothing.
        var unloaded = result.Installed.First(i => i.Id == "Acme.NotLoaded");
        Assert.IsNull(unloaded.Capabilities);
    }

    [TestMethod]
    public async Task ExtensionDisable_SetsStatusToDisabled()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        var extensions = ExtensionHandler.HandleList(ns);
        var firstId = extensions.Extensions[0].ExtensionId;

        var disableParams = JsonSerializer.SerializeToElement(
            new ExtensionToggleParams { ExtensionId = firstId },
            JsonRpcMessage.SerializerOptions);

        var result = await ExtensionHandler.HandleDisableAsync(ns, disableParams);

        var disabled = result.Extensions.First(e => e.ExtensionId == firstId);
        Assert.AreEqual("Disabled", disabled.Status);
    }

    [TestMethod]
    public async Task VariableList_ReturnsEmptyWhenNoVariables()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);

        var result = VariableHandler.HandleList(ns);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Variables.Count);
    }

    [TestMethod]
    public async Task VariableList_ReturnsVariablesAfterSet()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        ns.Scaffold.Variables.Set("myVar", 42);

        var result = VariableHandler.HandleList(ns);

        Assert.AreEqual(1, result.Variables.Count);
        Assert.AreEqual("myVar", result.Variables[0].Name);
        Assert.AreEqual("Int32", result.Variables[0].TypeName);
    }

    [TestMethod]
    public async Task Open_ReturnsResolvedActiveLayout()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);

        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        // The open response must carry the resolved active layout so clients can select
        // the correct layout renderer on their first paint instead of re-rendering after
        // a separate layout/getLayouts round trip.
        Assert.IsNotNull(result.ActiveLayout);
        Assert.AreEqual(LayoutDefaults.LayoutId, result.ActiveLayout.Id);
        Assert.AreEqual(LayoutDefaults.ExtensionId, result.ActiveLayout.ExtensionId);
        Assert.IsTrue(result.ActiveLayout.IsActive);
    }
}
