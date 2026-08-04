using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Channels;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Channels;

/// <summary>
/// Covers the outbound leg: an owner's message becoming a notification the client can hand to the
/// frame drawing its channel.
/// </summary>
[TestClass]
public class SessionOutputChannelTransportTests
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

    [TestMethod]
    public async Task PostingKeepsTheOwnersNamespaceOnTheWire()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        var transport = new SessionOutputChannelTransport(ns);
        await transport.PostAsync("chan-1", "ext/counter", new { value = 7 }, CancellationToken.None);

        Assert.AreEqual(1, notifications.Count);

        using var doc = JsonDocument.Parse(notifications[0]);
        var root = doc.RootElement;
        Assert.AreEqual(MethodNames.ChannelPost, root.GetProperty("method").GetString());

        var p = root.GetProperty("params");
        Assert.AreEqual(notebookId, p.GetProperty("notebookId").GetString());
        Assert.AreEqual("chan-1", p.GetProperty("channelId").GetString());
        Assert.AreEqual(
            "ext/counter",
            p.GetProperty("messageType").GetString(),
            "The prefix comes off in the interop, which is the hop both hosts share.");
        Assert.AreEqual(7, p.GetProperty("payload").GetProperty("value").GetInt32());
    }

    [TestMethod]
    public async Task PostingWithoutAPayloadStillCarriesTheType()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        var transport = new SessionOutputChannelTransport(ns);
        await transport.PostAsync("chan-1", "ext/tick", null, CancellationToken.None);

        using var doc = JsonDocument.Parse(notifications[0]);
        var p = doc.RootElement.GetProperty("params");
        Assert.AreEqual("ext/tick", p.GetProperty("messageType").GetString());

        // A null payload is left off the wire rather than written as null, which is what these
        // notifications do everywhere. The client reads an absent payload as no payload, so both
        // spellings mean the same thing by the time a view sees one.
        Assert.IsFalse(p.TryGetProperty("payload", out _));
    }

    [TestMethod]
    public async Task ClosingAChannelTellsTheClientWhy()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        var transport = new SessionOutputChannelTransport(ns);
        await transport.CloseAsync("chan-1", "the cell was re-run", CancellationToken.None);

        using var doc = JsonDocument.Parse(notifications[0]);
        var root = doc.RootElement;
        Assert.AreEqual(MethodNames.ChannelClosed, root.GetProperty("method").GetString());

        var p = root.GetProperty("params");
        Assert.AreEqual("chan-1", p.GetProperty("channelId").GetString());
        Assert.AreEqual("the cell was re-run", p.GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task AnOwnerDisposingItsChannelReachesTheView()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = await ns.Scaffold.OutputChannels.OpenAsync(Guid.NewGuid());
        notifications.Clear();

        await channel.DisposeAsync();

        Assert.AreEqual(1, notifications.Count);
        using var doc = JsonDocument.Parse(notifications[0]);
        Assert.AreEqual(MethodNames.ChannelClosed, doc.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(
            channel.ChannelId,
            doc.RootElement.GetProperty("params").GetProperty("channelId").GetString());
    }
}
