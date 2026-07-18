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
}
