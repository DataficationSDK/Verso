using Verso.Abstractions;

namespace Verso.Extensions.Themes;

/// <summary>
/// Built-in light theme for Verso notebooks, inspired by VS Code Light+.
/// </summary>
[VersoExtension]
public sealed class VersoLightTheme : ITheme
{
    // --- IExtension ---

    public string ExtensionId => "verso.theme.light";
    public string Name => "Verso Light";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Default light theme for Verso notebooks.";

    // --- ITheme ---

    public string ThemeId => "verso-light";
    public string DisplayName => "Verso Light";
    public ThemeKind ThemeKind => ThemeKind.Light;

    public ThemeColorTokens Colors { get; } = new();
    public ThemeTypography Typography { get; } = new();
    public ThemeSpacing Spacing { get; } = new();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string? GetCustomToken(string key) => null;

    public SyntaxColorMap GetSyntaxColors()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#0000FF");
        map.Set("comment", "#008000");
        map.Set("string", "#A31515");
        map.Set("number", "#098658");
        map.Set("type", "#267F99");
        map.Set("function", "#795E26");
        map.Set("variable", "#001080");
        map.Set("operator", "#000000");
        map.Set("punctuation", "#000000");
        map.Set("preprocessor", "#808080");
        map.Set("attribute", "#267F99");
        map.Set("namespace", "#267F99");
        return map;
    }
}
