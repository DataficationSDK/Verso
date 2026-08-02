using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class PanelDisplayNamesTests
{
    [TestMethod]
    public void For_Properties_SaysWhichProperties()
    {
        // The toggle reads "Properties"; the header it opens has room to say more.
        Assert.AreEqual(UI.Panel_PropertiesHeading, PanelDisplayNames.For("properties"));
        Assert.AreNotEqual(UI.Panel_Properties, PanelDisplayNames.For("properties"));
    }

    [TestMethod]
    public void For_KnownPanels_ReturnsTheirNames()
    {
        Assert.AreEqual(UI.Panel_Metadata, PanelDisplayNames.For("metadata"));
        Assert.AreEqual(UI.Panel_Extensions, PanelDisplayNames.For("extensions"));
        Assert.AreEqual(UI.Panel_Variables, PanelDisplayNames.For("variables"));
        Assert.AreEqual(UI.Panel_Settings, PanelDisplayNames.For("settings"));
    }

    [TestMethod]
    public void For_UnknownPanel_FallsBackToTheId()
    {
        Assert.AreEqual("custom-panel", PanelDisplayNames.For("custom-panel"));
    }

    [TestMethod]
    public void For_LeavesCaseAlone()
    {
        // Headers are drawn in capitals by the stylesheet. Doing it here would apply one
        // language's case rules to every language's words.
        Assert.AreEqual("Metadata", PanelDisplayNames.For("metadata"));
    }
}
