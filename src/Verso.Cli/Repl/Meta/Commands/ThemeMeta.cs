using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.theme</c>. Prints or switches the active theme.</summary>
public sealed class ThemeMeta : IMetaCommand
{
    public string Name => "theme";
    public string Summary => "Prints or switches the active theme.";
    public string DetailedHelp =>
        ".theme [<name>]\n" +
        "  With no argument, prints the active theme.\n" +
        "  With a name, changes the active theme. Matched by DisplayName case-insensitively,\n" +
        "  with ThemeId as a fallback.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveTheme?.DisplayName ?? "<default>";
            context.Console.MarkupLine($"Active theme: [bold]{Markup.Escape(current)}[/]");
            return Task.FromResult(true);
        }

        var themes = context.Session.ExtensionHost.GetThemes();
        var match = themes.FirstOrDefault(t => string.Equals(t.DisplayName, arg, StringComparison.OrdinalIgnoreCase))
                    ?? themes.FirstOrDefault(t => string.Equals(t.ThemeId, arg, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = string.Join(", ", themes.Select(t => t.DisplayName));
            context.Console.MarkupLine($"[red]Theme '{Markup.Escape(arg)}' is not registered.[/] Available: {Markup.Escape(known)}");
            return Task.FromResult(true);
        }

        context.Session.ActiveTheme = match;
        context.Console.MarkupLine($"Switched to theme: [bold]{Markup.Escape(match.DisplayName)}[/]");
        return Task.FromResult(true);
    }
}
