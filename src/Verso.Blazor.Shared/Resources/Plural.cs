namespace Verso.Blazor.Shared.Resources;

/// <summary>
/// Chooses between the two forms of a message that counts something.
/// </summary>
/// <remarks>
/// Two forms cover every language Verso ships in: German, Spanish, Japanese, and Chinese all
/// need at most a singular and a plural, and the last two need neither. A language with more
/// forms, such as Russian or Polish, does not fit here and would need a real plural selector
/// keyed on the count and the language together. Adding one of those means replacing this,
/// not adding a third key.
/// <para>
/// The alternative, building a word out of a stem and an <c>s</c>, does not survive translation
/// at all: the plural of a German noun is not its singular with a letter on the end.
/// </para>
/// </remarks>
internal static class Plural
{
    /// <summary>Picks the singular for exactly one, and the plural for anything else.</summary>
    /// <param name="count">How many things the message is about.</param>
    /// <param name="one">The message written for a single thing.</param>
    /// <param name="other">The message written for any other number of them.</param>
    public static string Of(int count, string one, string other) => count == 1 ? one : other;
}
