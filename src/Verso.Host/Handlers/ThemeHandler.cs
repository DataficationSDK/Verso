using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;

namespace Verso.Host.Handlers;

public static class ThemeHandler
{
    public static ThemeResult? HandleGetTheme(NotebookSession ns)
    {
        var theme = ns.Scaffold.ThemeEngine?.ActiveTheme;
        if (theme is null)
            return null;

        var colors = new Dictionary<string, string>();
        var colorTokens = theme.Colors;
        AddColorToken(colors, "editorBackground", colorTokens.EditorBackground);
        AddColorToken(colors, "editorForeground", colorTokens.EditorForeground);
        AddColorToken(colors, "editorLineNumber", colorTokens.EditorLineNumber);
        AddColorToken(colors, "editorCursor", colorTokens.EditorCursor);
        AddColorToken(colors, "editorSelection", colorTokens.EditorSelection);
        AddColorToken(colors, "editorGutter", colorTokens.EditorGutter);
        AddColorToken(colors, "editorWhitespace", colorTokens.EditorWhitespace);
        AddColorToken(colors, "cellBackground", colorTokens.CellBackground);
        AddColorToken(colors, "cellBorder", colorTokens.CellBorder);
        AddColorToken(colors, "cellActiveBorder", colorTokens.CellActiveBorder);
        AddColorToken(colors, "cellHoverBackground", colorTokens.CellHoverBackground);
        AddColorToken(colors, "cellOutputBackground", colorTokens.CellOutputBackground);
        AddColorToken(colors, "cellOutputForeground", colorTokens.CellOutputForeground);
        AddColorToken(colors, "cellErrorBackground", colorTokens.CellErrorBackground);
        AddColorToken(colors, "cellErrorForeground", colorTokens.CellErrorForeground);
        AddColorToken(colors, "cellRunningIndicator", colorTokens.CellRunningIndicator);
        AddColorToken(colors, "toolbarBackground", colorTokens.ToolbarBackground);
        AddColorToken(colors, "toolbarForeground", colorTokens.ToolbarForeground);
        AddColorToken(colors, "toolbarButtonHover", colorTokens.ToolbarButtonHover);
        AddColorToken(colors, "toolbarSeparator", colorTokens.ToolbarSeparator);
        AddColorToken(colors, "toolbarDisabledForeground", colorTokens.ToolbarDisabledForeground);
        AddColorToken(colors, "accentPrimary", colorTokens.AccentPrimary);
        AddColorToken(colors, "accentSecondary", colorTokens.AccentSecondary);
        AddColorToken(colors, "accentForeground", colorTokens.AccentForeground);
        AddColorToken(colors, "statusSuccess", colorTokens.StatusSuccess);
        AddColorToken(colors, "statusWarning", colorTokens.StatusWarning);
        AddColorToken(colors, "statusError", colorTokens.StatusError);
        AddColorToken(colors, "statusInfo", colorTokens.StatusInfo);
        AddColorToken(colors, "bgDefault", colorTokens.BgDefault);
        AddColorToken(colors, "bgElevated", colorTokens.BgElevated);
        AddColorToken(colors, "bgSunken", colorTokens.BgSunken);
        AddColorToken(colors, "fgDefault", colorTokens.FgDefault);
        AddColorToken(colors, "fgMuted", colorTokens.FgMuted);
        AddColorToken(colors, "fgSubtle", colorTokens.FgSubtle);

        var syntaxColors = theme.GetSyntaxColors().GetAll()
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var typography = theme.Typography;
        var spacing = theme.Spacing;
        var elevation = theme.Elevation;

        return new ThemeResult
        {
            ThemeId = theme.ThemeId,
            DisplayName = theme.DisplayName,
            ThemeKind = theme.ThemeKind.ToString(),
            Colors = colors,
            SyntaxColors = syntaxColors,
            Typography = new ThemeTypographyDto
            {
                EditorFont = MapFont(typography.EditorFont),
                UIFont = MapFont(typography.UIFont),
                ProseFont = MapFont(typography.ProseFont),
                CodeOutputFont = MapFont(typography.CodeOutputFont)
            },
            Spacing = new ThemeSpacingDto
            {
                CellPadding = spacing.CellPadding,
                CellGap = spacing.CellGap,
                ToolbarHeight = spacing.ToolbarHeight,
                SidebarWidth = spacing.SidebarWidth,
                ContentMarginHorizontal = spacing.ContentMarginHorizontal,
                ContentMarginVertical = spacing.ContentMarginVertical,
                CellBorderRadius = spacing.CellBorderRadius,
                ButtonBorderRadius = spacing.ButtonBorderRadius,
                OutputPadding = spacing.OutputPadding,
                ScrollbarWidth = spacing.ScrollbarWidth,
                ShapeSmall = spacing.ShapeSmall,
                ShapeMedium = spacing.ShapeMedium,
                ShapeLarge = spacing.ShapeLarge,
                ShapeFull = spacing.ShapeFull
            },
            Elevation = new ThemeElevationDto
            {
                Level0 = elevation.Level0,
                Level1 = elevation.Level1,
                Level2 = elevation.Level2,
                Level3 = elevation.Level3
            }
        };
    }

    public static ThemesResult HandleGetThemes(NotebookSession ns)
    {
        var themes = ns.Scaffold.ThemeEngine?.AvailableThemes ?? Array.Empty<Verso.Abstractions.ITheme>();
        var activeId = ns.Scaffold.ThemeEngine?.ActiveTheme?.ThemeId;
        return new ThemesResult
        {
            Themes = themes.Select(t => new ThemeListItemDto
            {
                Id = t.ThemeId,
                DisplayName = t.DisplayName,
                ThemeKind = t.ThemeKind.ToString(),
                IsActive = string.Equals(t.ThemeId, activeId, StringComparison.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    public static ThemeResult? HandleSwitchTheme(NotebookSession ns, JsonElement? @params)
    {
        var themeId = @params?.GetProperty("themeId").GetString()
            ?? throw new JsonException("Missing themeId");
        ns.Scaffold.ThemeEngine!.SetActiveTheme(themeId);
        ns.Scaffold.Notebook.PreferredThemeId = themeId;
        return HandleGetTheme(ns);
    }

    private static void AddColorToken(Dictionary<string, string> dict, string key, string value)
    {
        dict[key] = value;
    }

    private static FontDto MapFont(FontDescriptor font)
    {
        return new FontDto
        {
            Family = font.Family,
            SizePx = font.SizePx,
            Weight = font.Weight,
            LineHeight = font.LineHeight
        };
    }
}
