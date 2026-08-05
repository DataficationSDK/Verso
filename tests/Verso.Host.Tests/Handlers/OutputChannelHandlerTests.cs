using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Handlers;

/// <summary>
/// Covers the out-of-process host's half of an output channel: the leg that carries a view's word
/// back to whoever owns the channel it is drawing, and the leg that carries an owner's message out
/// as a notification. The client is stood in for by the recorded notification list, so what is
/// under test is the routing rather than anything drawn.
/// </summary>
[TestClass]
public class OutputChannelHandlerTests
{
    private async Task<(HostSession Session, string NotebookId, List<string> Notifications)>
        CreateOpenSessionAsync()
    {
        var notifications = new List<string>();
        var session = new HostSession(n => notifications.Add(n));
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, notifications);
    }

    private static JsonElement Params(object value)
        => JsonSerializer.SerializeToElement(value, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task OpeningANotebookGivesItsChannelsSomewhereToSend()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        Assert.IsTrue(
            ns.Scaffold.OutputChannels.CanReachViews,
            "An open notebook in this host has a client drawing it, so its channels can reach a view.");
    }

    [TestMethod]
    public async Task AViewSayingItIsReadyReleasesWhatWasHeldForIt()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        await channel.PostMessageAsync("greeting", new { text = "before the view existed" });
        notifications.Clear();

        await OutputChannelHandler.HandleReadyAsync(ns, Params(new ChannelReadyParams
        {
            ChannelId = channel.ChannelId,
            ProtocolVersion = OutputChannelProtocol.Version
        }));

        Assert.AreEqual(1, notifications.Count, "The queued message should have been sent on mount.");

        using var doc = JsonDocument.Parse(notifications[0]);
        var root = doc.RootElement;
        Assert.AreEqual(MethodNames.ChannelPost, root.GetProperty("method").GetString());

        var p = root.GetProperty("params");
        Assert.AreEqual(notebookId, p.GetProperty("notebookId").GetString());
        Assert.AreEqual(channel.ChannelId, p.GetProperty("channelId").GetString());
        Assert.AreEqual("ext/greeting", p.GetProperty("messageType").GetString());
        Assert.AreEqual(
            "before the view existed",
            p.GetProperty("payload").GetProperty("text").GetString());
    }

    [TestMethod]
    public void AnOutputWithAChannelTellsTheClientWhichOne()
    {
        // The two CellOutputDto declarations are separate types with nothing shared between them,
        // so a field that stops being mapped here goes missing rather than failing to compile, and
        // a widget with no channel id is a widget nothing can talk to.
        Assert.AreEqual(
            "chan-1",
            NotebookHandler.MapOutput(CellOutput.Widget("<p>live</p>", "chan-1")).LiveChannelId);

        Assert.IsNull(
            NotebookHandler.MapOutput(CellOutput.Widget("<p>static</p>")).LiveChannelId,
            "An output that is finished when it is written names no channel.");
    }

    [TestMethod]
    public async Task AMessageFromAViewReachesTheChannelOwner()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        var received = new List<OutputChannelMessage>();
        channel.MessageReceived += message =>
        {
            received.Add(message);
            return Task.CompletedTask;
        };

        await OutputChannelHandler.HandleMessageAsync(ns, Params(new ChannelMessageParams
        {
            ChannelId = channel.ChannelId,
            MessageType = "slider",
            Payload = "{\"value\":3}"
        }));

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual("slider", received[0].Type, "The owner sees the type the view posted.");
        Assert.AreEqual("{\"value\":3}", received[0].Payload);
    }

    [TestMethod]
    public async Task BuffersArriveAsBytes()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        OutputChannelMessage? received = null;
        channel.MessageReceived += message =>
        {
            received = message;
            return Task.CompletedTask;
        };

        await OutputChannelHandler.HandleMessageAsync(ns, Params(new ChannelMessageParams
        {
            ChannelId = channel.ChannelId,
            MessageType = "frame",
            Buffers = new List<string>
            {
                Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                "not base64 at all!"
            }
        }));

        Assert.IsNotNull(received);
        Assert.IsNotNull(received!.Buffers);
        Assert.AreEqual(1, received.Buffers!.Count, "The buffer that would not decode is dropped.");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, received.Buffers[0]);
    }

    [TestMethod]
    public async Task TalkingToAChannelThatIsNotOpenDoesNothing()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        await OutputChannelHandler.HandleReadyAsync(ns, Params(new ChannelReadyParams
        {
            ChannelId = "no-such-channel"
        }));

        await OutputChannelHandler.HandleMessageAsync(ns, Params(new ChannelMessageParams
        {
            ChannelId = "no-such-channel",
            MessageType = "slider"
        }));

        Assert.AreEqual(0, notifications.Count, "A view that outlived its channel is not an error.");
    }

    [TestMethod]
    public async Task AMessageWithoutAChannelIdIsRejected()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        await Assert.ThrowsExceptionAsync<JsonException>(
            () => OutputChannelHandler.HandleMessageAsync(ns, Params(new ChannelMessageParams
            {
                MessageType = "slider"
            })));

        await Assert.ThrowsExceptionAsync<JsonException>(
            () => OutputChannelHandler.HandleReadyAsync(ns, Params(new ChannelReadyParams())));
    }

    [TestMethod]
    public async Task ClosingTheSessionStopsTheNotificationsRatherThanWritingThemToNobody()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        await OutputChannelHandler.HandleReadyAsync(ns, Params(new ChannelReadyParams
        {
            ChannelId = channel.ChannelId
        }));
        notifications.Clear();

        await session.RemoveSessionAsync(notebookId);

        Assert.AreEqual(
            0,
            notifications.Count,
            "The client let the notebook go, so there is nothing left to tell about its channels.");
    }

    [TestMethod]
    public async Task AMessageSurvivesTheWireAsItWasWritten()
    {
        // The handlers are reached through the dispatcher in the running host, so the shape the
        // interop actually sends is what is fed in here rather than a DTO built in the test.
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        OutputChannelMessage? received = null;
        channel.MessageReceived += message =>
        {
            received = message;
            return Task.CompletedTask;
        };

        var wire =
            "{" +
            "\"notebookId\":\"" + notebookId + "\"," +
            "\"channelId\":\"" + channel.ChannelId + "\"," +
            "\"messageType\":\"ping\"," +
            "\"payload\":\"{\\\"at\\\":2}\"," +
            "\"buffers\":null" +
            "}";

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetBytes(wire));
        await OutputChannelHandler.HandleMessageAsync(ns, doc.RootElement);

        Assert.IsNotNull(received);
        Assert.AreEqual("ping", received!.Type);
        Assert.AreEqual("{\"at\":2}", received.Payload);
        Assert.IsNull(received.Buffers);
    }
}
