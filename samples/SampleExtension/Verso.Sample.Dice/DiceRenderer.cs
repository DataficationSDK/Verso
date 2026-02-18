using System.Text;
using System.Web;
using Verso.Abstractions;

namespace Verso.Sample.Dice;

/// <summary>
/// Renders dice cells with syntax highlighting for input and visual dice faces for output.
/// </summary>
[VersoExtension]
public sealed class DiceRenderer : ICellRenderer
{
    public string ExtensionId => "com.verso.sample.dice.renderer";
    public string Name => "Dice Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders dice notation with visual dice faces";
    public string CellTypeId => "dice";
    public string DisplayName => "Dice";
    public bool CollapsesInputOnExecute => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        var escaped = HttpUtility.HtmlEncode(source);
        var html = $"<pre style=\"font-family:monospace;padding:8px;background:#f5f5f5;border-radius:4px;\">" +
                   $"<code>{escaped}</code></pre>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        if (output.IsError)
        {
            var errorHtml = $"<div style=\"color:#d32f2f;padding:4px 8px;\">{HttpUtility.HtmlEncode(output.Content)}</div>";
            return Task.FromResult(new RenderResult("text/html", errorHtml));
        }

        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:monospace;padding:8px;\">");

        foreach (var line in output.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append($"<div style=\"margin:4px 0;\">{HttpUtility.HtmlEncode(line)}</div>");
        }

        sb.Append("</div>");
        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public string? GetEditorLanguage() => null;
}
