using System.Globalization;
using Verso.Abstractions;
using Verso.Showcase.SlideStudio;
using Verso.Testing.Stubs;

namespace Verso.Showcase.SlideStudio.Tests;

/// <summary>
/// The layout answers in whatever language is current when it is asked, rather than in
/// whichever one happened to be current the first time.
/// </summary>
/// <remarks>
/// A server draws notebooks for several readers from one process, and each of them may have
/// asked for a different language. Anything that resolves a string once and keeps it would
/// serve the first reader's language to everybody after them. The pseudo-locale is used
/// because it exists whether or not a translator has reached these strings yet.
/// </remarks>
[TestClass]
public sealed class SlideStudioCultureTests
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
    public void DisplayName_FollowsTheCurrentLanguage()
    {
        var layout = new SlideStudioLayout();
        var english = layout.DisplayName;

        InPseudoLocale(() => Assert.AreNotEqual(english, layout.DisplayName,
            "The layout picker label resolved once and was kept."));

        Assert.AreEqual(english, layout.DisplayName, "The label did not come back after the language did.");
    }

    [TestMethod]
    public async Task RenderedToolbar_FollowsTheCurrentLanguage()
    {
        var layout = new SlideStudioLayout();
        var context = new StubVersoContext();
        var cells = new List<CellModel> { new() { Type = "code", Source = "1 + 1" } };

        var english = (await layout.RenderLayoutAsync(cells, context)).Content;
        StringAssert.Contains(english, ">Present</button>");

        string pseudo = "";
        InPseudoLocale(() => pseudo = layout.RenderLayoutAsync(cells, context).GetAwaiter().GetResult().Content);

        Assert.IsFalse(pseudo.Contains(">Present</button>", StringComparison.Ordinal),
            "The toolbar was drawn in English while another language was current.");
        Assert.IsFalse(pseudo.Contains("1 slide<", StringComparison.Ordinal),
            "The slide count was drawn in English while another language was current.");
    }

    [TestMethod]
    public async Task PropertiesSection_FollowsTheCurrentLanguage()
    {
        var layout = new SlideStudioLayout();
        var cell = new CellModel { Type = "code" };
        var context = new StubCellRenderContext();

        var english = (await layout.GetPropertiesSectionAsync(cell, context)).Title;

        string pseudo = "";
        InPseudoLocale(() => pseudo = layout.GetPropertiesSectionAsync(cell, context).GetAwaiter().GetResult().Title);

        Assert.AreNotEqual(english, pseudo, "The properties section title resolved once and was kept.");
    }
}
