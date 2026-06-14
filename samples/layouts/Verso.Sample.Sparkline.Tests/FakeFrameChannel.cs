using Verso.Abstractions;

namespace Verso.Sample.Sparkline.Tests;

/// <summary>
/// Test double for <see cref="ILayoutFrameChannel"/> that records every pushed message.
/// In the running host the channel posts to the live iframe and prefixes the type with
/// "ext/"; this fake captures the raw type the layout passed so tests can assert on it.
/// </summary>
internal sealed class FakeFrameChannel : ILayoutFrameChannel
{
    private readonly List<(string Type, object? Payload)> _messages = new();

    public FakeFrameChannel(string frameInstanceId) => FrameInstanceId = frameInstanceId;

    public string FrameInstanceId { get; }

    public bool IsAlive { get; set; } = true;

    public IReadOnlyList<(string Type, object? Payload)> Messages => _messages;

    public Task PostMessageAsync(string type, object? payload, CancellationToken ct = default)
    {
        _messages.Add((type, payload));
        return Task.CompletedTask;
    }
}
