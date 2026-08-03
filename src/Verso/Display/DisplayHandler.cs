using Verso.Abstractions;

namespace Verso.Display;

/// <summary>
/// Resolves formatters and writes display output for <see cref="DisplayExtensions.Display"/>.
/// Created per cell execution with the active formatter pipeline and output writer.
/// </summary>
internal sealed class DisplayHandler
{
    private readonly Func<CellOutput, Task> _writeOutput;
    private readonly IFormatterContext _defaultFormatterContext;

    public DisplayHandler(
        Func<CellOutput, Task> writeOutput,
        IFormatterContext defaultFormatterContext)
    {
        _writeOutput = writeOutput;
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

        // Explicit Display uses the complete pipeline, including generic fallbacks.
        var formatted = await FormatterResolver.TryFormatAsync(
            value,
            formatterContext,
            includeFallback: true).ConfigureAwait(false);
        if (formatted is not null)
        {
            await _writeOutput(formatted).ConfigureAwait(false);
            return;
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
}
