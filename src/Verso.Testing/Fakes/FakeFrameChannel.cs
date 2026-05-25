using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Test double for <see cref="ILayoutFrameChannel"/>. Records every message
/// posted with its <c>ext/</c>-prefixed wire type so assertions can observe
/// the order, type, and payload of pushes. Implements the same reserved-namespace
/// guard the production host base enforces.
/// </summary>
public sealed class FakeFrameChannel : ILayoutFrameChannel
{
    private readonly List<RecordedFrameMessage> _messages = new();

    public FakeFrameChannel(string frameInstanceId = "fake-frame")
    {
        FrameInstanceId = frameInstanceId;
    }

    public string FrameInstanceId { get; }
    public bool IsAlive { get; private set; } = true;

    /// <summary>
    /// Ordered list of messages observed by the channel. Types are stored with
    /// the <c>ext/</c> prefix already applied, matching the wire form a real
    /// host channel would produce.
    /// </summary>
    public IReadOnlyList<RecordedFrameMessage> Messages => _messages;

    public Task PostMessageAsync(string type, object? payload, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(type))
            throw new ArgumentException("Message type must be a non-empty string.", nameof(type));

        if (type.StartsWith("verso/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Message type '{type}' is in the reserved 'verso/' namespace.",
                nameof(type));
        }

        if (!IsAlive)
            throw new InvalidOperationException(
                $"Cannot post message of type '{type}': frame '{FrameInstanceId}' is no longer alive.");

        _messages.Add(new RecordedFrameMessage($"ext/{type}", payload));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test hook that flips <see cref="IsAlive"/> to <c>false</c>, mirroring
    /// the production host base's <c>MarkDead</c> behavior.
    /// </summary>
    public void MarkDead() => IsAlive = false;
}

/// <summary>
/// One message captured by <see cref="FakeFrameChannel"/>.
/// </summary>
public sealed record RecordedFrameMessage(string Type, object? Payload);
