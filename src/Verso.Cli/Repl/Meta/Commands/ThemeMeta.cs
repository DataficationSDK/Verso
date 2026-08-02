using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.theme</c>. Prints or switches the active theme.</summary>
public sealed class ThemeMeta : IMetaCommand
{
    public string Name => "theme";
    public string Summary => Strings.Meta_Theme_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".theme [<name>]\n" + Strings.Meta_Theme_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveTheme?.DisplayName ?? Strings.Repl_MarkerDefault;
            context.Console.MarkupLine(Messages.Typed(Strings.Meta_Theme_Active, current));
            return Task.FromResult(true);
        }

        var themes = context.Session.ExtensionHost.GetThemes();
        var match = themes.FirstOrDefault(t => string.Equals(t.DisplayName, arg, StringComparison.OrdinalIgnoreCase))
                    ?? themes.FirstOrDefault(t => string.Equals(t.ThemeId, arg, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = string.Join(", ", themes.Select(t => t.DisplayName));
            context.Console.MarkupLine(
                Messages.In("red", Messages.Say(Strings.Error_ThemeNotRegistered, arg))
                + " " + Messages.Say(Strings.Error_AvailableThemes, known));
            return Task.FromResult(true);
        }

        context.Session.ActiveTheme = match;
        context.Console.MarkupLine(Messages.Typed(Strings.Meta_Theme_Switched, match.DisplayName));
        return Task.FromResult(true);
    }
}
