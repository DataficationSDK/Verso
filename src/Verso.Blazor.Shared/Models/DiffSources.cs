using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Models;

/// <summary>
/// The baselines every host can compare a notebook against, and the words for them.
/// </summary>
/// <remarks>
/// Both hosts offer the same four, and both used to spell out the same four names. The
/// server reads the file and runs git itself; the embedded shell asks the editor to do
/// both, because only the editor has the pickers and the repository. What they have in
/// common is exactly this: an id, a name, and a reason it might not be usable.
/// <para>
/// The names live here rather than travelling with the answer the editor sends back. An
/// editor writes its menus in the language its workbench is set to, and a notebook is
/// written in the language its interface is set to, which are two choices a reader is
/// allowed to make differently. The ids are what crosses between them, and they are the
/// same in every language.
/// </para>
/// </remarks>
public static class DiffSources
{
    /// <summary>The copy of the notebook currently on disk.</summary>
    public const string LastSaved = "lastSaved";

    /// <summary>The copy at the commit version control currently has checked out.</summary>
    public const string GitHead = "gitHead";

    /// <summary>The copy at a branch, tag, or commit chosen when the comparison runs.</summary>
    public const string GitRef = "gitRef";

    /// <summary>Another notebook, chosen from disk when the comparison runs.</summary>
    public const string File = "file";

    /// <summary>
    /// What to call a baseline, or <see langword="null"/> when it is not one of the four
    /// and only whoever offered it can name it.
    /// </summary>
    /// <remarks>
    /// Resolved on each call rather than held in a table, so a server drawing notebooks
    /// for several readers answers each of them in the language they asked for.
    /// </remarks>
    public static string? NameOf(string? sourceId) => sourceId switch
    {
        LastSaved => UI.Compare_SourceLastSaved,
        GitHead => UI.Compare_SourceGitHead,
        GitRef => UI.Compare_SourceGitRef,
        File => UI.Compare_SourceChooseFile,
        _ => null,
    };

    /// <summary>
    /// Why a baseline cannot be compared against, for the two reasons that apply to the
    /// four built-in sources, or <see langword="null"/> when neither does.
    /// </summary>
    public static string? UnavailableReason(string? sourceId) => sourceId switch
    {
        LastSaved => UI.Compare_NotSavedYet,
        GitHead or GitRef => UI.Compare_NotInGitRepo,
        _ => null,
    };

    /// <summary>
    /// Names the baseline a comparison actually ran against, for the heading over the
    /// change list.
    /// </summary>
    /// <param name="sourceId">Which baseline was used.</param>
    /// <param name="argument">
    /// The part of the name no language can change: the branch, tag, or commit that was
    /// chosen, or the name of the file that was. Ignored by the other sources.
    /// </param>
    public static string ResolvedName(string? sourceId, string? argument) => sourceId switch
    {
        GitRef => string.Format(UI.Compare_GitRefLabel, argument ?? ""),
        File => string.IsNullOrWhiteSpace(argument) ? UI.Compare_Baseline : argument,
        _ => NameOf(sourceId) ?? UI.Compare_Baseline,
    };
}
