using System.Net;
using System.Text;
using Verso.Abstractions;

namespace Verso.Extensions.Formatters;

/// <summary>
/// Formats exceptions as structured HTML with CSS class hooks for styling.
/// </summary>
[VersoExtension]
public sealed class ExceptionFormatter : IDataFormatter
{
    // --- IExtension ---

    public string ExtensionId => "verso.formatter.exception";
    public string Name => "Exception Formatter";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats exceptions as structured HTML.";

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(Exception) };
    public int Priority => 50;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        return value is Exception;
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var exception = (Exception)value;
        var sb = new StringBuilder();
        RenderException(sb, exception);
        return Task.FromResult(new CellOutput("text/html", sb.ToString()));
    }

    private static void RenderException(StringBuilder sb, Exception ex)
    {
        sb.Append("<div class=\"verso-exception\">");
        sb.Append("<span class=\"verso-exception-type\">")
          .Append(WebUtility.HtmlEncode(ex.GetType().FullName ?? ex.GetType().Name))
          .Append("</span>");
        sb.Append("<span class=\"verso-exception-message\">: ")
          .Append(WebUtility.HtmlEncode(ex.Message))
          .Append("</span>");

        if (ex.StackTrace is not null)
        {
            sb.Append("<pre class=\"verso-exception-stacktrace\">")
              .Append(WebUtility.HtmlEncode(ex.StackTrace))
              .Append("</pre>");
        }

        if (ex.InnerException is not null)
        {
            sb.Append("<div class=\"verso-exception-inner\">");
            sb.Append("<strong>Inner Exception:</strong>");
            RenderException(sb, ex.InnerException);
            sb.Append("</div>");
        }

        sb.Append("</div>");
    }
}
