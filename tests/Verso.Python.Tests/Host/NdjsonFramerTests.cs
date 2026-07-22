using System.Text;
using System.Text.Json.Nodes;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

[TestClass]
public sealed class NdjsonFramerTests
{
    [TestMethod]
    public async Task Frame_RoundTrips_SingleMessage()
    {
        var stream = new MemoryStream();
        var writer = new NdjsonFramer(stream);
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"hello\"}");

        await writer.WriteFrameAsync(payload, CancellationToken.None);

        stream.Position = 0;
        var reader = new NdjsonFramer(stream);
        var frame = await reader.ReadFrameAsync(CancellationToken.None);

        Assert.IsNotNull(frame);
        CollectionAssert.AreEqual(payload, frame);
    }

    [TestMethod]
    public async Task Frame_RoundTrips_MultipleMessages()
    {
        var stream = new MemoryStream();
        var writer = new NdjsonFramer(stream);
        var one = Encoding.UTF8.GetBytes("{\"n\":1}");
        var two = Encoding.UTF8.GetBytes("{\"n\":2}");
        var three = Encoding.UTF8.GetBytes("{\"n\":3}");

        await writer.WriteFrameAsync(one, CancellationToken.None);
        await writer.WriteFrameAsync(two, CancellationToken.None);
        await writer.WriteFrameAsync(three, CancellationToken.None);

        stream.Position = 0;
        var reader = new NdjsonFramer(stream);
        CollectionAssert.AreEqual(one, await reader.ReadFrameAsync(CancellationToken.None));
        CollectionAssert.AreEqual(two, await reader.ReadFrameAsync(CancellationToken.None));
        CollectionAssert.AreEqual(three, await reader.ReadFrameAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Read_ReturnsNull_AtEndOfStream()
    {
        var stream = new MemoryStream();
        var reader = new NdjsonFramer(stream);
        Assert.IsNull(await reader.ReadFrameAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Read_Throws_WhenStreamEndsMidFrame()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"partial\":true}")); // no trailing newline
        var reader = new NdjsonFramer(stream);
        await Assert.ThrowsExceptionAsync<PythonHostProtocolException>(
            async () => await reader.ReadFrameAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Write_Throws_WhenPayloadExceedsCeiling()
    {
        var stream = new MemoryStream();
        var framer = new NdjsonFramer(stream, maxFrameBytes: 8);
        var payload = Encoding.UTF8.GetBytes("123456789"); // 9 bytes exceeds an 8-byte ceiling

        await Assert.ThrowsExceptionAsync<PythonHostProtocolException>(
            async () => await framer.WriteFrameAsync(payload, CancellationToken.None));
    }

    [TestMethod]
    public async Task Read_Throws_WhenFrameExceedsCeiling()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("123456789\n")); // 9-byte line exceeds ceiling
        var framer = new NdjsonFramer(stream, maxFrameBytes: 8);

        await Assert.ThrowsExceptionAsync<PythonHostProtocolException>(
            async () => await framer.ReadFrameAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Escaping_PreservesNewlinesQuotesAndUnicode()
    {
        var original = "line1\nline2\t\"quoted\" \\ backslash café 🚀 mañana naïve";
        var message = new JsonObject { ["text"] = original };
        var payload = HostProtocol.Serialize(message);

        // The framed line must contain no raw newline other than the terminator the framer adds.
        Assert.IsTrue(Array.IndexOf(payload, (byte)'\n') < 0, "serialized payload must not contain a raw newline");

        var stream = new MemoryStream();
        var writer = new NdjsonFramer(stream);
        await writer.WriteFrameAsync(payload, CancellationToken.None);

        stream.Position = 0;
        var reader = new NdjsonFramer(stream);
        var frame = await reader.ReadFrameAsync(CancellationToken.None);
        Assert.IsNotNull(frame);

        var parsed = HostProtocol.Deserialize(frame);
        Assert.AreEqual(original, HostProtocol.TryGetString(parsed, "text"));
    }
}
