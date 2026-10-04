using Motus.Abstractions;
using Motus.Testing.MSTest;
using Verso.E2E.Tests.Infrastructure;

namespace Verso.E2E.Tests.Serve;

/// <summary>
/// Saving a notebook opened from a format serve does not write back converts it to a sibling
/// .verso file. When that file already exists, serve asks before replacing it, and nothing the
/// reader does short of choosing Replace may touch the file or the notebook.
/// </summary>
[MotusTestClass]
public sealed class SaveReplaceDialogTests : MotusTestBase
{
    private const string ExistingVerso = "an existing .verso file the save must not touch\n";

    // A negative check has nothing to wait for, so it waits this long for something to go wrong.
    private const int SettleMs = 750;

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

    private ILocator ReplaceDialog => Page.Locator("[role=dialog][aria-label='Replace Existing File?']");

    [TestMethod]
    public async Task Save_WithNoSiblingVerso_WritesItWithoutAsking()
    {
        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        var verso = Path.ChangeExtension(ipynb, ".verso");

        await OpenAsync(ipynb);
        await ClickSaveAsync();

        await WaitForFileAsync(verso, content => content.Contains("\"verso\"", StringComparison.Ordinal));
        Assert.AreEqual(0, await ReplaceDialog.CountAsync(), "Nothing was there to replace, so nothing should be asked.");
    }

    [TestMethod]
    public async Task Save_WithSiblingVerso_AsksWithCancelFocused()
    {
        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        _scratch.Write("report.verso", ExistingVerso);

        await OpenAsync(ipynb);
        await ClickSaveAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Visible, timeout: 10_000);

        var body = await ReplaceDialog.InnerTextAsync();
        StringAssert.Contains(body, "report.verso");
        StringAssert.Contains(body, "--preserve-format", "An .ipynb source should point at keeping its own format.");
        Assert.AreEqual("Cancel", await FocusedButtonTextAsync(), "The safe answer should hold focus when the question opens.");
    }

    [TestMethod]
    public async Task EnterOnCancel_KeepsTheFileAndTheNotebook()
    {
        KeyboardSupport.RequireReliableKeyPresses();

        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        var verso = _scratch.Write("report.verso", ExistingVerso);

        await OpenAsync(ipynb);
        await ClickSaveAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Visible, timeout: 10_000);
        Assert.AreEqual("Cancel", await FocusedButtonTextAsync());

        // Only the keydown is checked here: a dialog-wide Enter handler once replaced the file
        // from that alone. That the button's own activation then closes the dialog is not, since
        // Motus sends Enter without the text a browser needs to activate a focused button
        // (DataficationSDK/Motus#3). Wait for the dialog to close here once that is fixed.
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(SettleMs);

        Assert.AreEqual(ExistingVerso, await File.ReadAllTextAsync(verso));
        Assert.IsFalse(await IsCellEditorFocusedAsync(), "Enter on Cancel must not also start editing a cell.");
    }

    [TestMethod]
    public async Task Escape_KeepsTheFile()
    {
        KeyboardSupport.RequireReliableKeyPresses();

        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        var verso = _scratch.Write("report.verso", ExistingVerso);

        await OpenAsync(ipynb);
        await ClickSaveAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Visible, timeout: 10_000);

        await Page.Keyboard.PressAsync("Escape");
        await ReplaceDialog.WaitForAsync(ElementState.Detached, timeout: 10_000);
        await Page.WaitForTimeoutAsync(SettleMs);

        Assert.AreEqual(ExistingVerso, await File.ReadAllTextAsync(verso));
    }

    [TestMethod]
    public async Task CommandKeysWhileAsking_DoNotEditTheNotebook()
    {
        KeyboardSupport.RequireReliableKeyPresses();

        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        _scratch.Write("report.verso", ExistingVerso);

        await OpenAsync(ipynb);
        var before = await CellCountAsync();

        await ClickSaveAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Visible, timeout: 10_000);

        // Insert above, insert below, and the two-key delete, all of which act on the selected
        // cell in command mode.
        foreach (var key in new[] { "a", "b", "d", "d" })
            await Page.Keyboard.PressAsync(key);
        await Page.WaitForTimeoutAsync(SettleMs);

        Assert.AreEqual(1, await ReplaceDialog.CountAsync(), "Letter keys should leave the question open.");
        Assert.AreEqual(before, await CellCountAsync(), "Keys pressed while the question is open must not edit the notebook.");
    }

    [TestMethod]
    public async Task Replace_WritesTheConvertedNotebook()
    {
        var ipynb = _scratch.Write("report.ipynb", Notebooks.TwoCellJupyter);
        var verso = _scratch.Write("report.verso", ExistingVerso);

        await OpenAsync(ipynb);
        await ClickSaveAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Visible, timeout: 10_000);

        await ReplaceDialog.Locator("button", new LocatorOptions { HasText = "Replace" }).ClickAsync();
        await ReplaceDialog.WaitForAsync(ElementState.Detached, timeout: 10_000);

        var written = await WaitForFileAsync(verso, content => content != ExistingVerso);
        StringAssert.Contains(written, "\"verso\"");
        StringAssert.Contains(written, "# From Jupyter");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task OpenAsync(string notebookPath)
    {
        await Page.GotoAsync(s_server.UrlFor(notebookPath));

        // Cells are on the page after the first render, but the toolbar only acts once the
        // interactive circuit is up, which is when Monaco has attached its editors.
        await Page.WaitForFunctionAsync<bool>(
            "document.querySelectorAll('.verso-cell').length > 0 && !!document.querySelector('.verso-cell .monaco-editor')",
            timeout: 60_000);
    }

    private Task ClickSaveAsync()
        => Page.Locator("button.verso-toolbar-btn[data-verso-tip='Save']").ClickAsync();

    private Task<int> CellCountAsync()
        => Page.EvaluateAsync<int>("document.querySelectorAll('.verso-cell').length");

    private Task<string> FocusedButtonTextAsync()
        => Page.EvaluateAsync<string>(
            "document.activeElement instanceof HTMLButtonElement ? document.activeElement.textContent.trim() : ''");

    private Task<bool> IsCellEditorFocusedAsync()
        => Page.EvaluateAsync<bool>("!!document.activeElement?.closest('.monaco-editor')");

    private static async Task<string> WaitForFileAsync(string path, Func<string, bool> isDone)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    last = await File.ReadAllTextAsync(path);
                    if (isDone(last))
                        return last;
                }
                catch (IOException)
                {
                    // Still being written.
                }
            }

            await Task.Delay(100);
        }

        Assert.Fail($"{Path.GetFileName(path)} never reached the expected content. Last read:\n{last ?? "(missing)"}");
        return last!;
    }
}
