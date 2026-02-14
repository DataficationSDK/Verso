using System.Collections;
using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// Generates human-readable value previews for the variable explorer.
/// </summary>
public sealed class VariablePreviewService
{
    private readonly IExtensionHostContext _extensionHost;

    public VariablePreviewService(IExtensionHostContext extensionHost)
    {
        _extensionHost = extensionHost ?? throw new ArgumentNullException(nameof(extensionHost));
    }

    /// <summary>
    /// Returns a truncated string preview of a value, suitable for display in the variable explorer.
    /// </summary>
    public string GetPreview(object? value, int maxLength = 200)
    {
        if (value is null)
            return "null";

        try
        {
            var preview = value switch
            {
                string s => $"\"{s}\"",
                bool b => b.ToString().ToLowerInvariant(),
                char c => $"'{c}'",
                ICollection collection => $"{value.GetType().Name} (Count = {collection.Count})",
                IEnumerable enumerable when value.GetType() != typeof(string) => FormatEnumerable(enumerable),
                _ => value.ToString() ?? value.GetType().Name
            };

            if (preview.Length > maxLength)
                return preview.Substring(0, maxLength - 3) + "...";

            return preview;
        }
        catch
        {
            return $"<{value.GetType().Name}>";
        }
    }

    private static string FormatEnumerable(IEnumerable enumerable)
    {
        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
            if (count > 1000) break;
        }
        return $"{enumerable.GetType().Name} (Count = {count}{(count > 1000 ? "+" : "")})";
    }
}
