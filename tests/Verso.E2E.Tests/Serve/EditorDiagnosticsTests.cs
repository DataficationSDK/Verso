using System.Text.Json;
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Verso.E2E.Tests.Infrastructure;

namespace Verso.E2E.Tests.Serve;

/// <summary>
/// Kernel diagnostics reach the editor as Monaco markers: an error shows with its code, goes
/// away when the code is fixed, knows about names an earlier cell bound, and is cleared when the
/// kernel restarts and the state it was computed against is gone.
/// </summary>
[MotusTestClass]
public sealed class EditorDiagnosticsTests : MotusTestBase
{
    // Long enough for the edit debounce plus a cold Roslyn compilation on a busy agent.
    private const int MarkerTimeoutMs = 30_000;

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
    public async Task TypeError_ShowsMarkerWithCode_AndClearsWhenFixed()
    {
        await OpenAsync();

        await SetCellSourceAsync(0, "var ok = 1;\nint x = \"text\";");
        await WaitForMarkersAsync(0, "markers.some(m => String(m.code) === 'CS0029')");

        var marker = (await MarkersAsync(0)).Single(m => m.Code == "CS0029");
        Assert.AreEqual(8, marker.Severity, "A kernel error should be an error marker.");
        Assert.AreEqual(2, marker.StartLineNumber, "Kernel lines are 0-based, Monaco's 1-based.");
        StringAssert.StartsWith(marker.Source, "C#", "The marker should name the kernel that reported it.");

        await SetCellSourceAsync(0, "var ok = 1;\nint x = 2;");
        await WaitForMarkersAsync(0, "markers.length === 0");
    }

    [TestMethod]
    public async Task NameBoundInExecutedCell_IsNotMarked()
    {
        await OpenAsync();

        await SetCellSourceAsync(0, "var answer = 42;");
        await RunCellAsync(0);

        // The undeclared name gives a marker to wait for, so the check on 'answer' is made
        // against a reply that has actually arrived.
        await SetCellSourceAsync(1, "answer + notDeclared");
        await WaitForMarkersAsync(1, "markers.some(m => String(m.code) === 'CS0103')");

        var markers = await MarkersAsync(1);
        Assert.AreEqual(1, markers.Count, "Only the undeclared name should be marked: "
            + string.Join("; ", markers.Select(m => m.Message)));
        StringAssert.Contains(markers[0].Message, "notDeclared");
    }

    [TestMethod]
    public async Task KernelRestart_ClearsMarkers()
    {
        await OpenAsync();

        await SetCellSourceAsync(0, "var ok = 1;");
        await RunCellAsync(0);

        await SetCellSourceAsync(1, "int x = \"text\";");
        await WaitForMarkersAsync(1, "markers.length > 0");

        await Page.Locator("button.verso-toolbar-btn[data-verso-tip='Restart Kernel']").ClickAsync();
        var confirm = Page.Locator("[role=dialog][aria-label='Restart Kernel'] .verso-modal-btn--primary");
        await confirm.WaitForAsync(ElementState.Visible, timeout: 10_000);
        await confirm.ClickAsync();

        await WaitForMarkersAsync(1, "markers.length === 0");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task OpenAsync()
    {
        var ipynb = _scratch.Write("diagnostics.ipynb", Notebooks.TwoCSharpCells);
        await Page.GotoAsync(s_server.UrlFor(ipynb));

        // The editors are attached once the interactive circuit is up.
        await Page.WaitForFunctionAsync<bool>(
            "document.querySelectorAll('.verso-cell .monaco-editor').length >= 2 && !!window.monaco",
            timeout: 60_000);
    }

    // Finds the Monaco editor inside the n-th cell.
    private static string EditorFor(int cellIndex) =>
        "(() => { const cell = document.querySelectorAll('.verso-cell')[" + cellIndex + "];"
        + " return cell ? monaco.editor.getEditors().find(e => cell.contains(e.getContainerDomNode())) : null; })()";

    private static string MarkersFor(int cellIndex) =>
        "(() => { const ed = " + EditorFor(cellIndex) + ";"
        + " return ed ? monaco.editor.getModelMarkers({ owner: 'verso-kernel', resource: ed.getModel().uri }) : []; })()";

    // Sets the text through Monaco rather than the keyboard, which is the same content-change
    // path typing takes and does not depend on key presses Motus cannot send on every platform.
    private async Task SetCellSourceAsync(int cellIndex, string source)
    {
        var ok = await Page.EvaluateAsync<bool>(
            "(() => { const ed = " + EditorFor(cellIndex) + "; if (!ed) return false;"
            + " ed.setValue(" + JsonSerializer.Serialize(source) + "); return true; })()");
        Assert.IsTrue(ok, $"No editor found for cell {cellIndex}.");
    }

    private Task WaitForMarkersAsync(int cellIndex, string condition)
        => Page.WaitForFunctionAsync<bool>(
            "(() => { const markers = " + MarkersFor(cellIndex) + "; return " + condition + "; })()",
            timeout: MarkerTimeoutMs);

    private async Task<List<MarkerInfo>> MarkersAsync(int cellIndex)
    {
        // Returned as a JSON string: Motus hands back objects and strings reliably, and the
        // marker's code can be a string or an object, which a string sidesteps.
        var json = await Page.EvaluateAsync<string>(
            "JSON.stringify(" + MarkersFor(cellIndex) + ".map(m => ({"
            + " severity: m.severity, message: m.message, code: m.code == null ? null : String(m.code),"
            + " source: m.source || null, startLineNumber: m.startLineNumber })))");
        return JsonSerializer.Deserialize<List<MarkerInfo>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private async Task RunCellAsync(int cellIndex)
    {
        var cell = Page.Locator(".verso-cell").Nth(cellIndex);
        await cell.Locator("button.verso-cell-btn--run").ClickAsync();
        await cell.Locator(".verso-cell-status").WaitForAsync(ElementState.Visible, timeout: 60_000);
    }

    private sealed record MarkerInfo(int Severity, string Message, string? Code, string? Source, int StartLineNumber);
}
