using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Handlers;

/// <summary>
/// The cell/* mutation RPCs are also driven by clients that are not the notebook view
/// (for example the VS Code chat tools), so every structural mutation must announce
/// notebook/cellsChanged. Without it the view's cell cache and layout slots go stale:
/// a cell added through the RPC stays parked in the hidden cell pool and only appears
/// after the notebook is closed and reopened.
/// </summary>
[TestClass]
public class CellHandlerTests
{
    private async Task<(HostSession Session, string NotebookId, List<string> Notifications)> CreateOpenSession()
    {
        var notifications = new List<string>();
        var session = new HostSession(n => notifications.Add(n));
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, notifications);
    }

    private static JsonElement ToParams<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonRpcMessage.SerializerOptions);

    private static int CountCellsChanged(List<string> notifications) =>
        notifications.Count(n => n.Contains($"\"{MethodNames.NotebookCellsChanged}\""));

    [TestMethod]
    public async Task Add_AnnouncesCellsChanged()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        // The reported repro: an empty notebook. The client's cached layout chrome is the
        // zero-cell placeholder with no slots, so only this announcement makes it re-render.
        CellHandler.HandleAdd(ns, ToParams(new CellAddParams { Type = "code", Source = "1 + 1" }));

        Assert.AreEqual(1, CountCellsChanged(notifications),
            "cell/add must announce cellsChanged so views re-render their slots");
    }

    [TestMethod]
    public async Task Insert_AnnouncesCellsChanged()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        ns.Scaffold.AddCell("code");

        CellHandler.HandleInsert(ns, ToParams(new CellInsertParams { Index = 0, Type = "code" }));

        Assert.AreEqual(1, CountCellsChanged(notifications));
    }

    [TestMethod]
    public async Task Remove_AnnouncesCellsChanged_OnlyWhenCellExisted()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        var cell = ns.Scaffold.AddCell("code");

        CellHandler.HandleRemove(ns, ToParams(new CellRemoveParams { CellId = Guid.NewGuid().ToString() }));
        Assert.AreEqual(0, CountCellsChanged(notifications),
            "removing an unknown cell changes nothing and must stay silent");

        CellHandler.HandleRemove(ns, ToParams(new CellRemoveParams { CellId = cell.Id.ToString() }));
        Assert.AreEqual(1, CountCellsChanged(notifications));
    }

    [TestMethod]
    public async Task Move_AnnouncesCellsChanged_OnlyWhenIndexChanges()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        ns.Scaffold.AddCell("code");
        ns.Scaffold.AddCell("code");

        CellHandler.HandleMove(ns, ToParams(new CellMoveParams { FromIndex = 1, ToIndex = 1 }));
        Assert.AreEqual(0, CountCellsChanged(notifications),
            "a no-op move must stay silent");

        CellHandler.HandleMove(ns, ToParams(new CellMoveParams { FromIndex = 0, ToIndex = 1 }));
        Assert.AreEqual(1, CountCellsChanged(notifications));
    }

    [TestMethod]
    public async Task ChangeType_AnnouncesCellsChanged_OnlyWhenTypeChanges()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        var cell = ns.Scaffold.AddCell("code", source: "# heading?");

        CellHandler.HandleChangeType(ns, ToParams(new CellChangeTypeParams
        {
            CellId = cell.Id.ToString(),
            Type = "code"
        }));
        Assert.AreEqual(0, CountCellsChanged(notifications),
            "re-asserting the current type changes nothing and must stay silent");

        // Layout chrome carries per-type slot classes and heading folding, so a real
        // type change must trigger the same re-render as a structural change.
        CellHandler.HandleChangeType(ns, ToParams(new CellChangeTypeParams
        {
            CellId = cell.Id.ToString(),
            Type = "markdown"
        }));
        Assert.AreEqual(1, CountCellsChanged(notifications));
    }

    [TestMethod]
    public async Task UpdateSource_DoesNotAnnounceCellsChanged()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        var cell = ns.Scaffold.AddCell("code");

        // Content edits update the live cell component in place wherever it is portaled;
        // they do not change the slot set, so no structural announcement is expected.
        CellHandler.HandleUpdateSource(ns, ToParams(new CellUpdateSourceParams
        {
            CellId = cell.Id.ToString(),
            Source = "2 + 2"
        }));

        Assert.AreEqual(0, CountCellsChanged(notifications));
    }
}
