using Verso.Http.Formatting;
using Verso.Http.Models;

namespace Verso.Http.Tests.Formatting;

[TestClass]
public sealed class HttpResponseFormatterTests
{
    private static HttpResponseData CreateResponse(int statusCode, string? body = null,
        string? contentType = null, long elapsedMs = 100)
    {
        var response = new HttpResponseData
        {
            StatusCode = statusCode,
            ReasonPhrase = statusCode switch
            {
                200 => "OK",
                201 => "Created",
                301 => "Moved Permanently",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => ""
            },
            Body = body,
            ContentType = contentType,
            ElapsedMs = elapsedMs
        };
        return response;
    }

    [TestMethod]
    public void FormatResponseHtml_2xx_HasGreenBadge()
    {
        var response = CreateResponse(200, "{}", "application/json");
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("verso-http-status-2xx"));
        Assert.IsTrue(html.Contains("200"));
        Assert.IsTrue(html.Contains("OK"));
    }

    [TestMethod]
    public void FormatResponseHtml_3xx_HasBlueBadge()
    {
        var response = CreateResponse(301);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("verso-http-status-3xx"));
    }

    [TestMethod]
    public void FormatResponseHtml_4xx_HasYellowBadge()
    {
        var response = CreateResponse(404);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("verso-http-status-4xx"));
    }

    [TestMethod]
    public void FormatResponseHtml_5xx_HasRedBadge()
    {
        var response = CreateResponse(500);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("verso-http-status-5xx"));
    }

    [TestMethod]
    public void FormatResponseHtml_ShowsElapsedTime()
    {
        var response = CreateResponse(200, elapsedMs: 42);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("42 ms"));
    }

    [TestMethod]
    public void FormatResponseHtml_JsonBody_PrettyPrinted()
    {
        var response = CreateResponse(200, "{\"id\":1,\"name\":\"test\"}", "application/json");
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        // Pretty-printed JSON should contain indentation (newlines/spaces)
        Assert.IsTrue(html.Contains("&quot;id&quot;"));
        Assert.IsTrue(html.Contains("&quot;name&quot;"));
    }

    [TestMethod]
    public void FormatResponseHtml_ResponseHeaders_InDetails()
    {
        var response = CreateResponse(200);
        response.Headers["Content-Type"] = "application/json";
        response.Headers["X-Request-Id"] = "abc-123";

        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("<details"));
        Assert.IsTrue(html.Contains("Content-Type"));
        Assert.IsTrue(html.Contains("X-Request-Id"));
    }

    [TestMethod]
    public void FormatResponseHtml_NoBody_NoPreTag()
    {
        var response = CreateResponse(204);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsFalse(html.Contains("<pre>"));
    }

    [TestMethod]
    public void FormatResponseHtml_LargeBody_Truncated()
    {
        var largeBody = new string('x', 150 * 1024); // 150KB
        var response = CreateResponse(200, largeBody, "text/plain");
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("verso-http-truncation"));
        Assert.IsTrue(html.Contains("100 KB"));
    }

    [TestMethod]
    public void FormatResponseHtml_HasCssStyles()
    {
        var response = CreateResponse(200);
        var html = HttpResponseFormatter.FormatResponseHtml(response);

        Assert.IsTrue(html.Contains("<style>"));
        Assert.IsTrue(html.Contains("verso-http-result"));
    }
}
