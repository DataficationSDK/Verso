using Verso.Http.Parsing;

namespace Verso.Http.Tests.Parsing;

[TestClass]
public sealed class HttpRequestParserTests
{
    [TestMethod]
    public void Parse_SimpleGet_ReturnsOneRequest()
    {
        var (vars, requests) = HttpRequestParser.Parse("GET https://example.com/api");

        Assert.AreEqual(0, vars.Count);
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("GET", requests[0].Method);
        Assert.AreEqual("https://example.com/api", requests[0].Url);
        Assert.IsNull(requests[0].Body);
    }

    [TestMethod]
    public void Parse_UrlOnly_DefaultsToGet()
    {
        var (_, requests) = HttpRequestParser.Parse("https://example.com/api");

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("GET", requests[0].Method);
        Assert.AreEqual("https://example.com/api", requests[0].Url);
    }

    [TestMethod]
    public void Parse_PostWithBody_ExtractsBody()
    {
        var code = "POST https://example.com/api\nContent-Type: application/json\n\n{\"name\": \"test\"}";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("POST", requests[0].Method);
        Assert.AreEqual("application/json", requests[0].Headers["Content-Type"]);
        Assert.AreEqual("{\"name\": \"test\"}", requests[0].Body);
    }

    [TestMethod]
    public void Parse_Headers_AreParsedCorrectly()
    {
        var code = "GET https://example.com\nAuthorization: Bearer token123\nAccept: application/json";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual(2, requests[0].Headers.Count);
        Assert.AreEqual("Bearer token123", requests[0].Headers["Authorization"]);
        Assert.AreEqual("application/json", requests[0].Headers["Accept"]);
    }

    [TestMethod]
    public void Parse_MultipleRequests_SeparatedByHash()
    {
        var code = "GET https://example.com/first\n\n###\n\nPOST https://example.com/second\nContent-Type: text/plain\n\nhello";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("GET", requests[0].Method);
        Assert.AreEqual("https://example.com/first", requests[0].Url);
        Assert.AreEqual("POST", requests[1].Method);
        Assert.AreEqual("https://example.com/second", requests[1].Url);
        Assert.AreEqual("hello", requests[1].Body);
    }

    [TestMethod]
    public void Parse_Variables_AreParsed()
    {
        var code = "@host = api.example.com\n@token = abc123\n\nGET https://{{host}}/api";
        var (vars, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(2, vars.Count);
        Assert.AreEqual("host", vars[0].Name);
        Assert.AreEqual("api.example.com", vars[0].Value);
        Assert.AreEqual("token", vars[1].Name);
        Assert.AreEqual("abc123", vars[1].Value);
        Assert.AreEqual(1, requests.Count);
    }

    [TestMethod]
    public void Parse_Directives_NameDirective()
    {
        var code = "# @name myRequest\nGET https://example.com";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("myRequest", requests[0].Name);
    }

    [TestMethod]
    public void Parse_Directives_NoRedirect()
    {
        var code = "# @no-redirect\nGET https://example.com/redirect";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.IsTrue(requests[0].NoRedirect);
    }

    [TestMethod]
    public void Parse_Directives_NoCookieJar()
    {
        var code = "# @no-cookie-jar\nGET https://example.com";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.IsTrue(requests[0].NoCookieJar);
    }

    [TestMethod]
    public void Parse_Comments_AreSkipped()
    {
        var code = "# This is a comment\n// Another comment\nGET https://example.com";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("GET", requests[0].Method);
    }

    [TestMethod]
    public void Parse_QueryContinuation_AppendedToUrl()
    {
        var code = "GET https://example.com/search\n?q=hello\n&page=1\n&limit=10";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("https://example.com/search?q=hello&page=1&limit=10", requests[0].Url);
    }

    [TestMethod]
    public void Parse_HttpVersion_IsIgnored()
    {
        var code = "GET https://example.com HTTP/1.1";
        var (_, requests) = HttpRequestParser.Parse(code);

        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("https://example.com", requests[0].Url);
    }

    [TestMethod]
    public void Parse_EmptySource_ReturnsEmpty()
    {
        var (vars, requests) = HttpRequestParser.Parse("");
        Assert.AreEqual(0, vars.Count);
        Assert.AreEqual(0, requests.Count);
    }

    [TestMethod]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        var (vars, requests) = HttpRequestParser.Parse("   \n  \n   ");
        Assert.AreEqual(0, vars.Count);
        Assert.AreEqual(0, requests.Count);
    }

    [TestMethod]
    public void Parse_AllMethods_Recognized()
    {
        var methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        foreach (var method in methods)
        {
            var (_, requests) = HttpRequestParser.Parse($"{method} https://example.com");
            Assert.AreEqual(1, requests.Count, $"Failed for method: {method}");
            Assert.AreEqual(method, requests[0].Method);
        }
    }

    [TestMethod]
    public void Parse_CaseInsensitiveMethod()
    {
        var (_, requests) = HttpRequestParser.Parse("post https://example.com");
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("POST", requests[0].Method);
    }
}
