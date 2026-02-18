using Microsoft.AspNetCore.Components;

namespace Verso.Blazor.Wasm.Services;

/// <summary>
/// Stub NavigationManager for VS Code webview hosting.
/// The webview URI scheme (vscode-webview://) is not parseable by
/// System.Uri, so we provide a synthetic base URI instead.
/// Navigation is not supported — this app is a single-view host.
/// </summary>
internal sealed class WebviewNavigationManager : NavigationManager
{
    public WebviewNavigationManager()
    {
        Initialize("app:///", "app:///");
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        // Navigation not supported in a VS Code webview.
    }
}
