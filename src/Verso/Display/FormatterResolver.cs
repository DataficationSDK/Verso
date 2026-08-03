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
        IReadOnlyList<string>? acceptableMimeTypes = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);

        var formatters = context.ExtensionHost.GetFormatters()
            .Where(formatter => includeFallback || !formatter.IsFallback)
            .Where(formatter => formatter.SupportedTypes.Any(type => type.IsInstanceOfType(value)))
            .OrderByDescending(formatter => formatter.Priority)
            .ToArray();

        var candidateContexts = new List<(IFormatterContext Context, string? RequiredMimeType)>();
        if (acceptableMimeTypes is null)
        {
            candidateContexts.Add((context, null));
        }
        else
        {
            foreach (var mimeType in acceptableMimeTypes)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
                candidateContexts.Add((new HintedFormatterContext(context, mimeType), mimeType));
            }
        }

        var rejectedFormatters = new HashSet<IDataFormatter>();
        var candidates = new List<(IDataFormatter Formatter, IFormatterContext Context, string? RequiredMimeType)>();

        // Probe every hinted context before invoking a formatter. Candidate order is
        // MIME preference first, then formatter priority within that representation.
        foreach (var candidateContext in candidateContexts)
        {
            foreach (var formatter in formatters)
            {
                if (rejectedFormatters.Contains(formatter))
                    continue;

                try
                {
                    if (formatter.CanFormat(value, candidateContext.Context))
                    {
                        candidates.Add((
                            formatter,
                            candidateContext.Context,
                            candidateContext.RequiredMimeType));
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // CanFormat is a probe. A broken formatter must not prevent another
                    // formatter (or the kernel's native fallback) from running.
                    rejectedFormatters.Add(formatter);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (rejectedFormatters.Contains(candidate.Formatter))
                continue;

            CellOutput formattedOutput;
            try
            {
                formattedOutput = await candidate.Formatter
                    .FormatAsync(value, candidate.Context)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A formatter that fails after claiming a value is broken for this
                // resolution. Skip all of its other MIME candidates and keep looking.
                rejectedFormatters.Add(candidate.Formatter);
                continue;
            }

            if (candidate.RequiredMimeType is not null &&
                !string.Equals(
                    formattedOutput.MimeType,
                    candidate.RequiredMimeType,
                    StringComparison.OrdinalIgnoreCase))
            {
                // CanFormat must only claim MIME types the formatter can emit. Do not
                // run a contract-violating formatter again for the same value.
                rejectedFormatters.Add(candidate.Formatter);
                continue;
            }

            return formattedOutput;
        }

        return null;
    }
}
