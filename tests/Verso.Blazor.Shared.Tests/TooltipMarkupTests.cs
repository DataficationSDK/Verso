using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The contract between a control and the shared tooltip: a name it always carries,
/// a description only when it has one, and never both mechanisms at once.
/// </summary>
/// <remarks>
/// Whether a tooltip actually appears, and how quickly, is decided in
/// <c>js/tooltip.js</c> from the rendered attributes. These tests cover what the
/// components put there, which is the half that can regress silently.
/// </remarks>
[TestClass]
public sealed class TooltipMarkupTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true, IsEmbedded = false };
        TestContext!.Services.AddSingleton<INotebookService>(_service);
    }

    private IRenderedComponent<Toolbar> RenderToolbar()
        => RenderComponent<Toolbar>(p => p.Add(t => t.Service, _service));

    private void AddIconOnlyAction(string name, string? description)
    {
        _service.ToolbarActions.Add(new ToolbarActionInfo(
            "ext.action", name, "<svg viewBox=\"0 0 16 16\"></svg>",
            ToolbarPlacement.MainToolbar, 10,
            IconOnly: true,
            Description: description));
        _service.ActionEnabledStates["ext.action"] = true;
    }

    // ── The two attributes ─────────────────────────────────────────────

    [TestMethod]
    public void ActionWithADescription_CarriesBothLines()
    {
        AddIconOnlyAction("Sparkline", "Contributed by the Sparkline extension.");

        var button = RenderToolbar().Find("button[data-verso-tip='Sparkline']");

        Assert.AreEqual("Contributed by the Sparkline extension.",
            button.GetAttribute("data-verso-tip-desc"));
    }

    [TestMethod]
    public void ActionWithoutADescription_OmitsTheSecondLine()
    {
        // Absent rather than empty: a tooltip padded out with a blank line is worse
        // than a one-liner, and the renderer drops null-valued attributes entirely.
        AddIconOnlyAction("Format Document", null);

        var button = RenderToolbar().Find("button[data-verso-tip='Format Document']");

        Assert.IsFalse(button.HasAttribute("data-verso-tip-desc"));
    }

    [TestMethod]
    public void IconOnlyAction_StillCarriesAnAccessibleName()
    {
        // The tooltip is decoration. Removing title took the accessible name with it
        // unless the control names itself, and an icon-only button has no text to
        // fall back on.
        AddIconOnlyAction("Format Document", null);

        var button = RenderToolbar().Find("button[data-verso-tip='Format Document']");

        Assert.AreEqual("Format Document", button.GetAttribute("aria-label"));
    }

    // ── Never both mechanisms ──────────────────────────────────────────

    [TestMethod]
    public void Toolbar_UsesNoTitleAttributes()
    {
        // Leaving title in place means the browser's own tooltip still fires a
        // second later, on top of ours.
        AddIconOnlyAction("Format Document", "Reformat every code cell.");

        StringAssert.DoesNotMatch(RenderToolbar().Markup, new Regex(@"\stitle="));
    }

    [TestMethod]
    public void Cell_UsesNoTitleAttributes()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        var cell = new CellModel { Type = "code", Language = "csharp", Source = "1 + 1" };
        var cut = RenderComponent<Cell>(p => p
            .Add(c => c.CellData, cell)
            .Add(c => c.Index, 1)
            .Add(c => c.IsSelected, true));

        StringAssert.DoesNotMatch(cut.Markup, new Regex(@"\stitle="));
    }

    // ── The toolbar is one group ───────────────────────────────────────

    [TestMethod]
    public void Toolbar_IsASingleTooltipGroup()
    {
        // The warm-up is per group, and to the user the actions and the panel pills
        // are one row. Splitting them would make every crossing wait again.
        var cut = RenderToolbar();

        Assert.AreEqual(1, cut.FindAll(".verso-toolbar[data-verso-tip-group]").Count);
    }

    // ── Nothing worth saying, nothing said ─────────────────────────────

    [TestMethod]
    public void DocInfo_SaysNothing_WhenTheNameFitsAndOneKernelIsInUse()
    {
        // The cluster already prints the file name and the kernel. Repeating them on
        // hover is the tooltip equivalent of shouting.
        _service.FilePath = "/notes/short.vnb";

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Find(".verso-doc-info").HasAttribute("data-verso-tip"));
    }

    [TestMethod]
    public void DocInfo_OffersTheFullName_WhenTheLabelTruncatesIt()
    {
        _service.FilePath = "/notes/a-notebook-with-a-very-long-file-name.vnb";

        var info = RenderToolbar().Find(".verso-doc-info");

        Assert.AreEqual("a-notebook-with-a-very-long-file-name.vnb",
            info.GetAttribute("data-verso-tip"));
    }

    [TestMethod]
    public void DocInfo_NamesEveryKernel_WhenTheNotebookIsPolyglot()
    {
        _service.FilePath = "/notes/short.vnb";
        _service.RegisteredLanguages = new List<KernelLanguageInfo>
        {
            new("csharp", "C#"),
            new("python", "Python"),
        };
        _service.Cells = new List<CellModel>
        {
            new() { Type = "code", Language = "csharp" },
            new() { Type = "code", Language = "python" },
        };

        var info = RenderToolbar().Find(".verso-doc-info");

        var detail = info.GetAttribute("data-verso-tip-desc");
        StringAssert.Contains(detail!, "C#");
        StringAssert.Contains(detail!, "Python");
    }

    [TestMethod]
    public void SaveButton_ExplainsItself_OnlyWhileDirty()
    {
        _service.IsDirty = false;
        Assert.IsFalse(RenderToolbar().Find("button[data-verso-tip='Save']")
            .HasAttribute("data-verso-tip-desc"),
            "A clean Save button would only repeat the word already on it.");

        _service.IsDirty = true;
        Assert.IsTrue(RenderToolbar().Find("button[data-verso-tip='Save']")
            .HasAttribute("data-verso-tip-desc"));
    }

    // ── Host panels ────────────────────────────────────────────────────

    [TestMethod]
    public void EveryHostPanel_ExplainsWhatItShows()
    {
        // A panel toggle is an icon at rest, so its tooltip is usually the only
        // chance to explain the panel before someone decides to open it.
        foreach (var panel in HostPanels.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(panel.Description),
                $"Host panel '{panel.PanelId}' has no description.");
            Assert.IsFalse(
                string.Equals(panel.DisplayName, panel.Description, StringComparison.OrdinalIgnoreCase),
                $"Host panel '{panel.PanelId}' only restates its name.");
        }
    }

    [TestMethod]
    public void PanelToggle_CarriesTheNameAndTheDescription()
    {
        var compare = HostPanels.Find(HostPanels.Compare)!;
        _service.Panels.Add(compare);

        var cut = RenderComponent<Toolbar>(p => p
            .Add(t => t.Service, _service)
            .Add(t => t.Panels, _service.Panels)
            .Add(t => t.OnTogglePanel, (string _) => { }));

        var pill = cut.Find($".verso-toolbar-toggle[data-verso-tip='{compare.DisplayName}']");
        Assert.AreEqual(compare.Description, pill.GetAttribute("data-verso-tip-desc"));
        Assert.AreEqual(compare.DisplayName, pill.GetAttribute("aria-label"));
    }
}
