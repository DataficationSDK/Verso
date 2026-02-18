using System.Web;

namespace MyExtension;

/// <summary>
/// Example <see cref="ICellRenderer"/> scaffold.
/// Replace this with your own cell renderer implementation.
/// </summary>
[VersoExtension]
public sealed class SampleRenderer : ICellRenderer
{
    public string ExtensionId => "com.example.myextension.renderer";
    public string Name => "Sample Renderer";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample cell renderer.";
    public string CellTypeId => "sample";
    public string DisplayName => "Sample";
    public bool CollapsesInputOnExecute => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        var html = $"<pre><code>{HttpUtility.HtmlEncode(source)}</code></pre>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        var html = $"<div>{HttpUtility.HtmlEncode(output.Content)}</div>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public string? GetEditorLanguage() => null;
}
