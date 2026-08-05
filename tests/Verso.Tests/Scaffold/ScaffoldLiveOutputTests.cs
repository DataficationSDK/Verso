using System.Collections.Concurrent;
using Verso.Abstractions;
using Verso.Execution;
using Verso.Testing.Fakes;

namespace Verso.Tests.Scaffold;

/// <summary>
/// Covers what happens to a live output when the thing behind it goes away, and what a save
/// writes while one is still live. A recording transport plays the part of a view, so what is
/// under test is the notebook's own bookkeeping rather than anything drawn.
/// </summary>
[TestClass]
public sealed class ScaffoldLiveOutputTests
{
    /// <summary>A view that records rather than renders.</summary>
    private sealed class RecordingTransport : IOutputChannelTransport
    {
        public ConcurrentQueue<(string ChannelId, string Reason)> Closes { get; } = new();

        public Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct)
            => Task.CompletedTask;

        public Task CloseAsync(string channelId, string reason, CancellationToken ct)
        {
            Closes.Enqueue((channelId, reason));
            return Task.CompletedTask;
        }
    }

    private Verso.Scaffold _scaffold = null!;
    private RecordingTransport _transport = null!;

    [TestInitialize]
    public void Setup()
    {
        _scaffold = new Verso.Scaffold();
        _transport = new RecordingTransport();
        _scaffold.OutputChannels.Transport = _transport;
    }

    /// <summary>
    /// A cell holding one live output, in the state a cell that drew a widget would be in: the
    /// output carries the document and the channel, and the channel is open.
    /// </summary>
    private async Task<(CellModel Cell, IOutputChannel Channel)> LiveCellAsync(
        string language = "python", string content = "<html>as it was</html>")
    {
        var cell = _scaffold.AddCell(language: language);
        var channel = await _scaffold.OutputChannels.OpenAsync(cell.Id);
        cell.Outputs.Add(CellOutput.Widget(content, channel.ChannelId));
        return (cell, channel);
    }

    // --- The channel goes when the output does ---

    [TestMethod]
    public async Task DeletingACellClosesItsLiveOutputs()
    {
        var (cell, channel) = await LiveCellAsync();

        _scaffold.RemoveCell(cell.Id);

        Assert.IsFalse(channel.IsAlive);
        Assert.AreEqual(0, _transport.Closes.Count, "The view went with the cell, so there is nobody to tell.");
    }

    [TestMethod]
    public async Task ClearingEveryOutputClosesEveryLiveOne()
    {
        var (_, first) = await LiveCellAsync();
        var (_, second) = await LiveCellAsync();

        _scaffold.ClearAllOutputs();

        Assert.IsFalse(first.IsAlive);
        Assert.IsFalse(second.IsAlive);
    }

    [TestMethod]
    public async Task RunningACellAgainClosesWhatTheLastRunLeftLive()
    {
        _scaffold.DefaultKernelId = "fake";
        _scaffold.RegisterKernel(new FakeLanguageKernel("fake"));

        var cell = _scaffold.AddCell(language: "fake", source: "draw something");
        var channel = await _scaffold.OutputChannels.OpenAsync(cell.Id);
        cell.Outputs.Add(CellOutput.Widget("<html>the first run</html>", channel.ChannelId));

        await _scaffold.ExecuteCellAsync(cell.Id);

        // Without this a cell accumulates a channel per execution and the first stays open for
        // the life of the session, answering a view that was replaced long ago.
        Assert.IsFalse(channel.IsAlive);
        Assert.AreEqual(0, _scaffold.OutputChannels.ForCell(cell.Id).Count);
    }

    // --- The channel goes when the kernel does ---

    [TestMethod]
    public async Task RestartingAKernelClosesItsCellsChannelsAndTellsTheViews()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("python"));
        var (_, channel) = await LiveCellAsync("python");

        await _scaffold.RestartKernelAsync("python");

        Assert.IsFalse(channel.IsAlive);

        // Told, unlike a clear or a delete: the view is still on the page, and a control that
        // goes on answering its own drags is exactly what the treatment exists to prevent.
        Assert.AreEqual(1, _transport.Closes.Count);
        Assert.IsTrue(_transport.Closes.TryDequeue(out var closed));
        Assert.AreEqual(channel.ChannelId, closed.ChannelId);
    }

    [TestMethod]
    public async Task RestartingOneKernelLeavesAnothersOutputsAlone()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("python"));
        _scaffold.RegisterKernel(new FakeLanguageKernel("csharp"));

        var (_, python) = await LiveCellAsync("python");
        var (_, csharp) = await LiveCellAsync("csharp");

        await _scaffold.RestartKernelAsync("python");

        Assert.IsFalse(python.IsAlive);
        Assert.IsTrue(csharp.IsAlive, "A kernel restarting says nothing about an output another kernel keeps alive.");
    }

    [TestMethod]
    public async Task ASupervisedRestartClosesTheChannelsBeforeTheProcessGoes()
    {
        var closedBeforeTheHandlerRan = false;
        var (_, channel) = await LiveCellAsync("python");

        _scaffold.HostRestartHandler = _ =>
        {
            closedBeforeTheHandlerRan = !channel.IsAlive;
            return Task.CompletedTask;
        };

        await _scaffold.RestartKernelAsync("python");

        Assert.IsTrue(
            closedBeforeTheHandlerRan,
            "The surface holding the views outlives a respawn, so they are told before the process goes.");
    }

    // --- What a save writes ---

    [TestMethod]
    public async Task SavingAsksALiveOutputWhatItIsShowingNow()
    {
        var (cell, channel) = await LiveCellAsync();
        channel.SnapshotProvider = _ => Task.FromResult<string?>("<html>as it is now</html>");

        await _scaffold.RefreshLiveOutputsAsync();

        Assert.AreEqual("<html>as it is now</html>", cell.Outputs[0].Content);
        Assert.AreEqual(channel.ChannelId, cell.Outputs[0].LiveChannelId, "The output is still live.");
    }

    [TestMethod]
    public async Task ALiveOutputThatDoesNotAnswerInTimeKeepsWhatItHad()
    {
        var (cell, channel) = await LiveCellAsync();
        channel.SnapshotProvider = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "never";
        };

        // The case this bound exists for. A kernel part way through a long cell answers nothing,
        // and a save is never the thing that waits for it.
        await _scaffold.RefreshLiveOutputsAsync(TimeSpan.FromMilliseconds(50));

        Assert.AreEqual("<html>as it was</html>", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task ALiveOutputWithNothingBehindItKeepsWhatItHad()
    {
        var (cell, _) = await LiveCellAsync();

        // What a reopened notebook looks like, and what a crashed kernel leaves behind: an output
        // naming a channel that is no longer open.
        await _scaffold.OutputChannels.CloseAllAsync("the kernel was restarted");

        await _scaffold.RefreshLiveOutputsAsync();

        Assert.AreEqual("<html>as it was</html>", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task AnOrdinaryOutputIsNotAskedAnything()
    {
        var cell = _scaffold.AddCell(language: "python");
        cell.Outputs.Add(CellOutput.Plain("42"));

        await _scaffold.RefreshLiveOutputsAsync();

        Assert.AreEqual("42", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task EveryLiveOutputIsAskedAtOnceRatherThanInTurn()
    {
        var (first, firstChannel) = await LiveCellAsync();
        var (second, secondChannel) = await LiveCellAsync();

        // Each waits for the other to have been asked, so only a pass that asks them together
        // finishes at all.
        var bothAsked = new TaskCompletionSource();
        var asked = 0;

        Func<CancellationToken, Task<string?>> answerWhenBothAreAsked = async _ =>
        {
            if (Interlocked.Increment(ref asked) == 2)
                bothAsked.SetResult();

            await bothAsked.Task.ConfigureAwait(false);
            return "<html>together</html>";
        };

        firstChannel.SnapshotProvider = answerWhenBothAreAsked;
        secondChannel.SnapshotProvider = answerWhenBothAreAsked;

        await _scaffold.RefreshLiveOutputsAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual("<html>together</html>", first.Outputs[0].Content);
        Assert.AreEqual("<html>together</html>", second.Outputs[0].Content);
    }
}
