using Verso.Abstractions;

namespace Verso.Display;

/// <summary>
/// Resolves runtime values through the registered formatter pipeline without writing output.
/// </summary>
internal static class FormatterResolver
{
    public static async Task<CellOutput?> TryFormatAsync(
        object value,
        IFormatterContext context,
        bool includeFallback,
        string? requiredMimeType = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);

        if (value is CellOutput output)
            return output;

        foreach (var formatter in context.ExtensionHost.GetFormatters()
                     .Where(formatter => includeFallback || !formatter.IsFallback)
                     .OrderByDescending(formatter => formatter.Priority))
        {
            if (!formatter.SupportedTypes.Any(type => type.IsInstanceOfType(value)))
                continue;

            bool canFormat;
            try
            {
                canFormat = formatter.CanFormat(value, context);
            }
            catch
            {
                // CanFormat is a probe. A broken formatter must not prevent another
                // formatter (or the kernel's native fallback) from running.
                continue;
            }

            if (canFormat)
            {
                var formattedOutput = await formatter.FormatAsync(value, context).ConfigureAwait(false);
                if (requiredMimeType is null ||
                    string.Equals(formattedOutput.MimeType, requiredMimeType, StringComparison.OrdinalIgnoreCase))
                {
                    return formattedOutput;
                }
            }
        }

        return null;
    }
}
