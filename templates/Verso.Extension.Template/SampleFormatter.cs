namespace MyExtension;

/// <summary>
/// Example <see cref="IDataFormatter"/> that formats DateTime values as HTML.
/// Replace this with your own formatter logic.
/// </summary>
[VersoExtension]
public sealed class SampleFormatter : IDataFormatter
{
    public string ExtensionId => "com.example.myextension.formatter";
    public string Name => "Sample Formatter";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "Formats DateTime values with a custom style.";

    public IReadOnlyList<Type> SupportedTypes => new[] { typeof(DateTime) };
    public int Priority => 0;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context) => value is DateTime;

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var dt = (DateTime)value;
        var html = $"<time datetime=\"{dt:O}\" style=\"font-weight:bold;\">{dt:F}</time>";
        return Task.FromResult(new CellOutput("text/html", html));
    }
}
