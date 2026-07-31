using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public class PanelIconsTests
{
    [TestMethod]
    public void KnownName_ResolvesToGlyph()
    {
        var icon = PanelIcons.Resolve("list", iconMarkup: null);

        Assert.IsNotNull(icon);
        StringAssert.StartsWith(icon, "<svg");
    }

    [TestMethod]
    public void KnownName_IsCaseInsensitive()
    {
        Assert.AreEqual(
            PanelIcons.Resolve("list", null),
            PanelIcons.Resolve("LIST", null));
    }

    [TestMethod]
    public void UnknownName_FallsBackToMarkup()
    {
        // An unrecognized name is not an error. The whole point of naming an icon is
        // that a host which does not have it degrades instead of failing.
        var icon = PanelIcons.Resolve("no-such-icon", "<svg id=\"custom\"></svg>");

        Assert.AreEqual("<svg id=\"custom\"></svg>", icon);
    }

    [TestMethod]
    public void KnownName_WinsOverMarkup()
    {
        var icon = PanelIcons.Resolve("gear", "<svg id=\"custom\"></svg>");

        StringAssert.Contains(icon!, "circle");
        Assert.AreNotEqual("<svg id=\"custom\"></svg>", icon);
    }

    [TestMethod]
    public void NoNameAndNoMarkup_ResolvesToNull()
    {
        Assert.IsNull(PanelIcons.Resolve(null, null));
        Assert.IsNull(PanelIcons.Resolve("", "   "));
    }

    [TestMethod]
    public void InitialFor_UsesFirstLetterUppercased()
    {
        Assert.AreEqual("F", PanelIcons.InitialFor("findings"));
        Assert.AreEqual("C", PanelIcons.InitialFor("  Cells  "));
    }

    [TestMethod]
    public void InitialFor_EmptyName_ReturnsPlaceholder()
    {
        Assert.AreEqual("?", PanelIcons.InitialFor(""));
        Assert.AreEqual("?", PanelIcons.InitialFor("   "));
    }

    [TestMethod]
    public void EveryDocumentedName_IsDrawable()
    {
        // The documented set and the shipped set must not drift apart. Anything named
        // in the interface docs has to resolve here.
        string[] documented =
        {
            "document", "list", "puzzle", "braces", "gear", "search", "info",
            "warning", "flag", "check", "clock", "tag", "chart", "table",
            "folder", "link"
        };

        foreach (var name in documented)
        {
            Assert.IsNotNull(PanelIcons.Resolve(name, null),
                $"Documented icon name '{name}' does not resolve to a glyph.");
        }

        Assert.AreEqual(documented.Length, PanelIcons.KnownNames.Count,
            "PanelIcons ships a name that is not documented, or vice versa.");
    }

    [TestMethod]
    public void EveryHostPanelIcon_IsDrawable()
    {
        foreach (var panel in HostPanels.All)
        {
            Assert.IsNotNull(PanelIcons.Resolve(panel.IconName, panel.IconMarkup),
                $"Host panel '{panel.PanelId}' has no drawable icon.");
        }
    }
}
