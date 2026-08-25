using Verso.Abstractions;
using Verso.DataFrame.Resources;

namespace Verso.DataFrame.Formatters;

/// <summary>
/// Formats Microsoft.Data.Analysis.DataFrame values as bounded, theme-aware HTML tables.
/// The implementation intentionally uses reflection so the extension does not load a second,
/// incompatible Microsoft.Data.Analysis assembly beside the one owned by a language runtime.
/// </summary>
[VersoExtension]
public sealed class DataFrameFormatter : IDataFormatter
{
    internal const string DataFrameTypeName = "Microsoft.Data.Analysis.DataFrame";

    public string ExtensionId => "verso.dataframe.formatter";
    public string Name => Strings.Formatter_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Formatter_Description;

    // Third-party extensions run in an isolated AssemblyLoadContext. Advertising object here and
    // checking FullName in CanFormat avoids coupling to a second DataFrame assembly identity.
    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(object) };
    public int Priority => 50;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        // The only representation this formatter emits is HTML, so a context targeting any
        // other MIME type must be declined rather than answered with the wrong representation.
        if (!string.Equals(context.MimeType, "text/html", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            return Unwrap(value).GetType().FullName == DataFrameTypeName;
        }
        catch
        {
            return false;
        }
    }

    // A frame whose shape the reflection walk cannot read is allowed to throw: the resolver
    // then skips this formatter for the value and the kernel's native rendering still shows
    // the data, which beats replacing it with an error box.
    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var html = DataFrameHtmlRenderer.Render(
            Unwrap(value),
            context.CancellationToken,
            maxHeight: context.MaxHeight);

        return Task.FromResult(CellOutput.Html(html));
    }

    private static object Unwrap(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Display calls made from PowerShell may preserve the PSObject wrapper. Avoid taking a
        // dependency on System.Management.Automation solely to reach BaseObject.
        if (value.GetType().FullName != "System.Management.Automation.PSObject")
            return value;

        return value.GetType().GetProperty("BaseObject")?.GetValue(value) ?? value;
    }
}
