using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that cycles to the next available theme.
/// </summary>
[VersoExtension]
public sealed class SwitchThemeAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.switch-theme";
    public string Name => "Switch Theme";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Cycles between available themes.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.switchTheme";
    public string DisplayName => "Switch Theme";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 55;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var themes = context.ExtensionHost.GetThemes();
        var enabled = themes.Count > 1;
        return Task.FromResult(enabled);
    }

    public Task ExecuteAsync(IToolbarActionContext context)
    {
        var themes = context.ExtensionHost.GetThemes();
        if (themes.Count <= 1) return Task.CompletedTask;

        // Find current theme index and cycle to next
        var currentIndex = -1;
        for (int i = 0; i < themes.Count; i++)
        {
            if (string.Equals(themes[i].ThemeId, context.Notebook.ActiveThemeId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        var nextIndex = (currentIndex + 1) % themes.Count;
        var nextTheme = themes[nextIndex];

        context.Notebook.SetActiveTheme(nextTheme.ThemeId);

        return Task.CompletedTask;
    }
}
