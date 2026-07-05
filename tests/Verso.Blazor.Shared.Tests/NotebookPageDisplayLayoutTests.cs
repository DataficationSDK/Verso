using Microsoft.AspNetCore.Components.Web;
using Verso.Blazor.Components.Pages;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Behavioural tests for how the notebook page treats cell selection when the active layout is a
/// read-only display surface (advertises no <see cref="LayoutCapabilities.CellEdit"/>, e.g. the
/// Presentation and Dashboard layouts) versus an editing layout. A render-only or
/// collapse-on-execute cell shows its output only while unselected; selecting it flips it into
/// edit mode and removes the output. In a display layout that output is the whole point, so
/// clicking it must not select the cell and make the output vanish.
/// </summary>
[TestClass]
public sealed class NotebookPageDisplayLayoutTests : BunitTestContext
{
    [TestMethod]
    public void DisplayLayout_ClickingRenderedOutput_KeepsOutputVisible()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        var service = CreatePreviewCellService(LayoutCapabilities.None);
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();
        Assert.IsTrue(cut.Markup.Contains(RenderedOutput),
            "The rendered output should be visible before any interaction.");

        cut.Find(".verso-cell-outputs").Click();

        Assert.IsTrue(cut.Markup.Contains(RenderedOutput),
            "Clicking a rendered output in a read-only display layout must not select the cell, "
            + "so the output stays visible instead of being replaced by a hidden editor.");
    }

    [TestMethod]
    public void EditingLayout_ClickingRenderedOutput_EntersEditMode()
    {
        // Contrast case: with CellEdit available, clicking a render-only cell's output selects it
        // and enters edit mode, so the rendered output gives way to the editor. This is the normal
        // notebook behaviour and confirms the gate above is what changes the outcome.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        var service = CreatePreviewCellService(LayoutCapabilities.CellEdit);
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();
        Assert.IsTrue(cut.Markup.Contains(RenderedOutput));

        cut.Find(".verso-cell-outputs").Click();

        Assert.IsFalse(cut.Markup.Contains(RenderedOutput),
            "In an editing layout, selecting a render-only cell replaces its output with the editor.");
    }

    private const string RenderedOutput = "<em>rendered-md</em>";

    private static FakeNotebookService CreatePreviewCellService(LayoutCapabilities capabilities)
    {
        // A markdown cell renders its source to output and collapses its input on execute, so while
        // it is unselected it shows the rendered output in preview mode (the clickable state).
        var cell = new CellModel { Type = "markdown", Source = "# Heading" };
        cell.Outputs.Add(new CellOutput("text/html", RenderedOutput));

        return new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { cell },
            LayoutCapabilities = capabilities,
            CollapseInputMap = new Dictionary<string, bool> { ["markdown"] = true },
        };
    }
}
