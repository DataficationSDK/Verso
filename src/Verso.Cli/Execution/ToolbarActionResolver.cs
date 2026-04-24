using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Cli.Execution;

/// <summary>
/// Resolution helpers for export-menu toolbar actions and themes, shared by
/// <c>verso export</c> and <c>verso repl</c>'s <c>.export</c> / <c>.theme</c>
/// meta-commands. Matches the contract documented in
/// <c>agent-docs/specifications/Verso/Verso-Repl-Specification-v1.0.md §6.1</c>.
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
            error = $"Error: Multiple export actions share display name '{format}'. Disambiguate by ActionId: {ids}.";
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
        error = $"Error: Export format '{format}' is not registered.";
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
            error = $"Error: Multiple themes share display name '{value}'. Disambiguate by ThemeId: {ids}.";
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
        error = $"Error: Theme '{value}' is not registered." +
                (known.Length > 0 ? $" Available themes: {known}." : "");
        return false;
    }
}
