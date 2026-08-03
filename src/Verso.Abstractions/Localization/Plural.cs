namespace Verso.Abstractions;

/// <summary>
/// Chooses between the two forms of a message that counts something.
/// </summary>
/// <remarks>
/// Two forms cover every language Verso ships in: German and Spanish need a singular and a
/// plural, and Japanese and Chinese need neither. A language with more forms, such as Russian
/// or Polish, does not fit here and would need a real plural selector keyed on the count and
/// the language together. Adding one of those means replacing this, not adding a third key.
/// <para>
/// The alternative, building a word out of a stem and an <c>s</c>, does not survive translation
/// at all: the plural of a German noun is not its singular with a letter on the end. Neither
/// does <c>cell(s)</c>, which no other language can copy.
/// </para>
/// <para>
/// It lives here, in the assembly every other one already references, because the notebook
/// interface, the engine, the command line, and each kernel all count something. An extension
/// writing its own messages can use it for the same reason.
/// </para>
/// </remarks>
public static class Plural
{
    /// <summary>Picks the singular for exactly one, and the plural for anything else.</summary>
    /// <param name="count">How many things the message is about.</param>
    /// <param name="one">The message written for a single thing.</param>
    /// <param name="other">The message written for any other number of them.</param>
    public static string Of(int count, string one, string other) => count == 1 ? one : other;
}
