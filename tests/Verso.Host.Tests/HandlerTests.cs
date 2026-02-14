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

    private async Task<HostSession> CreateOpenSession()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        await NotebookHandler.HandleOpenAsync(session, openParams);
        return session;
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
        Assert.IsNotNull(session.Scaffold);
    }

    [TestMethod]
    public async Task NotebookGetLanguages_ReturnsRegisteredLanguages()
    {
        var session = await CreateOpenSession();

        var result = NotebookHandler.HandleGetLanguages(session);

        // CSharpKernel is loaded as a built-in extension
        Assert.IsTrue(result.Languages.Count > 0);
        Assert.IsTrue(result.Languages.Any(l => l.Id == "csharp"));
    }

    [TestMethod]
    public async Task CellAdd_AddsCodeCell()
    {
        var session = await CreateOpenSession();
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Language = "csharp", Source = "var x = 1;" },
            JsonRpcMessage.SerializerOptions);

        var result = CellHandler.HandleAdd(session, addParams);

        Assert.AreEqual("code", result.Type);
        Assert.AreEqual("csharp", result.Language);
        Assert.AreEqual("var x = 1;", result.Source);
        Assert.IsFalse(string.IsNullOrEmpty(result.Id));
    }

    [TestMethod]
    public async Task CellInsert_InsertsAtIndex()
    {
        var session = await CreateOpenSession();

        // Add two cells
        var addParams1 = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "first" }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleAdd(session, addParams1);

        var addParams2 = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "third" }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleAdd(session, addParams2);

        // Insert between them
        var insertParams = JsonSerializer.SerializeToElement(
            new CellInsertParams { Index = 1, Source = "second" },
            JsonRpcMessage.SerializerOptions);
        CellHandler.HandleInsert(session, insertParams);

        var cells = session.Scaffold!.Cells;
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual("second", cells[1].Source);
    }

    [TestMethod]
    public async Task CellRemove_RemovesByGuid()
    {
        var session = await CreateOpenSession();
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "to remove" }, JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(session, addParams);

        var removeParams = JsonSerializer.SerializeToElement(
            new CellRemoveParams { CellId = cell.Id }, JsonRpcMessage.SerializerOptions);
        CellHandler.HandleRemove(session, removeParams);

        Assert.AreEqual(0, session.Scaffold!.Cells.Count);
    }

    [TestMethod]
    public async Task CellUpdateSource_UpdatesContent()
    {
        var session = await CreateOpenSession();
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "old" }, JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(session, addParams);

        var updateParams = JsonSerializer.SerializeToElement(
            new CellUpdateSourceParams { CellId = cell.Id, Source = "new" },
            JsonRpcMessage.SerializerOptions);
        CellHandler.HandleUpdateSource(session, updateParams);

        var fetched = session.Scaffold!.GetCell(Guid.Parse(cell.Id));
        Assert.AreEqual("new", fetched!.Source);
    }

    [TestMethod]
    public async Task CellGet_ReturnsCell()
    {
        var session = await CreateOpenSession();
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "hello" }, JsonRpcMessage.SerializerOptions);
        var added = CellHandler.HandleAdd(session, addParams);

        var getParams = JsonSerializer.SerializeToElement(
            new CellGetParams { CellId = added.Id }, JsonRpcMessage.SerializerOptions);
        var result = CellHandler.HandleGet(session, getParams);

        Assert.IsNotNull(result);
        Assert.AreEqual("hello", result.Source);
    }

    [TestMethod]
    public async Task CellList_ReturnsAllCells()
    {
        var session = await CreateOpenSession();
        CellHandler.HandleAdd(session, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "a" }, JsonRpcMessage.SerializerOptions));
        CellHandler.HandleAdd(session, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "b" }, JsonRpcMessage.SerializerOptions));

        var result = CellHandler.HandleList(session);

        // Result is anonymous type with cells property; verify via JSON
        var json = JsonSerializer.Serialize(result, JsonRpcMessage.SerializerOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(2, doc.RootElement.GetProperty("cells").GetArrayLength());
    }

    [TestMethod]
    public async Task OutputClearAll_ClearsOutputs()
    {
        var session = await CreateOpenSession();
        OutputHandler.HandleClearAll(session);
        // Should not throw
        Assert.AreEqual(0, session.Scaffold!.Cells.Count);
    }

    [TestMethod]
    public async Task ExecutionCancel_DoesNotThrow()
    {
        var session = await CreateOpenSession();
        var result = ExecutionHandler.HandleCancel(session);
        var json = JsonSerializer.Serialize(result, JsonRpcMessage.SerializerOptions);
        Assert.IsTrue(json.Contains("true"));
    }

    [TestMethod]
    public async Task Dispatch_UnknownMethod_ReturnsMethodNotFoundError()
    {
        var session = await CreateOpenSession();
        var response = await session.DispatchAsync(1, "unknown/method", null);
        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.AreEqual(JsonRpcMessage.ErrorCodes.MethodNotFound, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public void EnsureSession_WithoutOpen_Throws()
    {
        var session = CreateSession();
        Assert.ThrowsException<InvalidOperationException>(() => session.EnsureSession());
    }

    [TestMethod]
    public async Task NotebookSave_ReturnsVersoContent()
    {
        var session = await CreateOpenSession();
        CellHandler.HandleAdd(session, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "Console.WriteLine(\"test\");" },
            JsonRpcMessage.SerializerOptions));

        var result = await NotebookHandler.HandleSaveAsync(session);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Content));
        Assert.IsTrue(result.Content.Contains("Console.WriteLine"));
    }

    [TestMethod]
    public async Task CellMove_ReordersCells()
    {
        var session = await CreateOpenSession();
        CellHandler.HandleAdd(session, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "first" }, JsonRpcMessage.SerializerOptions));
        CellHandler.HandleAdd(session, JsonSerializer.SerializeToElement(
            new CellAddParams { Source = "second" }, JsonRpcMessage.SerializerOptions));

        CellHandler.HandleMove(session, JsonSerializer.SerializeToElement(
            new CellMoveParams { FromIndex = 0, ToIndex = 1 },
            JsonRpcMessage.SerializerOptions));

        Assert.AreEqual("second", session.Scaffold!.Cells[0].Source);
        Assert.AreEqual("first", session.Scaffold!.Cells[1].Source);
    }

    [TestMethod]
    public async Task ExtensionList_ReturnsLoadedExtensions()
    {
        var session = await CreateOpenSession();

        var result = ExtensionHandler.HandleList(session);

        Assert.IsTrue(result.Extensions.Count > 0);
        Assert.IsTrue(result.Extensions.All(e => !string.IsNullOrEmpty(e.ExtensionId)));
        Assert.IsTrue(result.Extensions.All(e => e.Status == "Enabled"));
    }

    [TestMethod]
    public async Task ExtensionDisable_SetsStatusToDisabled()
    {
        var session = await CreateOpenSession();
        var extensions = ExtensionHandler.HandleList(session);
        var firstId = extensions.Extensions[0].ExtensionId;

        var disableParams = JsonSerializer.SerializeToElement(
            new ExtensionToggleParams { ExtensionId = firstId },
            JsonRpcMessage.SerializerOptions);

        var result = await ExtensionHandler.HandleDisableAsync(session, disableParams);

        var disabled = result.Extensions.First(e => e.ExtensionId == firstId);
        Assert.AreEqual("Disabled", disabled.Status);
    }

    [TestMethod]
    public async Task VariableList_ReturnsEmptyWhenNoVariables()
    {
        var session = await CreateOpenSession();

        var result = VariableHandler.HandleList(session);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Variables.Count);
    }

    [TestMethod]
    public async Task VariableList_ReturnsVariablesAfterSet()
    {
        var session = await CreateOpenSession();
        session.Scaffold!.Variables.Set("myVar", 42);

        var result = VariableHandler.HandleList(session);

        Assert.AreEqual(1, result.Variables.Count);
        Assert.AreEqual("myVar", result.Variables[0].Name);
        Assert.AreEqual("Int32", result.Variables[0].TypeName);
    }
}
