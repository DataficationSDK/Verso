using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class InteractionHandlerTests
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

    [TestMethod]
    public async Task HandleInteract_ValidHandler_ReturnsResponse()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeCellInteractionHandler();
        handler.ResponseToReturn = "<table>page 2</table>";
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var cellId = Guid.NewGuid();
        var interactParams = JsonSerializer.SerializeToElement(new CellInteractParams
        {
            CellId = cellId.ToString(),
            ExtensionId = handler.ExtensionId,
            InteractionType = "paginate",
            Payload = "{\"page\":2}",
            OutputBlockId = "block-1",
            Region = "Output"
        }, JsonRpcMessage.SerializerOptions);

        var result = await InteractionHandler.HandleInteractAsync(ns, interactParams);

        Assert.AreEqual("<table>page 2</table>", result.Response);
        Assert.AreEqual(1, handler.ReceivedInteractions.Count);
        Assert.AreEqual("paginate", handler.ReceivedInteractions[0].InteractionType);
        Assert.AreEqual("{\"page\":2}", handler.ReceivedInteractions[0].Payload);
    }

    [TestMethod]
    public async Task HandleInteract_UnknownExtensionId_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var interactParams = JsonSerializer.SerializeToElement(new CellInteractParams
        {
            CellId = Guid.NewGuid().ToString(),
            ExtensionId = "com.unknown.extension",
            InteractionType = "click",
            Payload = "",
            Region = "Output"
        }, JsonRpcMessage.SerializerOptions);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => InteractionHandler.HandleInteractAsync(ns, interactParams));
    }

    [TestMethod]
    public async Task HandleInteract_InvalidRegion_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeCellInteractionHandler();
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var interactParams = JsonSerializer.SerializeToElement(new CellInteractParams
        {
            CellId = Guid.NewGuid().ToString(),
            ExtensionId = handler.ExtensionId,
            InteractionType = "click",
            Payload = "",
            Region = "InvalidRegion"
        }, JsonRpcMessage.SerializerOptions);

        await Assert.ThrowsExceptionAsync<JsonException>(
            () => InteractionHandler.HandleInteractAsync(ns, interactParams));
    }

    [TestMethod]
    public async Task HandleInteract_HandlerReturnsNull_ResultIsNull()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeCellInteractionHandler();
        handler.ResponseToReturn = null;
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var interactParams = JsonSerializer.SerializeToElement(new CellInteractParams
        {
            CellId = Guid.NewGuid().ToString(),
            ExtensionId = handler.ExtensionId,
            InteractionType = "click",
            Payload = "",
            Region = "Input"
        }, JsonRpcMessage.SerializerOptions);

        var result = await InteractionHandler.HandleInteractAsync(ns, interactParams);

        Assert.IsNull(result.Response);
    }
}
