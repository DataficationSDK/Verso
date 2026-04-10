namespace MyExtension;

/// <summary>
/// Example <see cref="ITheme"/> scaffold that defines a dark theme.
/// Customize the color tokens, typography, and spacing to create your own theme.
/// </summary>
[VersoExtension]
public sealed class SampleTheme : ITheme
{
    public string ExtensionId => "com.example.myextension.theme";
    public string Name => "Sample Theme";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample dark theme.";

    public string ThemeId => "myextension.dark";
    public string DisplayName => "My Dark Theme";
    public ThemeKind ThemeKind => ThemeKind.Dark;

    public ThemeColorTokens Colors => new()
    {
        EditorBackground = "#1e1e2e",
        EditorForeground = "#cdd6f4",
        CellBackground = "#24243a",
        CellBorder = "#45475a",
        ToolbarBackground = "#181825",
        ToolbarForeground = "#cdd6f4",
        SidebarBackground = "#181825",
        SidebarForeground = "#bac2de",
        AccentPrimary = "#89b4fa",
        AccentSecondary = "#74c7ec",
        StatusSuccess = "#a6e3a1",
        StatusWarning = "#f9e2af",
        StatusError = "#f38ba8"
    };

    public ThemeTypography Typography => new();

    public ThemeSpacing Spacing => new();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string? GetCustomToken(string key) => null;

    public SyntaxColorMap GetSyntaxColors()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#cba6f7");
        map.Set("string", "#a6e3a1");
        map.Set("comment", "#6c7086");
        map.Set("number", "#fab387");
        map.Set("type", "#89b4fa");
        map.Set("function", "#89dceb");
        map.Set("operator", "#94e2d5");
        return map;
    }
}
