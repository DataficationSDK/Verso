using Microsoft.JSInterop;
using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Covers the served host's half of an output channel: the leg that carries a channel owner's
/// message into the page, and the leg that brings a view's answer back. The browser is stood in for
/// by a recording interop, so what is under test is the routing rather than anything drawn.
/// </summary>
[TestClass]
public sealed class OutputChannelRoutingTests
{
    [TestMethod]
    public async Task OpeningANotebookGivesItsChannelsSomewhereToSend()
    {
        await using var service = await CreateServiceAsync();

        // Attaching the transport is the capability signal. A kernel asking a context for channels
        // gets them because this host has a page to reach, and would get null without one.
        Assert.IsTrue(service.Scaffold!.OutputChannels.CanReachViews);
    }

    [TestMethod]
    public async Task AMessageToAViewBecomesACallIntoThePage()
    {
        var js = new RecordingJSRuntime();
        await using var service = await CreateServiceAsync(js);

        var channel = await service.Scaffold!.OutputChannels.OpenAsync(Guid.NewGuid());
        await service.OutputChannelReadyAsync(channel.ChannelId, "1.0");
        await channel.PostMessageAsync("counter", new { value = 7 });

        var call = js.Calls.Single(c => c.Identifier == "versoOutputChannel.post");
        Assert.AreEqual(channel.ChannelId, call.Args[0]);

        // The prefix is applied once, by the channel host, so every transport carries the same
        // spelling and none of them has to remember to add it.
        Assert.AreEqual("ext/counter", call.Args[1]);
    }

    [TestMethod]
    public async Task ClosingAChannelTellsThePageWhy()
    {
        var js = new RecordingJSRuntime();
        await using var service = await CreateServiceAsync(js);

        var channel = await service.Scaffold!.OutputChannels.OpenAsync(Guid.NewGuid());
        await service.Scaffold.OutputChannels.CloseAsync(channel.ChannelId, "cell re-executed");

        var call = js.Calls.Single(c => c.Identifier == "versoOutputChannel.closed");
        Assert.AreEqual(channel.ChannelId, call.Args[0]);
        Assert.AreEqual("cell re-executed", call.Args[1]);
    }

    [TestMethod]
    public async Task AMessageFromAViewReachesTheChannelOwner()
    {
        await using var service = await CreateServiceAsync();

        var channel = await service.Scaffold!.OutputChannels.OpenAsync(Guid.NewGuid());
        var received = new TaskCompletionSource<OutputChannelMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        channel.MessageReceived += message =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        };

        await service.OutputChannelMessageAsync(
            channel.ChannelId, "ext/slider", "{\"value\":3}", null);

        var message = await received.Task;

        // The owner is handed the type it posted under and the payload as text, because it is the
        // only part of this that knows what shape it expects back.
        Assert.AreEqual("slider", message.Type);
        Assert.AreEqual("{\"value\":3}", message.Payload);
    }

    [TestMethod]
    public async Task BuffersArriveAsBytes()
    {
        await using var service = await CreateServiceAsync();

        var channel = await service.Scaffold!.OutputChannels.OpenAsync(Guid.NewGuid());
        var received = new TaskCompletionSource<OutputChannelMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        channel.MessageReceived += message =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        };

        await service.OutputChannelMessageAsync(
            channel.ChannelId,
            "ext/frame",
            null,
            new[] { Convert.ToBase64String(new byte[] { 1, 2, 3 }), "not base64 at all!" });

        var message = await received.Task;

        // One buffer that would not decode is dropped and the message still arrives. The rest of it
        // is worth delivering, and an owner reading a buffer it did not get fails where it can say
        // what it was reading.
        Assert.IsNotNull(message.Buffers);
        Assert.AreEqual(1, message.Buffers!.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, message.Buffers[0]);
    }

    [TestMethod]
    public async Task TalkingToAChannelThatIsNotOpenDoesNothing()
    {
        await using var service = await CreateServiceAsync();

        // What a view answering a moment after its cell was re-run comes to. Neither call has
        // anywhere to go and neither is an error.
        await service.OutputChannelReadyAsync("no such channel", "1.0");
        await service.OutputChannelMessageAsync("no such channel", "ext/anything", "{}", null);
    }

    private static async Task<ServerNotebookService> CreateServiceAsync(IJSRuntime? js = null)
    {
        var service = new ServerNotebookService(js ?? new RecordingJSRuntime(), new LayoutAssetCache());
        await service.NewNotebookAsync();
        return service;
    }

    private sealed record RecordedCall(string Identifier, object?[] Args);

    /// <summary>
    /// Stands in for the page. Every call is kept rather than answered, because nothing on this
    /// side of the boundary reads a return value from the interop.
    /// </summary>
    private sealed class RecordingJSRuntime : IJSRuntime
    {
        public List<RecordedCall> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => Record<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
            => Record<TValue>(identifier, args);

        private ValueTask<TValue> Record<TValue>(string identifier, object?[]? args)
        {
            lock (Calls)
                Calls.Add(new RecordedCall(identifier, args ?? Array.Empty<object?>()));

            return new ValueTask<TValue>(default(TValue)!);
        }
    }
}
