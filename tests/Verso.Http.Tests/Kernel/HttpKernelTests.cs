using System.Net;
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

    /// <summary>
    /// A request that came back 5xx did not do what the cell asked. Reporting it as a success
    /// meant a notebook could pass in a pipeline while every call it made was failing.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ServerError_FailsTheCell()
    {
        using var server = new LocalServer(statusCode: 500, body: "boom");
        var kernel = new HttpKernel();
        var ctx = new StubExecutionContext();

        var outputs = await kernel.ExecuteAsync($"GET {server.Url}", ctx);

        var failure = outputs.FirstOrDefault(o => o.IsError);
        Assert.IsNotNull(failure, "a 500 response should fail the cell");
        Assert.AreEqual("HttpRequestFailed", failure!.ErrorName);
        StringAssert.Contains(failure.Content, "500");

        // The response is still shown, because the status line and body are the whole point of
        // looking, and it has to read before the verdict.
        Assert.IsTrue(outputs.Count > 1, "the response itself should still be reported");
        Assert.IsFalse(outputs[0].IsError, "the response should come before the failure");
    }

    [TestMethod]
    public async Task ExecuteAsync_SuccessfulResponse_DoesNotFailTheCell()
    {
        using var server = new LocalServer(statusCode: 200, body: "fine");
        var kernel = new HttpKernel();
        var ctx = new StubExecutionContext();

        var outputs = await kernel.ExecuteAsync($"GET {server.Url}", ctx);

        Assert.IsFalse(outputs.Any(o => o.IsError), "a 200 response is not a failure");
    }

    /// <summary>A one-request loopback server, so these tests need no network.</summary>
    private sealed class LocalServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        public LocalServer(int statusCode, string body)
        {
            var port = GetFreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    context.Response.StatusCode = statusCode;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
                catch (HttpListenerException)
                {
                    // Disposed while waiting, which is how the test ends.
                }
                catch (ObjectDisposedException)
                {
                }
            });
        }

        public string Url { get; }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
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
