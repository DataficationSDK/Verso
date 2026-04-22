using PrettyPrompt.Highlighting;

namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// v1.0 fallback syntax highlighter: string literals and comments only, per
/// <see cref="ILanguageKernel.LanguageId"/>. Kernels that ship their own
/// semantic colouring can supply it later via a formatter; this class exists so
/// users get readable output in the REPL without requiring one.
/// </summary>
public static class MinimalHighlighter
{
    private static readonly ConsoleFormat StringFormat = new(Foreground: AnsiColor.Green);
    private static readonly ConsoleFormat CommentFormat = new(Foreground: AnsiColor.BrightBlack);

    public static IReadOnlyCollection<FormatSpan> Highlight(string text, string? languageId)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<FormatSpan>();

        var lang = languageId?.ToLowerInvariant() ?? "";
        return lang switch
        {
            "csharp" or "javascript" or "typescript" or "js" or "ts" or "java" or "cpp" or "c" or "go" or "rust"
                => ScanCFamily(text),
            "fsharp" or "fs"
                => ScanFSharp(text),
            "python" or "py"
                => ScanHashLine(text, '\''),
            "powershell" or "ps1" or "bash" or "shell" or "sh" or "http"
                => ScanHashLine(text, '\''),
            "html" or "xml" or "svg"
                => ScanHtml(text),
            "sql"
                => ScanSql(text),
            "mermaid"
                => ScanMermaid(text),
            _ => Array.Empty<FormatSpan>()
        };
    }

    private static List<FormatSpan> ScanCFamily(string text)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(i + 2, text.Length);
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (ch == '"' || ch == '\'')
            {
                var quote = ch;
                int start = i;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i += 2;
                    else if (text[i] == '\n') { break; }
                    else i++;
                }
                if (i < text.Length) i++;
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else
            {
                i++;
            }
        }
        return spans;
    }

    private static List<FormatSpan> ScanFSharp(string text)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (ch == '(' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                int depth = 1;
                while (i + 1 < text.Length && depth > 0)
                {
                    if (text[i] == '(' && text[i + 1] == '*') { depth++; i += 2; }
                    else if (text[i] == '*' && text[i + 1] == ')') { depth--; i += 2; }
                    else i++;
                }
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (ch == '"')
            {
                int start = i;
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i += 2;
                    else i++;
                }
                if (i < text.Length) i++;
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else { i++; }
        }
        return spans;
    }

    private static List<FormatSpan> ScanHashLine(string text, char altQuote)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '#')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (ch == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                int start = i;
                i += 3;
                while (i + 2 < text.Length && !(text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')) i++;
                i = Math.Min(i + 3, text.Length);
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else if (ch == '"' || ch == altQuote)
            {
                var quote = ch;
                int start = i;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i += 2;
                    else if (text[i] == '\n') break;
                    else i++;
                }
                if (i < text.Length) i++;
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else { i++; }
        }
        return spans;
    }

    private static List<FormatSpan> ScanHtml(string text)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            if (i + 3 < text.Length && text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                int start = i;
                i += 4;
                while (i + 2 < text.Length && !(text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '>')) i++;
                i = Math.Min(i + 3, text.Length);
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (text[i] == '"' || text[i] == '\'')
            {
                var quote = text[i];
                int start = i++;
                while (i < text.Length && text[i] != quote && text[i] != '\n') i++;
                if (i < text.Length) i++;
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else { i++; }
        }
        return spans;
    }

    private static List<FormatSpan> ScanSql(string text)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '-' && text[i + 1] == '-')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(i + 2, text.Length);
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else if (text[i] == '\'' || text[i] == '"')
            {
                var quote = text[i];
                int start = i++;
                while (i < text.Length && text[i] != quote && text[i] != '\n') i++;
                if (i < text.Length) i++;
                spans.Add(new FormatSpan(start, i - start, StringFormat));
            }
            else { i++; }
        }
        return spans;
    }

    private static List<FormatSpan> ScanMermaid(string text)
    {
        var spans = new List<FormatSpan>();
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '%' && text[i + 1] == '%')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, CommentFormat));
            }
            else { i++; }
        }
        return spans;
    }
}
