using Verso.Abstractions;

namespace Verso.FSharp.Kernel;

/// <summary>
/// Stands in for <see cref="Console.In"/> while a cell runs, so <c>Console.ReadLine()</c> asks the
/// front end for a line instead of reading the process's standard input. In the VS Code host that
/// stream carries the host protocol, and a cell reading from it would stall the notebook.
/// </summary>
internal sealed class HostInputReader : TextReader
{
    private readonly IExecutionContext _context;
    private readonly StringWriter _console;
    private readonly TextReader _fallback;
    private string _buffer = "";
    private int _position;
    private int _promptFrom;

    /// <param name="context">The running cell's context, which carries the input request to the host.</param>
    /// <param name="console">The cell's captured standard output, read for the prompt and given the echo.</param>
    /// <param name="fallback">The reader this one replaces, used when the host cannot ask the user.</param>
    public HostInputReader(IExecutionContext context, StringWriter console, TextReader fallback)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public override string? ReadLine()
    {
        if (_position < _buffer.Length)
        {
            var rest = _buffer[_position..].TrimEnd('\n');
            _position = _buffer.Length;
            return rest;
        }

        return RequestLine();
    }

    public override int Read()
    {
        if (_position >= _buffer.Length)
        {
            var line = RequestLine();
            if (line is null)
                return -1;

            _buffer = line + "\n";
            _position = 0;
        }

        return _buffer[_position++];
    }

    // Never prompts: a peek that opened an input box would surprise code that only checks for input.
    public override int Peek() => _position < _buffer.Length ? _buffer[_position] : -1;

    private string? RequestLine()
    {
        string? line;
        try
        {
            // Console.ReadLine is synchronous, so this blocks the cell's thread until the user answers.
            line = _context.RequestInputAsync(CurrentPrompt(), false, _context.CancellationToken)
                .GetAwaiter().GetResult();
        }
        catch (NotSupportedException)
        {
            // A command-line host has no input box, but its standard input is the user's own
            // terminal or pipe, so reading it is what the cell meant.
            return _fallback.ReadLine();
        }

        // A terminal shows what was typed and moves to the next line; keep the transcript the same.
        if (line is not null)
            _console.WriteLine(line);

        // The next prompt is whatever the cell writes from here on, not this answer.
        _promptFrom = _console.GetStringBuilder().Length;
        return line;
    }

    /// <summary>
    /// The text the cell wrote just before asking, e.g. the "Name: " of
    /// <c>Console.Write("Name: ")</c>, since that output is not on screen until the cell ends.
    /// </summary>
    private string CurrentPrompt()
    {
        var written = _console.GetStringBuilder();
        var end = written.Length;
        while (end > _promptFrom && char.IsWhiteSpace(written[end - 1]))
            end--;

        var start = end;
        while (start > _promptFrom && written[start - 1] != '\n')
            start--;

        return written.ToString(start, end - start).Trim();
    }
}
