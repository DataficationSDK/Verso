using Verso.Abstractions;

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
    public string Name => "DataFrame Formatter";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Formats Microsoft.Data.Analysis.DataFrame values as HTML tables.";

    // Third-party extensions run in an isolated AssemblyLoadContext. Advertising object here and
    // checking FullName in CanFormat avoids coupling to a second DataFrame assembly identity.
    public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(object) };
    public int Priority => 50;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context)
    {
        try
        {
            return Unwrap(value).GetType().FullName == DataFrameTypeName;
        }
        catch
        {
            return false;
        }
    }

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var dataFrame = Unwrap(value);
        string html;

        try
        {
            html = DataFrameHtmlRenderer.Render(
                dataFrame,
                context.CancellationToken,
                maxHeight: context.MaxHeight);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            html = DataFrameHtmlRenderer.RenderError(ex.Message);
        }

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
