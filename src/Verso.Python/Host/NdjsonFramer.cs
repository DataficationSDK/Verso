using System.Buffers;

namespace Verso.Python.Host;

/// <summary>
/// Frames messages over a byte stream as UTF-8, newline-delimited JSON: one object per line.
/// Both directions enforce a fixed frame ceiling; a violation is a protocol fault. JSON string
/// escaping guarantees payloads never contain a raw newline.
/// </summary>
internal sealed class NdjsonFramer
{
    /// <summary>Maximum size of a single frame in bytes, excluding the newline terminator.</summary>
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    private const byte Newline = (byte)'\n';
    private const int ReadChunkSize = 65536;

    private readonly Stream _stream;
    private readonly int _maxFrameBytes;
    private readonly byte[] _readChunk = new byte[ReadChunkSize];
    private byte[] _pending = Array.Empty<byte>();
    private int _pendingLength;

    /// <param name="stream">Underlying transport.</param>
    /// <param name="maxFrameBytes">Frame ceiling; defaults to <see cref="MaxFrameBytes"/>.</param>
    public NdjsonFramer(Stream stream, int maxFrameBytes = MaxFrameBytes)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxFrameBytes = maxFrameBytes > 0
            ? maxFrameBytes
            : throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
    }

    /// <summary>
    /// Read the next frame's payload bytes, without the newline. Returns <c>null</c> at a clean
    /// end of stream. Throws <see cref="PythonHostProtocolException"/> when a frame exceeds the
    /// ceiling or the stream ends part way through a frame.
    /// </summary>
    public async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newlineIndex = Array.IndexOf(_pending, Newline, 0, _pendingLength);
            if (newlineIndex >= 0)
            {
                if (newlineIndex > _maxFrameBytes)
                    throw new PythonHostProtocolException("Incoming frame exceeds the size limit.");

                var frame = new byte[newlineIndex];
                Array.Copy(_pending, frame, newlineIndex);
                ConsumePending(newlineIndex + 1);
                return frame;
            }

            if (_pendingLength > _maxFrameBytes)
                throw new PythonHostProtocolException("Incoming frame exceeds the size limit.");

            var read = await _stream.ReadAsync(_readChunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_pendingLength > 0)
                    throw new PythonHostProtocolException("Connection closed mid-frame.");
                return null;
            }

            AppendPending(_readChunk, read);
        }
    }

    /// <summary>
    /// Write one frame: the payload bytes followed by a newline. Throws
    /// <see cref="PythonHostProtocolException"/> when the payload exceeds the ceiling.
    /// </summary>
    public async Task WriteFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > _maxFrameBytes)
            throw new PythonHostProtocolException("Outgoing frame exceeds the size limit.");

        var buffer = ArrayPool<byte>.Shared.Rent(payload.Length + 1);
        try
        {
            payload.Span.CopyTo(buffer);
            buffer[payload.Length] = Newline;
            await _stream.WriteAsync(buffer.AsMemory(0, payload.Length + 1), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void AppendPending(byte[] source, int count)
    {
        EnsurePendingCapacity(_pendingLength + count);
        Array.Copy(source, 0, _pending, _pendingLength, count);
        _pendingLength += count;
    }

    private void ConsumePending(int count)
    {
        var remaining = _pendingLength - count;
        if (remaining > 0)
            Array.Copy(_pending, count, _pending, 0, remaining);
        _pendingLength = remaining;
    }

    private void EnsurePendingCapacity(int required)
    {
        if (_pending.Length >= required)
            return;
        var doubled = _pending.Length == 0 ? ReadChunkSize : _pending.Length * 2;
        Array.Resize(ref _pending, Math.Max(doubled, required));
    }
}
