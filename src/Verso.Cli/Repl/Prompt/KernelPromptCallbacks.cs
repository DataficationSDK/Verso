using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;
using Verso.Abstractions;
using PpCallbacks = PrettyPrompt.PromptCallbacks;

namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Bridges PrettyPrompt's completion, highlighting, and overload callbacks to
/// <see cref="ILanguageKernel"/> on the active kernel. Selection follows
/// <see cref="ReplSession.ActiveKernelId"/> so switching via <c>.kernel</c> takes
/// effect on the very next keystroke.
/// </summary>
public sealed class KernelPromptCallbacks : PpCallbacks
{
    private readonly ReplSession _session;

    public KernelPromptCallbacks(ReplSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    protected override async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        if (text.TrimStart().StartsWith("."))
            return await GetMetaCommandCompletionsAsync(text, caret);

        var kernel = GetActiveKernel();
        if (kernel is null) return Array.Empty<CompletionItem>();

        IReadOnlyList<Completion> completions;
        try
        {
            completions = await kernel.GetCompletionsAsync(text, caret);
        }
        catch
        {
            return Array.Empty<CompletionItem>();
        }

        if (completions.Count == 0) return Array.Empty<CompletionItem>();

        var items = new List<CompletionItem>(completions.Count);
        foreach (var c in completions)
        {
            items.Add(new CompletionItem(
                replacementText: c.InsertText,
                displayText: new FormattedString(c.DisplayText),
                filterText: c.DisplayText,
                getExtendedDescription: string.IsNullOrEmpty(c.Description)
                    ? null
                    : (_) => Task.FromResult(new FormattedString(c.Description!))));
        }
        return items;
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text, int caret, CancellationToken cancellationToken)
    {
        int start = caret;
        while (start > 0 && IsIdentifierChar(text[start - 1])) start--;
        int end = caret;
        while (end < text.Length && IsIdentifierChar(text[end])) end++;
        return Task.FromResult(TextSpan.FromBounds(start, end));
    }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(
        string text, CancellationToken cancellationToken)
    {
        return Task.FromResult((IReadOnlyCollection<FormatSpan>)MinimalHighlighter.Highlight(text, _session.ActiveKernelId));
    }

    /// <summary>
    /// Suppresses the auto-opening completion popup while the user is typing a
    /// meta-command. Without this, pressing Enter after <c>.exit</c> would commit
    /// the completion rather than submit the command, forcing a second Enter.
    /// The user can still trigger completion manually with Ctrl+Space.
    /// </summary>
    protected override Task<bool> ShouldOpenCompletionWindowAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        if (text.TrimStart().StartsWith("."))
            return Task.FromResult(false);
        return base.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
    }

    /// <summary>
    /// Enter inserts a newline unless the buffer already ends with two trailing
    /// newlines — then a third Enter submits. Meta-commands (leading '.') and
    /// Shift/Ctrl variants keep their default single-Enter submit behaviour so
    /// quick commands and explicit overrides stay snappy.
    /// </summary>
    protected override Task<KeyPress> TransformKeyPressAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var info = keyPress.ConsoleKeyInfo;
        if (info.Key != ConsoleKey.Enter || info.Modifiers != 0)
            return Task.FromResult(keyPress);

        if (ShouldSubmitNow(text, caret))
            return Task.FromResult(keyPress);

        // Re-emit as Shift+Enter so PrettyPrompt's NewLine binding inserts a line break.
        var shiftEnter = new ConsoleKeyInfo(
            keyChar: '\r', key: ConsoleKey.Enter,
            shift: true, alt: false, control: false);
        return Task.FromResult(new KeyPress(shiftEnter));
    }

    internal static bool ShouldSubmitNow(string text, int caret)
    {
        // Meta-commands submit immediately — they're single-token and users don't
        // want to double-tap Enter after `.exit` or `.list kernels`.
        if (text.TrimStart().StartsWith(".") && !text.Contains('\n'))
            return true;

        if (caret != text.Length) return false;
        if (text.Trim().Length == 0) return false;

        // Count trailing newlines (ignoring CR in CRLF pairs). This Enter plus the
        // existing trailing count must reach 2 blank lines at the end of input.
        int trailing = 0;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch == '\n') trailing++;
            else if (ch == '\r') continue;
            else break;
        }
        return trailing >= 2;
    }

    private Task<IReadOnlyList<CompletionItem>> GetMetaCommandCompletionsAsync(string text, int caret)
    {
        // Meta-command completion: offer every registered command by name.
        // Completion only fires on the first token, before a space.
        var trimmed = text.TrimStart();
        int afterDot = trimmed.IndexOf('.');
        if (afterDot < 0) return Task.FromResult((IReadOnlyList<CompletionItem>)Array.Empty<CompletionItem>());

        // Bail if the caret has already passed a whitespace character — we only
        // complete the command name, not its arguments.
        for (int i = 0; i < caret && i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) && text[i] != '\n')
            {
                // If any whitespace precedes the caret, we're in the arg portion.
                var firstDot = text.IndexOf('.');
                if (firstDot >= 0 && firstDot < i)
                    return Task.FromResult((IReadOnlyList<CompletionItem>)Array.Empty<CompletionItem>());
            }
        }

        var items = new List<CompletionItem>();
        // We don't have a back-reference to the registry here without adding a dependency —
        // hard-code the canonical list and let the registry validate at dispatch.
        foreach (var name in MetaCommandNames)
        {
            items.Add(new CompletionItem(
                replacementText: "." + name,
                displayText: new FormattedString("." + name),
                filterText: "." + name));
        }
        return Task.FromResult((IReadOnlyList<CompletionItem>)items);
    }

    private static readonly string[] MetaCommandNames =
    {
        "help", "exit", "quit", "clear", "reset", "kernel", "vars", "list",
        "theme", "layout", "md", "code", "history", "recall", "rerun", "set",
        "view", "save", "load", "convert", "export", "publish"
    };

    private ILanguageKernel? GetActiveKernel()
    {
        var kernelId = _session.ActiveKernelId;
        if (string.IsNullOrEmpty(kernelId)) return null;
        foreach (var k in _session.ExtensionHost.GetKernels())
        {
            if (string.Equals(k.LanguageId, kernelId, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        return null;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
