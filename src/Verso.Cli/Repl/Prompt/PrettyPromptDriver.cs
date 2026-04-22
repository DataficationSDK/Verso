using PrettyPrompt.Highlighting;
using PpPrompt = PrettyPrompt.Prompt;
using PpConfiguration = PrettyPrompt.PromptConfiguration;

namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Full-featured prompt driver backed by PrettyPrompt. Provides completion,
/// highlighting, and persistent history. Used when the process is attached to
/// a real TTY. See <see cref="PlainPromptDriver"/> for the redirected-stdin path.
/// </summary>
public sealed class PrettyPromptDriver : IReplPrompt
{
    private readonly ReplSession _session;
    private readonly string? _historyFilePath;
    private readonly bool _useColor;
    private readonly KernelPromptCallbacks _callbacks;

    public PrettyPromptDriver(ReplSession session, string? historyFilePath, bool useColor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _useColor = useColor;
        _historyFilePath = historyFilePath;
        _callbacks = new KernelPromptCallbacks(session);
    }

    public async Task<ReplInput> ReadAsync(int inputCounter, string? activeKernelId, string? initialText, CancellationToken ct)
    {
        // .recall seed: PrettyPrompt doesn't expose a "preload buffer" API, so we
        // echo the recalled source above the prompt and let the user retype / edit.
        if (!string.IsNullOrEmpty(initialText))
        {
            Console.WriteLine();
            Console.WriteLine("(recalled — copy and paste or edit below)");
            foreach (var line in initialText.Split('\n'))
                Console.WriteLine("  " + line.TrimEnd('\r'));
            Console.WriteLine();
        }

        // Recreate the Prompt each cycle so the prompt string reflects the current
        // counter and kernel. Construction is cheap; history remains file-backed.
        var configuration = new PpConfiguration(prompt: BuildPrompt(inputCounter, activeKernelId));
        await using var prompt = new PpPrompt(
            persistentHistoryFilepath: _historyFilePath,
            callbacks: _callbacks,
            configuration: configuration);

        try
        {
            var response = await prompt.ReadLineAsync();

            if (response.CancellationToken.IsCancellationRequested || ct.IsCancellationRequested)
                return ReplInput.Cancelled;

            if (!response.IsSuccess)
            {
                if (string.IsNullOrEmpty(response.Text))
                    return ReplInput.Eof;
                return ReplInput.Cancelled;
            }

            return ReplInput.Submission(response.Text);
        }
        catch (OperationCanceledException)
        {
            return ReplInput.Cancelled;
        }
    }

    public Task AddHistoryAsync(string submission) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static FormattedString BuildPrompt(int counter, string? kernelId)
    {
        var kernel = ShortKernelName(kernelId);
        var counterPart = $"[{counter}]";
        var text = $"{counterPart} {kernel} > ";

        var dim = new ConsoleFormat(Foreground: AnsiColor.BrightBlack);
        var cyan = new ConsoleFormat(Foreground: AnsiColor.Cyan);

        var spans = new List<FormatSpan>
        {
            new(0, counterPart.Length, dim),
            new(counterPart.Length + 1, kernel.Length, cyan),
            new(counterPart.Length + 1 + kernel.Length + 1, 1, cyan)
        };
        return new FormattedString(text, spans);
    }

    private static string ShortKernelName(string? kernelId) => (kernelId ?? "").ToLowerInvariant() switch
    {
        "csharp" => "C#",
        "fsharp" => "F#",
        "javascript" => "js",
        "typescript" => "ts",
        "python" => "py",
        "powershell" => "ps",
        "sql" => "sql",
        "http" => "http",
        "" => "?",
        _ => kernelId!
    };
}
