using System.Globalization;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The baselines a notebook can be compared against: what they are called, and what stays
/// the same whatever they are called.
/// </summary>
/// <remarks>
/// Both hosts read these names, and in the embedded shell the editor picks the baseline
/// while the notebook names it. The editor's menus follow the language its workbench is
/// set to and the notebook follows the language its interface is set to, so what crosses
/// between them has to be the id, never the name.
/// </remarks>
[TestClass]
public sealed class DiffSourcesTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-Ploc");

    private static void InPseudoLocale(Action assert)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Pseudo;
        try
        {
            assert();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [TestMethod]
    public void NameOf_KnowsTheFourBuiltInSources()
    {
        Assert.AreEqual(UI.Compare_SourceLastSaved, DiffSources.NameOf(DiffSources.LastSaved));
        Assert.AreEqual(UI.Compare_SourceGitHead, DiffSources.NameOf(DiffSources.GitHead));
        Assert.AreEqual(UI.Compare_SourceGitRef, DiffSources.NameOf(DiffSources.GitRef));
        Assert.AreEqual(UI.Compare_SourceChooseFile, DiffSources.NameOf(DiffSources.File));
    }

    [TestMethod]
    public void NameOf_LeavesAnythingElseUnnamed()
    {
        // A host may offer a baseline of its own. Nothing here knows what to call it, and
        // returning null says so rather than inventing a name for it.
        Assert.IsNull(DiffSources.NameOf("someOtherBaseline"));
        Assert.IsNull(DiffSources.NameOf(null));
    }

    [TestMethod]
    public void NameOf_FollowsTheCurrentLanguage()
    {
        var english = DiffSources.NameOf(DiffSources.LastSaved);

        InPseudoLocale(() => Assert.AreNotEqual(
            english, DiffSources.NameOf(DiffSources.LastSaved),
            "The name was resolved once and kept, so one reader's language would reach them all."));

        Assert.AreEqual(english, DiffSources.NameOf(DiffSources.LastSaved),
            "The name did not come back after the language did.");
    }

    [TestMethod]
    public void Ids_AreTheSameInEveryLanguage()
    {
        InPseudoLocale(() =>
        {
            Assert.AreEqual("lastSaved", DiffSources.LastSaved);
            Assert.IsNotNull(DiffSources.NameOf(DiffSources.GitHead));
        });
    }

    [TestMethod]
    public void ResolvedName_CarriesTheRefAndTheFileNameThrough()
    {
        // A branch name and a file name are the same in every language, which is why they
        // travel from wherever the baseline was picked instead of being chosen here.
        StringAssert.Contains(
            DiffSources.ResolvedName(DiffSources.GitRef, "release/2.0"), "release/2.0");
        Assert.AreEqual(
            "quarterly-report.verso",
            DiffSources.ResolvedName(DiffSources.File, "quarterly-report.verso"));
    }

    [TestMethod]
    public void ResolvedName_FallsBackToTheWordBaseline()
    {
        // Said of a comparison that ran against something this build cannot name, which is
        // still true of whatever it turned out to be.
        Assert.AreEqual(UI.Compare_Baseline, DiffSources.ResolvedName("someOtherBaseline", null));
        Assert.AreEqual(UI.Compare_Baseline, DiffSources.ResolvedName(DiffSources.File, "   "));
    }

    [TestMethod]
    public void UnavailableReason_ExplainsTheTwoItKnows()
    {
        Assert.AreEqual(UI.Compare_NotSavedYet, DiffSources.UnavailableReason(DiffSources.LastSaved));
        Assert.AreEqual(UI.Compare_NotInGitRepo, DiffSources.UnavailableReason(DiffSources.GitHead));
        Assert.AreEqual(UI.Compare_NotInGitRepo, DiffSources.UnavailableReason(DiffSources.GitRef));

        // Choosing a file always works, so there is nothing to explain.
        Assert.IsNull(DiffSources.UnavailableReason(DiffSources.File));
        Assert.IsNull(DiffSources.UnavailableReason("someOtherBaseline"));
    }
}
