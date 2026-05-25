using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Layouts;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Layouts;

[TestClass]
public class LayoutFrameChannelTests
{
    [TestMethod]
    public async Task PostMessageAsync_VersoPrefix_ThrowsArgumentException()
    {
        var channel = new TestChannel("frame-1");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => channel.PostMessageAsync("verso/interact", new { x = 1 }));

        StringAssert.Contains(ex.Message, "reserved");
        Assert.AreEqual(0, channel.Posted.Count, "Reserved-namespace messages must not reach the transport.");
    }

    [TestMethod]
    public async Task PostMessageAsync_EmptyType_ThrowsArgumentException()
    {
        var channel = new TestChannel("frame-1");
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => channel.PostMessageAsync("", null));
    }

    [TestMethod]
    public async Task PostMessageAsync_ValidType_ForwardsToCore()
    {
        var channel = new TestChannel("frame-1");

        await channel.PostMessageAsync("data-frame", new { value = 42 });

        Assert.AreEqual(1, channel.Posted.Count);
        Assert.AreEqual("data-frame", channel.Posted[0].Type);
    }

    [TestMethod]
    public async Task PostMessageAsync_AfterMarkDead_ThrowsInvalidOperation()
    {
        var channel = new TestChannel("frame-1");
        channel.MarkDeadForTest();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => channel.PostMessageAsync("data-frame", null));
    }

    [TestMethod]
    public async Task FakeFrameChannel_RejectsVersoPrefix()
    {
        var fake = new FakeFrameChannel("frame-2");

        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => fake.PostMessageAsync("verso/dispose", null));

        Assert.AreEqual(0, fake.Messages.Count);
    }

    [TestMethod]
    public async Task FakeFrameChannel_RecordsOrderedMessagesWithExtPrefix()
    {
        var fake = new FakeFrameChannel("frame-2");

        await fake.PostMessageAsync("data-frame", new { v = 1 });
        await fake.PostMessageAsync("select-point", "p");

        Assert.AreEqual(2, fake.Messages.Count);
        Assert.AreEqual("ext/data-frame", fake.Messages[0].Type);
        Assert.AreEqual("ext/select-point", fake.Messages[1].Type);
    }

    [TestMethod]
    public async Task FakeFrameChannel_PostAfterMarkDead_Throws()
    {
        var fake = new FakeFrameChannel("frame-2");
        fake.MarkDead();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => fake.PostMessageAsync("data-frame", null));
    }

    private sealed class TestChannel : LayoutFrameChannel
    {
        public TestChannel(string id) : base(id) { }

        public List<(string Type, object? Payload)> Posted { get; } = new();

        protected override Task PostMessageCoreAsync(string type, object? payload, CancellationToken ct)
        {
            Posted.Add((type, payload));
            return Task.CompletedTask;
        }

        public void MarkDeadForTest() => MarkDead();
    }
}
