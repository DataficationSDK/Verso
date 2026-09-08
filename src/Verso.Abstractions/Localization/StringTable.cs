using System.Collections;
using System.Globalization;
using System.Resources;

namespace Verso.Abstractions;

/// <summary>
/// Reads every string in a resource set for one language, so an extension can hand its
/// translations to something that cannot reach a <see cref="ResourceManager"/> itself.
/// </summary>
/// <remarks>
/// An isolated layout draws its interface inside a frame, from a script the extension wrote,
/// and that script has no way to ask a .NET resource for a string. The extension can, though:
/// it builds the table on the server for the language the host is answering in and passes it
/// to the frame with the rest of the initial state.
/// <para>
/// Each key falls back the way a generated <c>Strings</c> property would, through the parent
/// culture to the neutral language, so a string a translator has not reached yet arrives in
/// English rather than going missing.
/// </para>
/// </remarks>
public static class StringTable
{
    /// <summary>Resolves every string the neutral resource set defines, for one language.</summary>
    /// <param name="manager">The resource manager, typically the generated <c>Strings.ResourceManager</c>.</param>
    /// <param name="culture">The language to resolve for, typically <see cref="IVersoContext.UICulture"/>.</param>
    /// <returns>Every key with its resolved value.</returns>
    public static IReadOnlyDictionary<string, string> From(ResourceManager manager, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(culture);

        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        var neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        if (neutral is null)
            return table;

        foreach (DictionaryEntry entry in neutral)
        {
            if (entry.Key is not string key || entry.Value is not string fallback)
                continue;
            table[key] = manager.GetString(key, culture) ?? fallback;
        }

        return table;
    }
}
