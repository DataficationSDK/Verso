namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class MonacoEditorTests : BunitTestContext
{
    private IRenderedComponent<MonacoEditor> Render(string value = "var x = 1;", string language = "csharp")
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        return TestContext.RenderComponent<MonacoEditor>(p => p
            .Add(e => e.Value, value)
            .Add(e => e.Language, language));
    }

    private int SetLanguageCallCount()
        => TestContext!.JSInterop.Invocations.Count(i => i.Identifier == "versoMonaco.setLanguage");

    [TestMethod]
    public void SetLanguage_NotCalled_WhenLanguageUnchanged()
    {
        var cut = Render();

        // The editor is created with its initial language, so re-renders with the same language
        // must not issue another interop call. During Run All every code cell re-renders once per
        // coalesced output flush; an unconditional call here floods the webview main thread.
        cut.SetParametersAndRender(p => p.Add(e => e.Value, "var x = 1;"));
        cut.SetParametersAndRender(p => p.Add(e => e.Value, "var x = 1;"));

        Assert.AreEqual(0, SetLanguageCallCount());
    }

    [TestMethod]
    public void SetLanguage_CalledOnce_WhenLanguageChanges()
    {
        var cut = Render();

        cut.SetParametersAndRender(p => p.Add(e => e.Language, "python"));
        Assert.AreEqual(1, SetLanguageCallCount());

        cut.SetParametersAndRender(p => p.Add(e => e.Language, "python"));
        Assert.AreEqual(1, SetLanguageCallCount());
    }

    private int SetDiagnosticsSuspendedCallCount()
        => TestContext!.JSInterop.Invocations.Count(i => i.Identifier == "versoMonaco.setDiagnosticsSuspended");

    [TestMethod]
    public async Task GetDiagnostics_ReturnsCallbackResult()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        string? received = null;
        var expected = new { items = Array.Empty<object>() };
        var cut = TestContext.RenderComponent<MonacoEditor>(p => p
            .Add(e => e.Value, "var x = 1;")
            .Add(e => e.OnGetDiagnostics, code => { received = code; return Task.FromResult<object?>(expected); }));

        var result = await cut.Instance.GetDiagnostics("var y = 2;");

        Assert.AreEqual("var y = 2;", received);
        Assert.AreSame(expected, result);
    }

    [TestMethod]
    public async Task GetDiagnostics_ReturnsNull_WhenCallbackThrows()
    {
        // Null tells the editor to keep the markers it has; a failed request must not clear them.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = TestContext.RenderComponent<MonacoEditor>(p => p
            .Add(e => e.OnGetDiagnostics, _ => throw new InvalidOperationException("host gone")));

        Assert.IsNull(await cut.Instance.GetDiagnostics("var y = 2;"));
    }

    [TestMethod]
    public async Task GetDiagnostics_ReturnsNull_WithoutCallback()
    {
        var cut = Render();

        Assert.IsNull(await cut.Instance.GetDiagnostics("var y = 2;"));
    }

    [TestMethod]
    public void DiagnosticsSuspended_ForwardedOnlyOnChange()
    {
        var cut = Render();

        // Unchanged parameters arrive on every parent render; only a real flip reaches the editor.
        cut.SetParametersAndRender(p => p.Add(e => e.DiagnosticsSuspended, false));
        Assert.AreEqual(0, SetDiagnosticsSuspendedCallCount());

        cut.SetParametersAndRender(p => p.Add(e => e.DiagnosticsSuspended, true));
        cut.SetParametersAndRender(p => p.Add(e => e.DiagnosticsSuspended, true));
        Assert.AreEqual(1, SetDiagnosticsSuspendedCallCount());

        var call = TestContext!.JSInterop.Invocations.Single(i => i.Identifier == "versoMonaco.setDiagnosticsSuspended");
        Assert.AreEqual(true, call.Arguments[1]);

        cut.SetParametersAndRender(p => p.Add(e => e.DiagnosticsSuspended, false));
        Assert.AreEqual(2, SetDiagnosticsSuspendedCallCount());
    }

    [TestMethod]
    public async Task RefreshAndClearDiagnostics_CallInterop()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.RefreshDiagnosticsAsync());
        await cut.InvokeAsync(() => cut.Instance.ClearDiagnosticsAsync());

        Assert.AreEqual(1, TestContext!.JSInterop.Invocations.Count(i => i.Identifier == "versoMonaco.refreshDiagnostics"));
        Assert.AreEqual(1, TestContext.JSInterop.Invocations.Count(i => i.Identifier == "versoMonaco.clearDiagnostics"));
    }
}
