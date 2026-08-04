using System.Collections.Concurrent;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
using Verso.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Execution;

/// <summary>
/// Covers the session's output channels with a stand-in for the browser. Nothing here draws
/// anything: a recording transport plays the part of a view, so what is under test is the half
/// that has to be right before a frame is worth wiring, which is the ordering, the lifetime, and
/// what happens when a caller talks to a view that is not listening.
/// </summary>
[TestClass]
public sealed class OutputChannelHostTests
{
    /// <summary>A view that records rather than renders.</summary>
    private sealed class RecordingTransport : IOutputChannelTransport
    {
        public ConcurrentQueue<(string ChannelId, string Type, object? Payload)> Posts { get; } = new();

        public ConcurrentQueue<(string ChannelId, string Reason)> Closes { get; } = new();

        public Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct)
        {
            Posts.Enqueue((channelId, messageType, payload));
            return Task.CompletedTask;
        }

        public Task CloseAsync(string channelId, string reason, CancellationToken ct)
        {
            Closes.Enqueue((channelId, reason));
            return Task.CompletedTask;
        }
    }

    private static (OutputChannelHost Host, RecordingTransport Transport) NewHost()
    {
        var transport = new RecordingTransport();
        var host = new OutputChannelHost { Transport = transport };
        return (host, transport);
    }

    /// <summary>
    /// Runs <paramref name="action"/> with standard error captured, so a test can assert on the
    /// diagnostic a failure writes rather than only on the state it leaves behind.
    /// </summary>
    private static async Task<string> CaptureStandardErrorAsync(Func<Task> action)
    {
        var original = Console.Error;
        var captured = new StringWriter();

        try
        {
            Console.SetError(captured);
            await action().ConfigureAwait(false);
        }
        finally
        {
            Console.SetError(original);
        }

        return captured.ToString();
    }

    // --- Delivery ---

    [TestMethod]
    public async Task AMessagePostedAfterTheViewIsReadyGoesStraightToIt()
    {
        var (host, transport) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        await host.HandleReadyAsync(channel.ChannelId);
        await channel.PostMessageAsync("update", new { value = 7 });

        Assert.AreEqual(1, transport.Posts.Count);
        Assert.IsTrue(transport.Posts.TryDequeue(out var post));
        Assert.AreEqual(channel.ChannelId, post.ChannelId);
        Assert.AreEqual("ext/update", post.Type, "The host prefixes an owner's type on the way out.");
    }

    [TestMethod]
    public async Task MessagesPostedBeforeTheViewIsReadyArriveInOrderOnceItIs()
    {
        var (host, transport) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        // The case this exists for. A widget's first message is produced while the client is still
        // drawing the output that carries the channel, every time.
        for (var i = 0; i < 5; i++)
            await channel.PostMessageAsync("step", i);

        Assert.AreEqual(0, transport.Posts.Count, "Nothing should reach a view that has not mounted.");

        await host.HandleReadyAsync(channel.ChannelId);

        var delivered = transport.Posts.Select(p => p.Payload).ToList();
        CollectionAssert.AreEqual(new object[] { 0, 1, 2, 3, 4 }, delivered);
    }

    [TestMethod]
    public async Task AMessageFromTheViewReachesTheChannelOwner()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        var received = new List<OutputChannelMessage>();
        channel.MessageReceived += message =>
        {
            received.Add(message);
            return Task.CompletedTask;
        };

        await host.HandleMessageAsync(channel.ChannelId, "ext/update", "{\"value\":7}");

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(channel.ChannelId, received[0].ChannelId);
        Assert.AreEqual("update", received[0].Type, "The transport's prefix comes off on the way back.");
        Assert.AreEqual("{\"value\":7}", received[0].Payload);
    }

    [TestMethod]
    public async Task AReservedTypeFromTheViewIsNotDelivered()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        var received = 0;
        channel.MessageReceived += _ =>
        {
            received++;
            return Task.CompletedTask;
        };

        var diagnostic = await CaptureStandardErrorAsync(
            () => host.HandleMessageAsync(channel.ChannelId, "verso/channel-ready", null));

        Assert.AreEqual(0, received, "A framework message belongs to the transport, never to an owner.");
        StringAssert.Contains(diagnostic, "reserved");
    }

    [TestMethod]
    public async Task AMessageForAChannelThatIsNotOpenIsDropped()
    {
        var (host, _) = NewHost();

        // No exception: the ordinary way to get one of these is a view answering a moment after
        // its channel closed, which is a race rather than a fault.
        await host.HandleMessageAsync("no-such-channel", "ext/update", "{}");
        await host.HandleReadyAsync("no-such-channel");
    }

    [TestMethod]
    public async Task AHandlerThatThrowsDoesNotStopTheOnesAfterIt()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        var reachedSecond = false;
        channel.MessageReceived += _ => throw new InvalidOperationException("handler fault");
        channel.MessageReceived += _ =>
        {
            reachedSecond = true;
            return Task.CompletedTask;
        };

        var diagnostic = await CaptureStandardErrorAsync(
            () => host.HandleMessageAsync(channel.ChannelId, "ext/update", null));

        Assert.IsTrue(reachedSecond);
        StringAssert.Contains(diagnostic, "handler fault");
    }

    // --- Message types ---

    [TestMethod]
    public async Task PostingAReservedTypeThrows()
    {
        var (host, transport) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        var ex = Assert.ThrowsException<ArgumentException>(
            () => channel.PostMessageAsync("verso/channel-post", null));

        StringAssert.Contains(ex.Message, "reserved");
        Assert.AreEqual(0, transport.Posts.Count);
    }

    [TestMethod]
    public async Task PostingAnEmptyTypeThrows()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        Assert.ThrowsException<ArgumentException>(() => channel.PostMessageAsync("", null));
    }

    // --- Lifetime ---

    [TestMethod]
    public async Task ClosingTellsTheViewAndTakesTheChannelOutOfTheHost()
    {
        var (host, transport) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        await host.CloseAsync(channel.ChannelId, "cell re-executed");

        Assert.IsFalse(channel.IsAlive);
        Assert.IsNull(host.Find(channel.ChannelId));
        Assert.IsTrue(transport.Closes.TryDequeue(out var closed));
        Assert.AreEqual("cell re-executed", closed.Reason);
    }

    [TestMethod]
    public async Task PostingToAClosedChannelThrows()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        await host.CloseAsync(channel.ChannelId, "cell deleted");

        // Thrown rather than dropped, which is the whole reason IsAlive is on the interface: a
        // caller posting from a handler that outlived the view is told so.
        Assert.ThrowsException<InvalidOperationException>(() => channel.PostMessageAsync("update", null));
    }

    [TestMethod]
    public async Task DisposingAChannelClosesIt()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        await channel.DisposeAsync();

        Assert.IsFalse(channel.IsAlive);
        Assert.IsNull(host.Find(channel.ChannelId));
    }

    [TestMethod]
    public async Task DisposingTheHostClosesEveryChannelWithoutTellingTheViews()
    {
        var (host, transport) = NewHost();
        var first = await host.OpenAsync(Guid.NewGuid());
        var second = await host.OpenAsync(Guid.NewGuid());

        await host.DisposeAsync();

        Assert.IsFalse(first.IsAlive);
        Assert.IsFalse(second.IsAlive);

        // The session ending takes the surface holding the views with it, so there is nobody left
        // to tell and a transport reaching for a torn-down page would only raise on the way out.
        Assert.AreEqual(0, transport.Closes.Count);
    }

    [TestMethod]
    public async Task ForCellReturnsOnlyThatCellsChannels()
    {
        var (host, _) = NewHost();
        var cell = Guid.NewGuid();
        var mine = await host.OpenAsync(cell);
        await host.OpenAsync(Guid.NewGuid());

        var forCell = host.ForCell(cell);

        Assert.AreEqual(1, forCell.Count);
        Assert.AreEqual(mine.ChannelId, forCell[0].ChannelId);
    }

    [TestMethod]
    public async Task ClosingACellsChannelsLeavesOtherCellsAlone()
    {
        var (host, _) = NewHost();
        var cell = Guid.NewGuid();
        var mine = await host.OpenAsync(cell);
        var other = await host.OpenAsync(Guid.NewGuid());

        await host.CloseForCellAsync(cell, "output cleared");

        Assert.IsFalse(mine.IsAlive);
        Assert.IsTrue(other.IsAlive);
    }

    // --- Bounds ---

    [TestMethod]
    public async Task TooManyQueuedMessagesClosesTheChannelWithADiagnostic()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        var diagnostic = await CaptureStandardErrorAsync(async () =>
        {
            for (var i = 0; i < OutputChannelHost.MaxQueuedMessages; i++)
                await channel.PostMessageAsync("step", i);

            Assert.IsTrue(channel.IsAlive, "The bound is the number that fits, not the number that fails.");

            Assert.ThrowsException<InvalidOperationException>(() => channel.PostMessageAsync("step", "one too many"));
        });

        Assert.IsFalse(channel.IsAlive);
        Assert.IsNull(host.Find(channel.ChannelId));
        StringAssert.Contains(diagnostic, "never became ready");
    }

    [TestMethod]
    public async Task TooManyQueuedBytesClosesTheChannelWithADiagnostic()
    {
        var (host, _) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        // One message past the byte bound on its own, so this is the byte bound failing and not
        // the message count reaching it by another route.
        var oversize = new string('x', (int)(OutputChannelHost.MaxQueuedBytes + 1024));

        var diagnostic = await CaptureStandardErrorAsync(() =>
        {
            Assert.ThrowsException<InvalidOperationException>(() => channel.PostMessageAsync("state", oversize));
            return Task.CompletedTask;
        });

        Assert.IsFalse(channel.IsAlive);
        StringAssert.Contains(diagnostic, "never became ready");
    }

    [TestMethod]
    public async Task AQueueThatDrainsDoesNotCountAgainstTheBoundTwice()
    {
        var (host, transport) = NewHost();
        var channel = await host.OpenAsync(Guid.NewGuid());

        for (var i = 0; i < OutputChannelHost.MaxQueuedMessages; i++)
            await channel.PostMessageAsync("step", i);

        await host.HandleReadyAsync(channel.ChannelId);

        // Everything queued has gone, so a mounted channel is not carrying a full queue's worth of
        // credit it can never spend.
        await channel.PostMessageAsync("step", "after");

        Assert.IsTrue(channel.IsAlive);
        Assert.AreEqual(OutputChannelHost.MaxQueuedMessages + 1, transport.Posts.Count);
    }

    // --- The capability signal ---

    [TestMethod]
    public void AHostWithNoTransportCannotReachViews()
    {
        var host = new OutputChannelHost();

        Assert.IsFalse(host.CanReachViews);

        host.Transport = new RecordingTransport();

        Assert.IsTrue(host.CanReachViews);
    }

    [TestMethod]
    public async Task AKernelSeesNoChannelsUntilAHostAttachesATransport()
    {
        var channels = new OutputChannelHost();
        IOutputChannelHost? seen = null;
        var kernel = new FakeLanguageKernel("csharp", executeFunc: (_, context) =>
        {
            seen = context.OutputChannels;
            return Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());
        });

        var cell = new CellModel { Language = "csharp", Source = "x" };
        var pipeline = BuildPipeline(kernel, cell, channels);

        await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.IsNull(seen, "A kernel that finds no channels writes static output instead of failing.");

        channels.Transport = new RecordingTransport();
        await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreSame(channels, seen, "The transport is attached when a host has a surface for it, which can be after the first cell.");
    }

    private static ExecutionPipeline BuildPipeline(
        FakeLanguageKernel kernel, CellModel cell, OutputChannelHost channels)
    {
        return new ExecutionPipeline(
            new VariableStore(),
            new StubThemeContext(),
            LayoutCapabilities.None,
            new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>()),
            new NotebookMetadataContext(new NotebookModel()),
            new StubNotebookOperations(),
            resolveKernel: languageId => kernel.LanguageId.Equals(languageId, StringComparison.OrdinalIgnoreCase) ? kernel : null,
            ensureInitialized: k => k.InitializeAsync(),
            resolveLanguageId: _ => cell.Language,
            getExecutionCount: _ => 1,
            outputChannels: channels);
    }
}
