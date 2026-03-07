using Verso.Http.Models;
using Verso.Http.Parsing;

namespace Verso.Http.Tests.Parsing;

[TestClass]
public sealed class HttpResponseReferenceTests
{
    private static Dictionary<string, HttpResponseData> CreateResponses(string name, string body,
        Dictionary<string, string>? headers = null)
    {
        var response = new HttpResponseData
        {
            StatusCode = 200,
            Body = body,
        };
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
                response.Headers[k] = v;
        }

        return new Dictionary<string, HttpResponseData>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = response
        };
    }

    [TestMethod]
    public void Resolve_BodyEntireBody_ReturnsFull()
    {
        var responses = CreateResponses("req1", "{\"id\": 1}");
        var result = HttpResponseReference.Resolve("req1.response.body.*", responses);
        Assert.AreEqual("{\"id\": 1}", result);
    }

    [TestMethod]
    public void Resolve_BodyJsonProperty_ReturnsValue()
    {
        var responses = CreateResponses("req1", "{\"id\": 42, \"name\": \"test\"}");
        var result = HttpResponseReference.Resolve("req1.response.body.$.id", responses);
        Assert.AreEqual("42", result);
    }

    [TestMethod]
    public void Resolve_BodyStringProperty_ReturnsString()
    {
        var responses = CreateResponses("req1", "{\"name\": \"hello\"}");
        var result = HttpResponseReference.Resolve("req1.response.body.$.name", responses);
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void Resolve_BodyNestedProperty_ReturnsValue()
    {
        var json = "{\"data\": {\"user\": {\"id\": 5}}}";
        var responses = CreateResponses("req1", json);
        var result = HttpResponseReference.Resolve("req1.response.body.$.data.user.id", responses);
        Assert.AreEqual("5", result);
    }

    [TestMethod]
    public void Resolve_BodyArrayIndex_ReturnsElement()
    {
        var json = "{\"items\": [\"a\", \"b\", \"c\"]}";
        var responses = CreateResponses("req1", json);
        var result = HttpResponseReference.Resolve("req1.response.body.$.items[1]", responses);
        Assert.AreEqual("b", result);
    }

    [TestMethod]
    public void Resolve_BodyArrayObjectProperty_ReturnsValue()
    {
        var json = "{\"users\": [{\"name\": \"Alice\"}, {\"name\": \"Bob\"}]}";
        var responses = CreateResponses("req1", json);
        var result = HttpResponseReference.Resolve("req1.response.body.$.users[0].name", responses);
        Assert.AreEqual("Alice", result);
    }

    [TestMethod]
    public void Resolve_HeaderReference_ReturnsValue()
    {
        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        var responses = CreateResponses("req1", "", headers);
        var result = HttpResponseReference.Resolve("req1.response.headers.Content-Type", responses);
        Assert.AreEqual("application/json", result);
    }

    [TestMethod]
    public void Resolve_MissingName_ReturnsNull()
    {
        var responses = CreateResponses("req1", "{}");
        var result = HttpResponseReference.Resolve("unknown.response.body.$.id", responses);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Resolve_MissingProperty_ReturnsNull()
    {
        var responses = CreateResponses("req1", "{\"id\": 1}");
        var result = HttpResponseReference.Resolve("req1.response.body.$.missing", responses);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Resolve_NonMatchingExpression_ReturnsNull()
    {
        var responses = CreateResponses("req1", "{}");
        var result = HttpResponseReference.Resolve("someOtherExpression", responses);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Resolve_EmptyResponses_ReturnsNull()
    {
        var result = HttpResponseReference.Resolve(
            "req1.response.body.$.id",
            new Dictionary<string, HttpResponseData>());
        Assert.IsNull(result);
    }
}
