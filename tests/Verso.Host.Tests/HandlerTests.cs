using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

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
    public async Task NotebookSave_ReturnsVersoContent()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = GetNs(session, notebookId);
        CellHandler.HandleAdd(ns, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "Console.WriteLine(\"test\");" },
            JsonRpcMessage.SerializerOptions));

        var result = await NotebookHandler.HandleSaveAsync(ns);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Content));
        Assert.IsTrue(result.Content.Contains("Console.WriteLine"));
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
}
