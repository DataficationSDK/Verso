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
    private readonly PpConfiguration _configuration;
    private readonly KernelPromptCallbacks _callbacks;
    private readonly PpPrompt _prompt;

    public PrettyPromptDriver(ReplSession session, string? historyFilePath, bool useColor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // Single Prompt, single callbacks reused across every ReadLineAsync call —
        // the canonical usage pattern per CSharpRepl. The prompt text still varies
        // per cycle (input counter, active kernel) but PromptConfiguration.Prompt
        // has a public setter, so we mutate the existing configuration in ReadAsync.
        _callbacks = new KernelPromptCallbacks(_session);
        _configuration = new PpConfiguration(prompt: BuildPrompt(0, _session.ActiveKernelId));
        _prompt = new PpPrompt(
            persistentHistoryFilepath: historyFilePath,
            callbacks: _callbacks,
            configuration: _configuration);
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

        // Update the prompt text in place. PromptConfiguration.Prompt has a public
        // setter for exactly this reason; no need to reconstruct the Prompt.
        _configuration.Prompt = BuildPrompt(inputCounter, activeKernelId);

        // PrettyPrompt captures Console.CursorTop inside its CodePane constructor
        // and (on macOS/Linux) never recomputes it for the lifetime of the prompt.
        // See https://github.com/dotnet/runtime/issues/88343 and the guard in
        // PrettyPrompt's CodePane.MeasureConsole. Its own RenderPrompt later writes
        // newlines to reserve space for the popup, but by then TopCoordinate has
        // already latched onto the pre-scroll cursor row. If the previous
        // meta-command left the cursor near the bottom of the window (e.g. after
        // a Spectre Table render), the popup ends up positioned into nonexistent
        // rows and never appears. We pre-scroll through System.Console here so
        // PrettyPrompt observes a cursor row that leaves room for a popup.
        EnsurePopupRoom();

        try
        {
            var response = await _prompt.ReadLineAsync();

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

    private static void EnsurePopupRoom()
    {
        // Matches PromptConfiguration defaults: 9 max completion items + 2 border
        // rows. Leave a little extra slack so the prompt line itself fits too.
        const int PopupReserveRows = 12;

        int windowHeight;
        int cursorTop;
        try
        {
            windowHeight = Console.WindowHeight;
            cursorTop = Console.CursorTop;
        }
        catch
        {
            // Console dimensions can be unavailable in some terminals; nothing to do.
            return;
        }

        int avail = windowHeight - cursorTop;
        if (avail >= PopupReserveRows) return;

        int scroll = PopupReserveRows - avail;
        for (int i = 0; i < scroll; i++)
            Console.WriteLine();
        // Console.WriteLine on Unix writes "\n"; at the bottom of the window the
        // terminal scrolls the buffer and .NET clamps CursorTop at WindowHeight-1.
        // Move the cursor back up so the prompt appears right under the previous
        // output instead of way down the screen. Using SetCursorPosition keeps
        // .NET's cached cursor coordinates in sync with what PrettyPrompt will read.
        int newTop = Math.Max(0, Console.CursorTop - scroll);
        try
        {
            Console.SetCursorPosition(0, newTop);
        }
        catch
        {
            // Swallow; worst case the prompt just renders lower on screen.
        }
    }

    public Task AddHistoryAsync(string submission) => Task.CompletedTask;

    public ValueTask DisposeAsync() => _prompt.DisposeAsync();

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
