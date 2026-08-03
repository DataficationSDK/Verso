using Verso.Resources;

namespace Verso.Localization;

/// <summary>
/// Puts the standard prefix in front of a message written into a cell's output.
/// </summary>
/// <remarks>
/// The prefix is one entry rather than the first word of thirty, so a translator sets it once
/// and every message that carries it reads the same. It is a placeholder rather than something
/// glued on the front, because a language may not put it there at all.
/// </remarks>
internal static class CellText
{
    /// <summary>Marks a message about something a cell could not carry out.</summary>
    public static string Error(string message) => string.Format(Strings.Prefix_Error, message);

    /// <summary>Marks a message about something that worked but is worth knowing.</summary>
    public static string Warning(string message) => string.Format(Strings.Prefix_Warning, message);
}
