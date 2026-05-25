using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Layouts;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Layouts;

[TestClass]
public class IframeFrameChannelTests
{
    private async Task<(HostSession Session, string NotebookId, List<string> Notifications)> CreateOpenSessionAsync()
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
    public async Task PostMessageAsync_EmitsLayoutFrameMessageNotificationWithExtPrefix()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        var frameInstanceId = $"{notebookId}/sparkline/1";
        var channel = new IframeFrameChannel(frameInstanceId, ns);

        await channel.PostMessageAsync("data-frame", new { value = 42 });

        Assert.AreEqual(1, notifications.Count, "Exactly one notification should have been emitted.");

        using var doc = JsonDocument.Parse(notifications[0]);
        var root = doc.RootElement;
        Assert.AreEqual("layout/frameMessage", root.GetProperty("method").GetString());

        var p = root.GetProperty("params");
        Assert.AreEqual(notebookId, p.GetProperty("notebookId").GetString());
        Assert.AreEqual(frameInstanceId, p.GetProperty("frameInstanceId").GetString());
        Assert.AreEqual("ext/data-frame", p.GetProperty("type").GetString());
        Assert.AreEqual(42, p.GetProperty("payload").GetProperty("value").GetInt32());
    }

    [TestMethod]
    public async Task PostMessageAsync_VersoPrefix_ThrowsAndDoesNotEmit()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        notifications.Clear();

        var channel = new IframeFrameChannel($"{notebookId}/sparkline/1", ns);

        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => channel.PostMessageAsync("verso/init", new { }));

        Assert.AreEqual(0, notifications.Count, "Reserved-namespace messages must not emit a notification.");
    }

    [TestMethod]
    public async Task PostMessageAsync_AfterMarkDead_ThrowsInvalidOperation()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var channel = new IframeFrameChannel($"{notebookId}/sparkline/1", ns);
        channel.MarkDead();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => channel.PostMessageAsync("data-frame", null));
    }
}
