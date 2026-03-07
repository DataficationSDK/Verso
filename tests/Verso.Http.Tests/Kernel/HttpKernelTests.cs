using Verso.Http.Kernel;
using Verso.Testing.Stubs;

namespace Verso.Http.Tests.Kernel;

[TestClass]
public sealed class HttpKernelTests
{
    [TestMethod]
    public async Task ExecuteAsync_EmptyCode_ReturnsError()
    {
        var kernel = new HttpKernel();
        var ctx = new StubExecutionContext();

        var outputs = await kernel.ExecuteAsync("", ctx);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.Contains("No HTTP request found"));
    }

    [TestMethod]
    public async Task ExecuteAsync_RelativeUrlWithoutBase_ReturnsError()
    {
        var kernel = new HttpKernel();
        var ctx = new StubExecutionContext();

        var outputs = await kernel.ExecuteAsync("GET /api/users", ctx);

        Assert.IsTrue(outputs.Any(o => o.IsError && o.Content.Contains("base URL")));
    }

    [TestMethod]
    public async Task GetCompletionsAsync_HttpMethods_Returned()
    {
        var kernel = new HttpKernel();

        var completions = await kernel.GetCompletionsAsync("G", 1);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "GET"));
    }

    [TestMethod]
    public async Task GetCompletionsAsync_Headers_Returned()
    {
        var kernel = new HttpKernel();

        var completions = await kernel.GetCompletionsAsync("Con", 3);

        Assert.IsTrue(completions.Any(c => c.DisplayText.Contains("Content-Type")));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_NoRequest_ReturnsError()
    {
        var kernel = new HttpKernel();

        var diag = await kernel.GetDiagnosticsAsync("this is not a valid request");

        Assert.IsTrue(diag.Any(d => d.Severity == Verso.Abstractions.DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ValidRequest_NoDiagnostics()
    {
        var kernel = new HttpKernel();

        var diag = await kernel.GetDiagnosticsAsync("GET https://example.com");

        // Should have no errors (may have unresolved variable warnings if any)
        Assert.IsFalse(diag.Any(d => d.Severity == Verso.Abstractions.DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_HttpMethod_ReturnsDescription()
    {
        var kernel = new HttpKernel();

        var hover = await kernel.GetHoverInfoAsync("GET https://example.com", 1);

        Assert.IsNotNull(hover);
        Assert.IsTrue(hover!.Content.Contains("GET"));
    }

    [TestMethod]
    public void ExtensionProperties_Correct()
    {
        var kernel = new HttpKernel();

        Assert.AreEqual("verso.http.kernel.http", kernel.ExtensionId);
        Assert.AreEqual("http", kernel.LanguageId);
        Assert.AreEqual("HTTP", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".http"));
    }
}
