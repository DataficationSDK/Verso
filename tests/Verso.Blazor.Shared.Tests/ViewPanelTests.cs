using Bunit;
using Microsoft.AspNetCore.Components;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;
using Verso.Blazor.Shared.Tests.Fakes;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The layout and theme lists that used to be a toolbar dropdown. What the panel adds over
/// the menu is that it stays open across selections, so the tests care about what the rows
/// report after a switch as much as what a click does.
/// </summary>
[TestClass]
public sealed class ViewPanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void SetUp()
    {
        _service = new FakeNotebookService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(_service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;

        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false),
            new("presentation", "Presentation", true, LayoutCapabilities.None),
        };
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("verso-light", "Verso Light", ThemeKind.Light),
            new("verso-dark", "Verso Dark", ThemeKind.Dark),
        };
    }

    private IRenderedComponent<ViewPanel> Render(EventCallback<string> onActionExecuted = default)
        => TestContext!.RenderComponent<ViewPanel>(parameters => parameters
            .Add(p => p.Service, _service)
            .Add(p => p.OnActionExecuted, onActionExecuted));

    [TestMethod]
    public void ActiveLayout_IsMarked()
    {
        var cut = Render();

        var active = cut.FindAll(".verso-panel-row--active");
        Assert.IsTrue(active.Any(r => r.TextContent.Contains("Notebook")));
        Assert.IsFalse(active.Any(r => r.TextContent.Contains("Presentation")));
    }

    [TestMethod]
    public void ReadOnlyLayout_SaysSo()
    {
        // A layout with no CellEdit capability shows the notebook rather than letting you
        // work in it, which surprises people who switch and find they cannot type.
        var cut = Render();

        var presentation = cut.FindAll(".verso-panel-row")
            .First(r => r.TextContent.Contains("Presentation"));

        StringAssert.Contains(presentation.TextContent, "Read only");
    }

    [TestMethod]
    public void ClickingALayout_SwitchesAndReportsTheAction()
    {
        string? action = null;
        var cut = Render(EventCallback.Factory.Create<string>(this, a => action = a));

        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Presentation")).Click();

        Assert.AreEqual("presentation", _service.ActiveLayoutId);
        Assert.AreEqual("verso.switchLayout", action,
            "The page and the toolbar both key layout-gated state off this.");
    }

    [TestMethod]
    public void ClickingALayout_MovesTheActiveMarkWithoutClosingAnything()
    {
        var cut = Render();

        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Presentation")).Click();

        var active = cut.FindAll(".verso-panel-row--active");
        Assert.IsTrue(active.Any(r => r.TextContent.Contains("Presentation")));
        Assert.AreEqual(2, cut.FindAll(".verso-panel-section").Count,
            "Both sections stay on screen after a selection; that is the point of the panel.");
    }

    [TestMethod]
    public void ClickingATheme_Switches()
    {
        string? action = null;
        var cut = Render(EventCallback.Factory.Create<string>(this, a => action = a));

        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Verso Dark")).Click();

        Assert.AreEqual("verso-dark", _service.ActiveThemeId);
        Assert.AreEqual("verso.switchTheme", action);
    }

    [TestMethod]
    public void EmbeddedHost_OffersLayoutsWithoutThemes()
    {
        // Themes come from the host in the embedded shell, so offering a theme list there
        // would be offering a choice this host does not own.
        _service.IsEmbedded = true;

        var cut = Render();

        Assert.AreEqual(1, cut.FindAll(".verso-panel-section").Count);
        Assert.IsFalse(cut.Markup.Contains("Verso Dark"));
    }
}
