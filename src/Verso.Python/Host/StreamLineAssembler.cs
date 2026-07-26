using System.Text;
using Verso.Abstractions;

namespace Verso.Python.Host;

/// <summary>
/// Accumulates streamed text into a single output block per unbroken run of one channel, honouring
/// the carriage return the way a terminal does.
/// <para>
/// A progress bar redraws itself by writing a carriage return followed by the whole line again,
/// several times a second. Appended verbatim that is a wall of near-identical lines; interpreted
/// as a terminal would, it is one line that changes, which is what its author intended.
/// </para>
/// <para>
/// A run ends when the other channel writes, so standard output and standard error keep their
/// relative order instead of collapsing into two blocks that no longer interleave.
/// </para>
/// </summary>
internal sealed class StreamLineAssembler
{
    /// <summary>
    /// How large one block may grow before the next chunk starts another. Revising a block means
    /// sending everything it holds, so an unbounded block would make each chunk cost more than the
    /// last. Rolling over keeps that cost flat, at the price of a page break every so often in
    /// output long enough that nobody reads it as one piece anyway.
    /// </summary>
    private const int MaxBlockCharacters = 64 * 1024;

    /// <summary>Lines in the current block that a newline has already ended.</summary>
    private readonly StringBuilder _committed = new();

    /// <summary>The line being written, which a carriage return can still discard.</summary>
    private readonly StringBuilder _pending = new();

    private OutputChannel? _channel;
    private int _run;

    /// <summary>
    /// Identifies the block being written. Stable for as long as the run lasts, which is what lets
    /// the host revise that block rather than adding another.
    /// </summary>
    public string BlockId => $"stream-{_run}";

    /// <summary>The channel the current run belongs to.</summary>
    public OutputChannel Channel => _channel ?? OutputChannel.Stdout;

    /// <summary>
    /// Takes a chunk and returns what the block should now contain, or null when there is nothing
    /// to show yet. Starts a new block when the channel changes, and when the current one has grown
    /// large enough that rewriting it again would be wasteful.
    /// </summary>
    public string? Append(OutputChannel channel, string text)
    {
        // Rolling over here rather than at the end of the previous call keeps the returned content
        // and the block id describing the same block, which is what the caller writes.
        if (_channel != channel || _committed.Length + _pending.Length >= MaxBlockCharacters)
        {
            StartNewRun(channel);
        }

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    // What follows overwrites this line from its start. A carriage return before
                    // a newline is a Windows line ending, and the newline case then commits an
                    // empty pending line, which is correct.
                    _pending.Clear();
                    break;

                case '\n':
                    _committed.Append(_pending).Append('\n');
                    _pending.Clear();
                    break;

                default:
                    _pending.Append(ch);
                    break;
            }
        }

        if (_committed.Length == 0 && _pending.Length == 0)
            return null;

        return _committed.ToString() + _pending.ToString();
    }

    private void StartNewRun(OutputChannel channel)
    {
        _channel = channel;
        _committed.Clear();
        _pending.Clear();
        _run++;
    }
}
