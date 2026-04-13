using Verso.Abstractions;

namespace Verso.Display;

/// <summary>
/// Resolves formatters and writes display output for <see cref="DisplayExtensions.Display"/>.
/// Created per cell execution with the active formatter pipeline and output writer.
/// </summary>
internal sealed class DisplayHandler
{
    private readonly Func<CellOutput, Task> _writeOutput;
    private readonly IExtensionHostContext _extensionHost;
    private readonly IFormatterContext _defaultFormatterContext;

    public DisplayHandler(
        Func<CellOutput, Task> writeOutput,
        IExtensionHostContext extensionHost,
        IFormatterContext defaultFormatterContext)
    {
        _writeOutput = writeOutput;
        _extensionHost = extensionHost;
        _defaultFormatterContext = defaultFormatterContext;
    }

    public async Task DisplayAsync(object value, string? mimeTypeHint)
    {
        // If the value is already a CellOutput, write it directly
        if (value is CellOutput cellOutput)
        {
            await _writeOutput(cellOutput).ConfigureAwait(false);
            return;
        }

        // If the caller provided a MIME hint and the value is already a string,
        // honor it directly — bypasses the formatter pipeline which would emit text/plain.
        if (mimeTypeHint is not null && value is string rawString)
        {
            await _writeOutput(new CellOutput(mimeTypeHint, rawString)).ConfigureAwait(false);
            return;
        }

        // Build a formatter context, applying the MIME hint if provided
        var formatterContext = mimeTypeHint is not null
            ? new HintedFormatterContext(_defaultFormatterContext, mimeTypeHint)
            : _defaultFormatterContext;

        // Try the formatter pipeline
        var formatters = _extensionHost.GetFormatters();
        if (formatters.Count > 0)
        {
            foreach (var formatter in formatters.OrderByDescending(f => f.Priority))
            {
                if (formatter.SupportedTypes.Any(t => t.IsInstanceOfType(value))
                    && formatter.CanFormat(value, formatterContext))
                {
                    var output = await formatter.FormatAsync(value, formatterContext).ConfigureAwait(false);
                    await _writeOutput(output).ConfigureAwait(false);
                    return;
                }
            }
        }

        // Handle specific MIME hint formats before falling back to ToString
        if (mimeTypeHint is not null)
        {
            var hintOutput = TryFormatWithHint(value, mimeTypeHint);
            if (hintOutput is not null)
            {
                await _writeOutput(hintOutput).ConfigureAwait(false);
                return;
            }
        }

        // Fallback: plain text
        await _writeOutput(new CellOutput("text/plain", value.ToString() ?? ""))
            .ConfigureAwait(false);
    }

    private static CellOutput? TryFormatWithHint(object value, string mimeType)
    {
        return mimeType switch
        {
            "application/json" => TryJsonFormat(value),
            _ => null
        };
    }

    private static CellOutput? TryJsonFormat(object value)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return new CellOutput("application/json", json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Wraps an existing <see cref="IFormatterContext"/> with an overridden MIME type hint.
    /// </summary>
    private sealed class HintedFormatterContext : IFormatterContext
    {
        private readonly IFormatterContext _inner;
        public HintedFormatterContext(IFormatterContext inner, string mimeType)
        {
            _inner = inner;
            MimeType = mimeType;
        }

        public string MimeType { get; }
        public double MaxWidth => _inner.MaxWidth;
        public double MaxHeight => _inner.MaxHeight;
        public IVariableStore Variables => _inner.Variables;
        public CancellationToken CancellationToken => _inner.CancellationToken;
        public Task WriteOutputAsync(CellOutput output) => _inner.WriteOutputAsync(output);
        public IThemeContext Theme => _inner.Theme;
        public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
        public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
        public INotebookOperations Notebook => _inner.Notebook;
    }
}
