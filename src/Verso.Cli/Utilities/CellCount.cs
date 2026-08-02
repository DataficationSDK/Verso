using Verso.Abstractions;
using Verso.Cli.Resources;

namespace Verso.Cli.Utilities;

/// <summary>
/// Writes out how many cells something holds.
/// </summary>
/// <remarks>
/// The phrase is built here and dropped into the surrounding message as one piece, so a sentence
/// like "Saved three cells to notebook.verso" needs one entry rather than a singular and a plural
/// of the whole sentence. Where the count falls in that sentence is then the translator's to
/// decide, which it has to be: not every language puts it where English does.
/// </remarks>
internal static class CellCount
{
    /// <summary>Describes a count of cells in words the reader's language would use.</summary>
    public static string Describe(int count)
        => string.Format(Plural.Of(count, Strings.Repl_CellCount_One, Strings.Repl_CellCount_Other), count);
}
