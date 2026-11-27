using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public class HostPanelsTests
{
    [TestMethod]
    public void All_AreMarkedAsHostPanels()
    {
        foreach (var panel in HostPanels.All)
            Assert.IsTrue(panel.IsHostPanel, $"'{panel.PanelId}' should be a host panel.");
    }

    [TestMethod]
    public void All_HaveEmptyExtensionId()
    {
        // A host panel has no owning extension, which is what keeps its Key a bare id
        // and stops it colliding with an extension that happens to use the same one.
        foreach (var panel in HostPanels.All)
            Assert.AreEqual("", panel.ExtensionId);
    }

    [TestMethod]
    public void Orders_AreDistinctAndLeaveRoomBetween()
    {
        var orders = HostPanels.All.Select(p => p.Order).ToList();

        CollectionAssert.AllItemsAreUnique(orders);
        Assert.AreEqual(100, orders.Min());
        Assert.AreEqual(700, orders.Max());
    }

    [TestMethod]
    public void Available_WithoutPropertiesSupport_OmitsPropertiesPanel()
    {
        var available = HostPanels.Available(supportsPropertiesPanel: false, hasViewChoices: true, canDebug: true).ToList();

        Assert.IsFalse(available.Any(p => p.PanelId == HostPanels.Properties));
        Assert.AreEqual(HostPanels.All.Count - 1, available.Count);
    }

    [TestMethod]
    public void Available_WithoutDebugging_OmitsDebugPanel()
    {
        // A panel whose only control could never take effect would be a promise the notebook
        // cannot keep, so it is left off the rail until some language can pause a cell.
        var available = HostPanels.Available(supportsPropertiesPanel: true, hasViewChoices: true, canDebug: false).ToList();

        Assert.IsFalse(available.Any(p => p.PanelId == HostPanels.Debug));
        Assert.AreEqual(HostPanels.All.Count - 1, available.Count);
    }

    [TestMethod]
    public void Available_DebuggingDefaultsToOff()
    {
        // Callers written before debugging existed pass two arguments and must not grow a panel.
        var available = HostPanels.Available(supportsPropertiesPanel: true, hasViewChoices: true).ToList();

        Assert.IsFalse(available.Any(p => p.PanelId == HostPanels.Debug));
    }

    [TestMethod]
    public void Available_WithDebugging_OffersDebugPanel()
    {
        var available = HostPanels.Available(supportsPropertiesPanel: false, hasViewChoices: false, canDebug: true).ToList();

        Assert.IsTrue(available.Any(p => p.PanelId == HostPanels.Debug));
    }

    [TestMethod]
    public void DebugPanel_SitsBetweenVariablesAndSettings()
    {
        var debug = HostPanels.Find(HostPanels.Debug)!;

        Assert.AreEqual("bug", debug.IconName);
        Assert.IsTrue(HostPanels.Find("variables")!.Order < debug.Order);
        Assert.IsTrue(debug.Order < HostPanels.Find("settings")!.Order);
    }

    [TestMethod]
    public void CanDebug_NeedsALanguageThatSupportsIt()
    {
        Assert.IsFalse(HostPanels.CanDebug(Array.Empty<KernelLanguageInfo>()));
        Assert.IsFalse(HostPanels.CanDebug(new[]
        {
            new KernelLanguageInfo("fsharp", "F#"),
            new KernelLanguageInfo("powershell", "PowerShell")
        }));
        Assert.IsTrue(HostPanels.CanDebug(new[]
        {
            new KernelLanguageInfo("fsharp", "F#"),
            new KernelLanguageInfo("python", "Python", SupportsDebugging: true)
        }));
    }

    [TestMethod]
    public void Available_WithoutViewChoices_OmitsViewPanel()
    {
        // One layout and no theme choice leaves the view panel with nothing to offer, so
        // it is not shown at all rather than shown empty.
        var available = HostPanels.Available(supportsPropertiesPanel: true, hasViewChoices: false).ToList();

        Assert.IsFalse(available.Any(p => p.PanelId == HostPanels.View));
        Assert.IsTrue(available.Any(p => p.PanelId == HostPanels.Compare),
            "Compare is always offered: a baseline can be chosen from disk whatever else is true.");
    }

    [TestMethod]
    public void Available_WithEverything_IncludesEverything()
    {
        var available = HostPanels.Available(supportsPropertiesPanel: true, hasViewChoices: true, canDebug: true).ToList();

        Assert.AreEqual(HostPanels.All.Count, available.Count);
        Assert.IsTrue(available.Any(p => p.PanelId == HostPanels.Properties));
        Assert.IsTrue(available.Any(p => p.PanelId == HostPanels.View));
        Assert.IsTrue(available.Any(p => p.PanelId == HostPanels.Debug));
    }

    [TestMethod]
    public void HasViewChoices_NeedsMoreThanOneOfSomething()
    {
        Assert.IsFalse(HostPanels.HasViewChoices(layoutCount: 1, themeCount: 1, isEmbedded: false));
        Assert.IsTrue(HostPanels.HasViewChoices(layoutCount: 3, themeCount: 1, isEmbedded: false));
        Assert.IsTrue(HostPanels.HasViewChoices(layoutCount: 1, themeCount: 3, isEmbedded: false));
    }

    [TestMethod]
    public void HasViewChoices_EmbeddedHost_IgnoresThemes()
    {
        // Themes come from the host in the embedded shell, so a theme list there is not a
        // reason to offer a panel that would only be able to show it.
        Assert.IsFalse(HostPanels.HasViewChoices(layoutCount: 1, themeCount: 5, isEmbedded: true));
        Assert.IsTrue(HostPanels.HasViewChoices(layoutCount: 2, themeCount: 5, isEmbedded: true));
    }

    [TestMethod]
    public void Find_IsCaseInsensitive()
    {
        Assert.IsNotNull(HostPanels.Find("VARIABLES"));
        Assert.IsNull(HostPanels.Find("com.example.findings"));
    }

    [TestMethod]
    public void Key_DistinguishesHostFromExtensionPanels()
    {
        var host = HostPanels.Find("settings")!;
        var extension = new NotebookPanelInfo(
            "settings", "com.example.other", "Settings", null, null, 600, IsHostPanel: false);

        Assert.AreEqual("settings", host.Key);
        Assert.AreEqual("com.example.other::settings", extension.Key);
        Assert.AreNotEqual(host.Key, extension.Key);
    }
}
