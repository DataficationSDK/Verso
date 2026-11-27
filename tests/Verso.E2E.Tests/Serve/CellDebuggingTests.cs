using System.Text.Json;
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Verso.E2E.Tests.Infrastructure;

namespace Verso.E2E.Tests.Serve;

/// <summary>
/// Debugging a C# cell from the browser: the Debug panel turns the mode on, a click in the
/// editor's margin sets a breakpoint, a run stops there with its variables shown, and the
/// panel's buttons step into a function another cell defined and carry on to the end.
/// </summary>
[MotusTestClass]
public sealed class CellDebuggingTests : MotusTestBase
{
    // Long enough for a cold Roslyn compilation on a busy agent.
    private const int RunTimeoutMs = 90_000;
    private const int UiTimeoutMs = 15_000;

    private const string Defining = "int Square(int n)\n{\n    var result = n * n;\n    return result;\n}\n\nvar seed = 3;";
    private const string Calling = "var total = 0;\nfor (var i = 1; i <= 2; i++)\n{\n    total += Square(i + seed);\n}\ntotal";

    private static VersoServer s_server = null!;
    private ScratchDirectory _scratch = null!;

    [ClassInitialize]
    public static async Task StartServerAsync(TestContext _) => s_server = await VersoServer.StartAsync();

    [ClassCleanup]
    public static async Task StopServerAsync()
    {
        if (s_server is not null)
            await s_server.DisposeAsync();
    }

    [TestInitialize]
    public void CreateScratch() => _scratch = new ScratchDirectory();

    [TestCleanup]
    public void RemoveScratch() => _scratch.Dispose();

    [TestMethod]
    public async Task TurningDebuggingOn_ShowsTheBreakpointMargin_AndOffHidesIt()
    {
        await OpenAsync();

        Assert.AreEqual(0, await GlyphMarginWidthAsync(1), "The margin should not be there until debugging is on.");

        await TurnDebuggingOnAsync();
        await WaitForAsync("(" + EditorFor(1) + ").getLayoutInfo().glyphMarginWidth > 0", "the breakpoint margin to appear");

        // The toggle keeps saying the mode is on once its panel is closed.
        await DebugToggle.ClickAsync();
        await WaitForAsync("!document.querySelector('.verso-debug-panel')", "the Debug panel to close");
        Assert.AreEqual(1, await CountAsync(".verso-toolbar-toggle--on .verso-toolbar-toggle-dot"));

        await DebugToggle.ClickAsync();
        await Page.Locator(".verso-debug-panel button", new LocatorOptions { HasText = "Turn off debugging" }).ClickAsync();
        await WaitForAsync("(" + EditorFor(1) + ").getLayoutInfo().glyphMarginWidth === 0", "the breakpoint margin to go");
        Assert.AreEqual(0, await CountAsync(".verso-toolbar-toggle-dot"));
    }

    [TestMethod]
    public async Task Breakpoint_StopsTheCell_StepsIntoAnotherCell_AndRunsOn()
    {
        await OpenAsync();
        await TurnDebuggingOnAsync();

        // The defining cell runs with debugging on, which is what lets a later step go into it.
        await RunCellAsync(0);
        await WaitForCellStatusAsync(0, "success");

        await ClickMarginAsync(cellIndex: 1, line: 4);
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 2, line 4");

        await RunCellAsync(1);
        await WaitForTextAsync(".verso-debug-status", "Stopped at a breakpoint", RunTimeoutMs);
        await WaitForTextAsync(".verso-debug-status", "Cell 2, line 4");

        CollectionAssert.AreEquivalent(
            new[] { "i int 1", "total int 0", "seed int 3" },
            await VariablesAsync(),
            "The stop should list what is assigned so far, including what an earlier cell declared.");
        await WaitForAsync(
            "document.querySelectorAll('.verso-cell')[1].querySelectorAll('.verso-debug-stopped-line').length === 1",
            "the line the cell stopped at to be highlighted");

        // Into the function the line calls, which lives in the first cell.
        await ControlAsync("Step into");
        await WaitForTextAsync(".verso-debug-status", "Cell 1, line 3");
        await WaitForTextAsync(".verso-debug-status", "Stopped after a step");
        CollectionAssert.AreEqual(new[] { "n int 4" }, await VariablesAsync());
        CollectionAssert.AreEqual(
            new[] { "Square Cell 1, line 3", "Cell 2 Line 4" },
            await TextsAsync(".verso-debug-panel .verso-panel-row[aria-pressed]"),
            "The call stack should name the function first and the cell that called it second.");

        // Back out to the caller, which by then is on its second time round the loop.
        await ControlAsync("Step out");
        await WaitForTextAsync(".verso-debug-status", "Cell 2, line 4");
        CollectionAssert.Contains(await VariablesAsync(), "total int 16");

        // The breakpoint is the only thing left to stop at, and the loop has no more turns.
        await Page.Locator(".verso-debug-breakpoint-remove").ClickAsync();
        await WaitForAsync("document.querySelectorAll('.verso-debug-breakpoint-row').length === 0", "the breakpoint to be removed");

        await ControlAsync("Continue");
        await WaitForTextAsync(".verso-debug-status", "Waiting for a breakpoint", RunTimeoutMs);

        await WaitForCellStatusAsync(1, "success");
        await WaitForAsync(
            "document.querySelectorAll('.verso-cell')[1].innerText.includes('41')",
            "the cell to show its result once it had run on");
        Assert.AreEqual(0, await CountAsync(".verso-debug-stopped-line"), "Nothing should be highlighted once the cell has finished.");
    }

    [TestMethod]
    public async Task Stop_EndsACellHeldAtABreakpoint()
    {
        await OpenAsync();
        await TurnDebuggingOnAsync();

        await ClickMarginAsync(cellIndex: 0, line: 7);
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 1, line 7");

        await RunCellAsync(0);
        await WaitForTextAsync(".verso-debug-status", "Stopped at a breakpoint", RunTimeoutMs);

        await Page.Locator(".verso-debug-control--stop").ClickAsync();
        await WaitForTextAsync(".verso-debug-status", "Waiting for a breakpoint");
        await WaitForCellStatusAsync(0, "cancelled");

        // The same cell runs again afterwards, so the kernel was left in working order.
        await Page.Locator(".verso-debug-breakpoint-remove").ClickAsync();
        await WaitForAsync("document.querySelectorAll('.verso-debug-breakpoint-row').length === 0", "the breakpoint to be removed");
        await RunCellAsync(0);
        await WaitForCellStatusAsync(0, "success");
    }

    [TestMethod]
    public async Task RestartingTheKernel_EndsACellHeldAtABreakpoint_AndKeepsDebuggingAsItWas()
    {
        await OpenAsync();
        await TurnDebuggingOnAsync();

        await ClickMarginAsync(cellIndex: 0, line: 7);
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 1, line 7");

        await RunCellAsync(0);
        await WaitForTextAsync(".verso-debug-status", "Stopped at a breakpoint", RunTimeoutMs);

        await Page.Locator("button.verso-toolbar-btn[data-verso-tip='Restart Kernel']").ClickAsync();
        var confirm = Page.Locator("[role=dialog][aria-label='Restart Kernel'] .verso-modal-btn--primary");
        await confirm.WaitForAsync(ElementState.Visible, timeout: UiTimeoutMs);
        await confirm.ClickAsync();

        // The cell is let go rather than left waiting on a kernel that is no longer there, and
        // the mode and the breakpoint are still what the reader set.
        await WaitForTextAsync(".verso-debug-status", "Waiting for a breakpoint", RunTimeoutMs);
        await WaitForAsync(
            "!document.querySelector('.verso-cell--executing')",
            "the stopped cell to end",
            RunTimeoutMs);
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 1, line 7");

        // The kernel that comes back stops at the same place and runs on from it.
        await RunCellAsync(0);
        await WaitForTextAsync(".verso-debug-status", "Stopped at a breakpoint", RunTimeoutMs);
        await ControlAsync("Continue");
        await WaitForCellStatusAsync(0, "success");
    }

    [TestMethod]
    public async Task Breakpoints_OutliveTheirCellsTextBeingReplaced_AndAnAnswerThatIsOutOfDate()
    {
        await OpenAsync();
        await TurnDebuggingOnAsync();

        await ClickMarginAsync(cellIndex: 1, line: 4);
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 2, line 4");

        // The whole text replaced from outside the editor, which is how a reload or a tool that
        // rewrites the cell arrives. The editor drops every mark when that happens, and that
        // must not be read as the breakpoints having been taken off.
        await ReplaceTextAsync(1, Calling + "\n// and a note");
        await WaitForAsync(MarkedLines(1) + " === '4'", "the breakpoint to be put back on its line");
        Assert.AreEqual(1, await CountAsync(".verso-debug-breakpoint-row"));

        // A second breakpoint, followed at once by word from the notebook that there is only
        // the first: the answer to an earlier change, arriving after this one was made. It is
        // not what the breakpoints are now, and drawing it would take the new mark away.
        var marked = await Page.EvaluateAsync<string>(
            "(() => { const ed = " + EditorFor(1) + "; const node = ed.getContainerDomNode();"
            + " const box = node.getBoundingClientRect(); const info = ed.getLayoutInfo();"
            + " const lineHeight = ed.getOption(monaco.editor.EditorOption.lineHeight);"
            + " const x = box.left + info.glyphMarginLeft + info.glyphMarginWidth / 2;"
            + " const y = box.top + ed.getTopForLineNumber(2) - ed.getScrollTop() + lineHeight / 2;"
            + " const target = document.elementFromPoint(x, y) || node;"
            + " for (const type of ['pointerdown', 'mousedown', 'pointerup', 'mouseup']) {"
            + "   const Kind = type.startsWith('pointer') ? PointerEvent : MouseEvent;"
            + "   target.dispatchEvent(new Kind(type, { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y,"
            + "     button: 0, buttons: type.endsWith('down') ? 1 : 0, pointerId: 1, pointerType: 'mouse', isPrimary: true })); }"
            + " versoMonaco.setDebugState(node.id, { enabled: true, breakpoints: [4], stoppedLine: 0, callerLines: [] });"
            + " return " + MarkedLines(1) + "; })()");
        Assert.AreEqual("2,4", marked, "The new breakpoint should still be marked after the out-of-date answer.");
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 2, line 2");
        Assert.AreEqual(2, await CountAsync(".verso-debug-breakpoint-row"));

        // Text too short to have a fourth line: that breakpoint has nowhere to go and is given up.
        await ReplaceTextAsync(1, "var only = 1;\nonly");
        await WaitForAsync(MarkedLines(1) + " === '2'", "the breakpoint past the end to be dropped");
        await WaitForAsync("document.querySelectorAll('.verso-debug-breakpoint-row').length === 1", "the notebook to be told it was dropped");
        await WaitForTextAsync(".verso-debug-breakpoint-row", "Cell 2, line 2");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private ILocator DebugToggle => Page.Locator("button.verso-toolbar-toggle[aria-label='Debug']");

    private async Task OpenAsync()
    {
        var notebook = _scratch.Write("debugging.verso", Notebooks.VersoWithSources(Defining, Calling));
        await Page.GotoAsync(s_server.UrlFor(notebook));

        // The editors are attached once the interactive circuit is up.
        await Page.WaitForFunctionAsync<bool>(
            "document.querySelectorAll('.verso-cell .monaco-editor').length >= 2 && !!window.monaco",
            timeout: 60_000);
        await DebugToggle.WaitForAsync(ElementState.Visible, timeout: UiTimeoutMs);
    }

    private async Task TurnDebuggingOnAsync()
    {
        await DebugToggle.ClickAsync();
        await Page.Locator(".verso-debug-panel button", new LocatorOptions { HasText = "Turn on debugging" }).ClickAsync();
        await WaitForTextAsync(".verso-debug-status", "Waiting for a breakpoint");
    }

    // Finds the Monaco editor inside the n-th cell.
    private static string EditorFor(int cellIndex) =>
        "(() => { const cell = document.querySelectorAll('.verso-cell')[" + cellIndex + "];"
        + " return monaco.editor.getEditors().find(e => cell.contains(e.getContainerDomNode())); })()";

    // The lines of the n-th cell that carry a breakpoint mark, as "2,4".
    private static string MarkedLines(int cellIndex) =>
        "(" + EditorFor(cellIndex) + ").getModel().getAllDecorations()"
        + ".filter(d => d.options.glyphMarginClassName === 'verso-debug-breakpoint')"
        + ".map(d => d.range.startLineNumber).sort((a, b) => a - b).join(',')";

    // Sets a cell's text the way the notebook does when the change comes from outside the editor.
    private Task ReplaceTextAsync(int cellIndex, string text)
        => Page.EvaluateAsync<JsonElement>(
            "(() => { const ed = " + EditorFor(cellIndex) + ";"
            + " versoMonaco.setValue(ed.getContainerDomNode().id, " + JsonSerializer.Serialize(text) + "); return { done: true }; })()");

    private async Task<int> GlyphMarginWidthAsync(int cellIndex)
    {
        var result = await Page.EvaluateAsync<JsonElement>(
            "({ width: (" + EditorFor(cellIndex) + ").getLayoutInfo().glyphMarginWidth })");
        return result.GetProperty("width").GetInt32();
    }

    // A real click in the margin beside a line number, which is the only way a reader has of
    // setting a breakpoint with a pointer. The position is worked out from the editor's own
    // layout rather than from the markup Monaco happens to draw the margin with.
    private async Task ClickMarginAsync(int cellIndex, int line)
    {
        await WaitForAsync("(" + EditorFor(cellIndex) + ").getLayoutInfo().glyphMarginWidth > 0", "the breakpoint margin to appear");

        var point = await Page.EvaluateAsync<JsonElement>(
            "(() => { const ed = " + EditorFor(cellIndex) + "; const box = ed.getContainerDomNode().getBoundingClientRect();"
            + " const info = ed.getLayoutInfo(); const lineHeight = ed.getOption(monaco.editor.EditorOption.lineHeight);"
            + " return { x: box.left + info.glyphMarginLeft + info.glyphMarginWidth / 2,"
            + " y: box.top + ed.getTopForLineNumber(" + line + ") - ed.getScrollTop() + lineHeight / 2 }; })()");

        await Page.Mouse.ClickAsync(point.GetProperty("x").GetDouble(), point.GetProperty("y").GetDouble());
    }

    // Counted across the notebook's run buttons rather than found inside the n-th cell. Every
    // cell has one whenever nothing is running, which is the only time these tests press it.
    private Task RunCellAsync(int cellIndex)
        => Page.Locator(".verso-cell button.verso-cell-btn--run").Nth(cellIndex).ClickAsync();

    // How a cell's last run ended: "success", "cancelled" or "failed".
    private Task WaitForCellStatusAsync(int cellIndex, string status)
        => WaitForAsync(
            "!!document.querySelectorAll('.verso-cell')[" + cellIndex + "].querySelector('.verso-cell-status--" + status + "')",
            "cell " + (cellIndex + 1) + " to end its run as " + status,
            RunTimeoutMs);

    private Task ControlAsync(string label)
        => Page.Locator(".verso-debug-control[aria-label='" + label + "']").ClickAsync();

    private Task<string[]> VariablesAsync() => TextsAsync(".verso-debug-variable");

    // Each match's text with its whitespace folded, so a table row reads as "name type value".
    private async Task<string[]> TextsAsync(string selector)
    {
        var json = await Page.EvaluateAsync<string>(
            "JSON.stringify(Array.from(document.querySelectorAll(" + JsonSerializer.Serialize(selector) + "))"
            + ".map(e => e.innerText.replace(/\\s+/g, ' ').trim()))");
        return JsonSerializer.Deserialize<string[]>(json)!;
    }

    private async Task<int> CountAsync(string selector)
    {
        var result = await Page.EvaluateAsync<JsonElement>(
            "({ count: document.querySelectorAll(" + JsonSerializer.Serialize(selector) + ").length })");
        return result.GetProperty("count").GetInt32();
    }

    private Task WaitForTextAsync(string selector, string text, int timeoutMs = UiTimeoutMs)
        => WaitForAsync(
            "Array.from(document.querySelectorAll(" + JsonSerializer.Serialize(selector) + "))"
            + ".some(e => e.innerText.replace(/\\s+/g, ' ').includes(" + JsonSerializer.Serialize(text) + "))",
            "'" + text + "' in " + selector,
            timeoutMs);

    private async Task WaitForAsync(string condition, string what, int timeoutMs = UiTimeoutMs)
    {
        try
        {
            await Page.WaitForFunctionAsync<bool>("!!(" + condition + ")", timeout: timeoutMs);
        }
        catch (Exception ex) when (ex is TimeoutException or MotusTimeoutException)
        {
            var panel = await Page.EvaluateAsync<string>(
                "(document.querySelector('.verso-debug-panel') || { innerText: '(the Debug panel is closed)' }).innerText");
            Assert.Fail($"Timed out waiting for {what}. The Debug panel read:\n{panel}");
        }
    }
}
