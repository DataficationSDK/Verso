using System.Globalization;

namespace Verso.Cli.Utilities;

/// <summary>
/// Measures and pads text by the number of terminal columns it occupies rather than the number
/// of characters it holds.
/// </summary>
/// <remarks>
/// <para>
/// A terminal draws most characters one column wide, but the characters used by Chinese,
/// Japanese and Korean are drawn two columns wide. <see cref="string.Length"/> counts UTF-16
/// units, so <c>"概要"</c> measures 2 and occupies 4. Padding a column to a fixed width with
/// <see cref="string.PadRight(int)"/> therefore overshoots by one column for every wide
/// character in it, and a table that lines up in English comes out ragged in Japanese.
/// </para>
/// <para>
/// The ranges below are the wide and fullwidth blocks of Unicode's East Asian Width property.
/// They are written out rather than read from ICU because <c>CharUnicodeInfo</c> does not expose
/// that property, and a table this small is easier to check than a dependency.
/// </para>
/// </remarks>
public static class DisplayWidth
{
    /// <summary>Number of terminal columns <paramref name="text"/> occupies.</summary>
    public static int Measure(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var columns = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
            {
                columns += IsWide(char.ConvertToUtf32(text[i], text[i + 1])) ? 2 : 1;
                i++;
                continue;
            }

            // A combining mark sits on the character before it and takes no column of its own.
            if (CharUnicodeInfo.GetUnicodeCategory(text[i]) == UnicodeCategory.NonSpacingMark)
                continue;

            columns += IsWide(text[i]) ? 2 : 1;
        }

        return columns;
    }

    /// <summary>
    /// Pads <paramref name="text"/> with spaces until it occupies <paramref name="columns"/>
    /// terminal columns. Text already that wide is returned unchanged, as
    /// <see cref="string.PadRight(int)"/> does.
    /// </summary>
    public static string PadRight(string? text, int columns)
    {
        var value = text ?? string.Empty;
        var padding = columns - Measure(value);
        return padding > 0 ? value + new string(' ', padding) : value;
    }

    private static bool IsWide(int codePoint) => codePoint switch
    {
        >= 0x1100 and <= 0x115F => true,    // Hangul Jamo initial consonants
        >= 0x2E80 and <= 0x303E => true,    // CJK radicals, Kangxi radicals, CJK symbols
        >= 0x3041 and <= 0x33FF => true,    // Hiragana, Katakana, Hangul Compatibility Jamo, CJK compatibility
        >= 0x3400 and <= 0x4DBF => true,    // CJK unified ideographs extension A
        >= 0x4E00 and <= 0x9FFF => true,    // CJK unified ideographs
        >= 0xA000 and <= 0xA4CF => true,    // Yi syllables and radicals
        >= 0xAC00 and <= 0xD7A3 => true,    // Hangul syllables
        >= 0xF900 and <= 0xFAFF => true,    // CJK compatibility ideographs
        >= 0xFE10 and <= 0xFE19 => true,    // Vertical forms
        >= 0xFE30 and <= 0xFE6F => true,    // CJK compatibility forms, small form variants
        >= 0xFF00 and <= 0xFF60 => true,    // Fullwidth ASCII
        >= 0xFFE0 and <= 0xFFE6 => true,    // Fullwidth currency and other signs
        >= 0x1F300 and <= 0x1F64F => true,  // Emoji and pictographs
        >= 0x1F900 and <= 0x1F9FF => true,  // Supplemental symbols and pictographs
        >= 0x20000 and <= 0x3FFFD => true,  // CJK unified ideographs extensions B and beyond
        _ => false,
    };
}
