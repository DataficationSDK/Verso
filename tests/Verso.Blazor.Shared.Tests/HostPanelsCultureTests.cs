using System.Globalization;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The built-in panel list is written in whatever language is current when it is asked for.
/// </summary>
/// <remarks>
/// The list used to be a <c>static readonly</c> field, which is built once, the first time
/// anything touches it. On a server that is whoever opened a notebook first, and every reader
/// after them would have seen the panel rail in that person's language.
/// </remarks>
[TestClass]
public sealed class HostPanelsCultureTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-Ploc");

    [TestMethod]
    public void All_FollowsTheCurrentLanguage()
    {
        var english = HostPanels.All.Select(p => p.DisplayName).ToList();

        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Pseudo;
        List<string> pseudo;
        try
        {
            pseudo = HostPanels.All.Select(p => p.DisplayName).ToList();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        CollectionAssert.AreNotEqual(english, pseudo,
            "The panel list was built once and kept, so it holds whichever language touched it first.");
        CollectionAssert.AreEqual(
            english, HostPanels.All.Select(p => p.DisplayName).ToList(),
            "The names did not come back after the language did.");
    }

    [TestMethod]
    public void Ids_AreTheSameInEveryLanguage()
    {
        var english = HostPanels.All.Select(p => p.PanelId).ToList();

        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Pseudo;
        try
        {
            CollectionAssert.AreEqual(
                english, HostPanels.All.Select(p => p.PanelId).ToList(),
                "Panel ids are stored and compared against, so translating one would break the lookup.");
            Assert.IsNotNull(HostPanels.Find(HostPanels.Compare));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
