using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Extensions;

namespace Verso.Cli.Execution;

/// <summary>
/// Resolution helpers for export-menu toolbar actions and themes, shared by
/// <c>verso export</c> and <c>verso repl</c>'s <c>.export</c> / <c>.theme</c>
/// meta-commands.
/// </summary>
public static class ToolbarActionResolver
{
    /// <summary>
    /// Resolves an export-menu toolbar action by display name (case-insensitive),
    /// falling back to <c>ActionId</c> to disambiguate collisions.
    /// </summary>
    public static bool TryResolveAction(
        ExtensionHost extensionHost,
        string format,
        out IToolbarAction action,
        out string error)
    {
        var exportActions = extensionHost.GetToolbarActions()
            .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
            .ToList();

        var byName = exportActions
            .Where(a => string.Equals(a.DisplayName, format, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
        {
            action = byName[0];
            error = "";
            return true;
        }

        if (byName.Count > 1)
        {
            var exactById = byName.FirstOrDefault(a => a.ActionId == format);
            if (exactById is not null)
            {
                action = exactById;
                error = "";
                return true;
            }

            var ids = string.Join(", ", byName.Select(a => a.ActionId));
            action = null!;
            error = Messages.Error(string.Format(Strings.Error_MultipleExportActions, format, ids));
            return false;
        }

        var byId = exportActions.FirstOrDefault(a => a.ActionId == format);
        if (byId is not null)
        {
            action = byId;
            error = "";
            return true;
        }

        action = null!;
        error = Messages.Error(string.Format(Strings.Error_ExportFormatNotRegistered, format));
        return false;
    }

    /// <summary>
    /// Resolves a theme by display name (case-insensitive), falling back to
    /// <c>ThemeId</c> for collision disambiguation.
    /// </summary>
    public static bool TryResolveTheme(
        ExtensionHost extensionHost,
        string value,
        out ITheme theme,
        out string error)
    {
        var themes = extensionHost.GetThemes();

        var byName = themes
            .Where(t => string.Equals(t.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
        {
            theme = byName[0];
            error = "";
            return true;
        }

        if (byName.Count > 1)
        {
            var exactById = byName.FirstOrDefault(t =>
                string.Equals(t.ThemeId, value, StringComparison.OrdinalIgnoreCase));
            if (exactById is not null)
            {
                theme = exactById;
                error = "";
                return true;
            }

            var ids = string.Join(", ", byName.Select(t => t.ThemeId));
            theme = null!;
            error = Messages.Error(string.Format(Strings.Error_MultipleThemes, value, ids));
            return false;
        }

        var byId = themes.FirstOrDefault(t =>
            string.Equals(t.ThemeId, value, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            theme = byId;
            error = "";
            return true;
        }

        theme = null!;
        var known = string.Join(", ", themes.Select(t => t.DisplayName));
        error = Messages.Error(string.Format(Strings.Error_ThemeNotRegistered, value))
                + (known.Length > 0 ? " " + string.Format(Strings.Error_AvailableThemes, known) : "");
        return false;
    }
}
