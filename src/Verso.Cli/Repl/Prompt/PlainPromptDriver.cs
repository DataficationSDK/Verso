using System.Text;

namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Line-oriented fallback prompt for terminals that cannot host PrettyPrompt
/// (stdin redirected, TERM=dumb, --plain, --no-color). Multi-line submissions
/// are delimited by a blank line or by a <c>;;</c> sentinel on its own line.
/// Idiomatic to F# interactive (fsi) users.
/// </summary>
public sealed class PlainPromptDriver : IReplPrompt
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly string _promptGlyph;
    private readonly bool _echoNewlines;

    public PlainPromptDriver(TextReader? input = null, TextWriter? output = null, string promptGlyph = "»")
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _promptGlyph = promptGlyph;

        // When stdin is redirected, the user's Enter press isn't echoed to stdout. Emit
        // a newline after each line read so the prompt and subsequent output don't run
        // together ("[1] » .list kernelsLanguage    ..."). Harmless on interactive TTY
        // because we never reach this code path there (PrettyPromptDriver handles TTY).
        _echoNewlines = Console.IsInputRedirected;
    }

    public Task<ReplInput> ReadAsync(int inputCounter, string? activeKernelId, string? initialText, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        if (!string.IsNullOrEmpty(initialText))
            buffer.AppendLine(initialText);

        var firstLine = true;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            WritePrompt(inputCounter, firstLine);
            _output.Flush();

            string? line;
            try
            {
                line = _input.ReadLine();
            }
            catch (IOException)
            {
                line = null;
            }

            if (_echoNewlines && line is not null) _output.WriteLine();

            if (line is null)
            {
                // EOF on stdin — either pipe closed or Ctrl+D on an empty buffer.
                if (buffer.Length == 0)
                    return Task.FromResult(ReplInput.Eof);
                return Task.FromResult(ReplInput.Submission(buffer.ToString().TrimEnd('\r', '\n')));
            }

            // ;; sentinel on its own line submits immediately (F# idiom).
            if (line.Trim() == ";;")
                return Task.FromResult(ReplInput.Submission(buffer.ToString().TrimEnd('\r', '\n')));

            // Meta-commands (starting with '.') submit immediately on their first line.
            // Users expect .exit, .help, .kernel csharp to take effect without an extra Enter.
            if (firstLine && line.TrimStart().StartsWith("."))
            {
                buffer.AppendLine(line);
                return Task.FromResult(ReplInput.Submission(buffer.ToString().TrimEnd('\r', '\n')));
            }

            // Blank line submits when the buffer already contains text.
            if (line.Length == 0)
            {
                if (buffer.Length == 0)
                {
                    // Blank input on first line is a no-op — re-prompt.
                    continue;
                }
                return Task.FromResult(ReplInput.Submission(buffer.ToString().TrimEnd('\r', '\n')));
            }

            buffer.AppendLine(line);
            firstLine = false;
        }
    }

    public Task AddHistoryAsync(string submission) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void WritePrompt(int inputCounter, bool firstLine)
    {
        if (firstLine)
            _output.Write($"[{inputCounter}] {_promptGlyph} ");
        else
            _output.Write("  ... ");
    }
}
