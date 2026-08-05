using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers what a widget document actually carries, against a real interpreter with real
/// ipywidgets, since the embedding is done by that library rather than by anything here.
/// </summary>
/// <remarks>
/// Nothing installs ipywidgets for this. It is the author's own dependency, present in any
/// environment where a widget exists to be shown, and it cannot be added to the environment the
/// rest of the suite runs in: importing it brings a real IPython, and the tests covering the
/// stand-in for <c>IPython.display</c> are about what happens when there is no real one. An
/// environment without it has nothing to say here and reports inconclusive.
/// </remarks>
[TestClass]
public sealed class WidgetEmbeddingIntegrationTests
{
    private static string Python => PythonProbe.Require();

    /// <summary>Distinctive enough that finding it in the document cannot be a coincidence.</summary>
    private const string Keepsake = "kept-although-it-equals-the-default";

    private static async Task<PythonHostSession> StartedSessionAsync()
    {
        var python = Python;
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var options = new PythonKernelOptions { PythonExecutable = python };
        var session = new PythonHostSession(options, locator: locator);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    private static async Task RequireWidgetsAsync(PythonHostSession session)
    {
        var probe = await session.ExecuteAsync("import ipywidgets", new StubExecutionContext());
        if (probe.Any(o => o.IsError))
            Assert.Inconclusive("ipywidgets is not importable in this environment.");
    }

    private static string Describe(IReadOnlyList<CellOutput> outputs)
        => outputs.Count == 0
            ? "nothing"
            : string.Join(" | ", outputs.Select(o => $"[{o.MimeType}] {Trim(o.Content)}"));

    private static string Trim(string content)
        => content.Length <= 120 ? content : content[..120] + "...";

    [TestMethod]
    [Timeout(120_000)]
    public async Task AWidgetDocumentCarriesASynchronizedTraitThatEqualsItsDefault()
    {
        // A trait still holding its declared default is the only case worth asserting on: the
        // embedder can be asked to leave those out, and a widget with anything else in the trait
        // would look the same either way. Some widgets keep what the page needs in exactly such
        // a trait, which is why the whole state travels.
        await using var session = await StartedSessionAsync();
        await RequireWidgetsAsync(session);

        var code = $@"
import ipywidgets
import traitlets

class Marked(ipywidgets.DOMWidget):
    _model_name = traitlets.Unicode('IntSliderModel').tag(sync=True)
    _view_name = traitlets.Unicode('IntSliderView').tag(sync=True)
    keepsake = traitlets.Unicode('{Keepsake}').tag(sync=True)

Marked()
";

        var outputs = await session.ExecuteAsync(code, new StubExecutionContext());

        var document = outputs.SingleOrDefault(o => o.MimeType == CellOutput.WidgetMimeType);
        Assert.IsNotNull(document,
            $"Expected a widget document. Got: {Describe(outputs)}");
        StringAssert.Contains(document.Content, Keepsake,
            "The widget's state reached the page without the trait the page would need.");
    }
}
