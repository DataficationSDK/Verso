using System.Text.Json.Nodes;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers how a MIME payload from the host script becomes an output the notebook renderers
/// understand, and how store values cross to JSON and back.
/// </summary>
[TestClass]
public sealed class DisplayMappingTests
{
    [TestMethod]
    public void Map_HtmlPassesThrough()
    {
        var output = PythonHostSession.MapPayload("text/html", "<b>rich</b>");

        Assert.AreEqual("text/html", output.MimeType);
        Assert.AreEqual("<b>rich</b>", output.Content);
    }

    [TestMethod]
    public void Map_SvgKeepsItsOwnType()
    {
        // The narrower renderer used for collapsed cells handles SVG directly, so it does not
        // need the wrapping that raster images do.
        var output = PythonHostSession.MapPayload("image/svg+xml", "<svg />");

        Assert.AreEqual("image/svg+xml", output.MimeType);
    }

    [TestMethod]
    public void Map_RasterImagesBecomeHtmlElements()
    {
        var output = PythonHostSession.MapPayload("image/png", "AAAA");

        Assert.AreEqual("text/html", output.MimeType);
        Assert.AreEqual("<img src=\"data:image/png;base64,AAAA\" />", output.Content);
    }

    [TestMethod]
    public void Map_JpegIsWrappedWithItsOwnMediaType()
    {
        var output = PythonHostSession.MapPayload("image/jpeg", "BBBB");

        StringAssert.Contains(output.Content, "data:image/jpeg;base64,BBBB");
    }

    [TestMethod]
    public void Map_JsonPassesThrough()
    {
        var output = PythonHostSession.MapPayload("application/json", "{\"a\":1}");

        Assert.AreEqual("application/json", output.MimeType);
    }

    [TestMethod]
    public void Map_MarkdownKeepsItsOwnType()
    {
        // No renderer claims markdown yet, so it shows as text. Reporting the real type keeps
        // notebook export honest and lets a renderer pick it up later without a kernel change.
        var output = PythonHostSession.MapPayload("text/markdown", "# heading");

        Assert.AreEqual("text/markdown", output.MimeType);
        Assert.AreEqual("# heading", output.Content);
    }

    [TestMethod]
    public void Map_AnUnknownTypeIsCarriedAsItIs()
    {
        var output = PythonHostSession.MapPayload("application/vnd.custom", "opaque");

        Assert.AreEqual("application/vnd.custom", output.MimeType);
        Assert.AreEqual("opaque", output.Content);
    }

    // --- store values to JSON ---

    [TestMethod]
    public void ToJson_CarriesTheJsonShapes()
    {
        Assert.IsTrue(HostVariables.TryToJson(42, out var integer));
        Assert.AreEqual("42", integer!.ToJsonString());

        Assert.IsTrue(HostVariables.TryToJson("text", out var text));
        Assert.AreEqual("\"text\"", text!.ToJsonString());

        Assert.IsTrue(HostVariables.TryToJson(
            new List<object> { 1, "two" }, out var list));
        Assert.AreEqual("[1,\"two\"]", list!.ToJsonString());

        Assert.IsTrue(HostVariables.TryToJson(
            new Dictionary<string, object> { ["a"] = 1 }, out var map));
        Assert.AreEqual("{\"a\":1}", map!.ToJsonString());
    }

    [TestMethod]
    public void ToJson_SkipsValuesWithNoJsonForm()
    {
        Assert.IsFalse(HostVariables.TryToJson(new Action(() => { }), out _));
        Assert.IsFalse(HostVariables.TryToJson(Task.CompletedTask, out _));
        Assert.IsFalse(HostVariables.TryToJson(CancellationToken.None, out _));
        Assert.IsFalse(HostVariables.TryToJson(new object(), out _));
    }

    [TestMethod]
    public void ToJson_SkipsNonFiniteNumbers()
    {
        // JSON cannot express these, and the host script refuses the bare tokens.
        Assert.IsFalse(HostVariables.TryToJson(double.NaN, out _));
        Assert.IsFalse(HostVariables.TryToJson(double.PositiveInfinity, out _));
    }

    [TestMethod]
    public void ToJson_SkipsADictionaryWithNonStringKeys()
    {
        var value = new Dictionary<int, string> { [1] = "one" };

        Assert.IsFalse(HostVariables.TryToJson(value, out _));
    }

    [TestMethod]
    public void ToJson_StopsAtASelfReferentialValue()
    {
        var cyclic = new List<object>();
        cyclic.Add(cyclic);

        Assert.IsFalse(HostVariables.TryToJson(cyclic, out _));
    }

    [TestMethod]
    public void ToJson_NullIsRepresentable()
    {
        Assert.IsTrue(HostVariables.TryToJson(null, out var node));
        Assert.IsNull(node);
    }

    // --- JSON back to store values ---

    [TestMethod]
    public void FromJson_ProducesThePlainClrShapes()
    {
        Assert.AreEqual(true, HostVariables.FromJson(JsonValue.Create(true)));
        Assert.AreEqual(7L, HostVariables.FromJson(JsonNode.Parse("7")));
        Assert.AreEqual(1.5d, HostVariables.FromJson(JsonNode.Parse("1.5")));
        Assert.AreEqual("text", HostVariables.FromJson(JsonValue.Create("text")));
        Assert.IsNull(HostVariables.FromJson(null));

        var list = (List<object>)HostVariables.FromJson(JsonNode.Parse("[1,2]"))!;
        CollectionAssert.AreEqual(new object[] { 1L, 2L }, list);

        var map = (Dictionary<string, object>)HostVariables.FromJson(JsonNode.Parse("{\"a\":1}"))!;
        Assert.AreEqual(1L, map["a"]);
    }

    [TestMethod]
    public void Hash_IsStableForEqualContentAndDiffersOtherwise()
    {
        HostVariables.TryToJson(new List<object> { 1, 2 }, out var first);
        HostVariables.TryToJson(new List<object> { 1, 2 }, out var same);
        HostVariables.TryToJson(new List<object> { 1, 3 }, out var different);

        Assert.AreEqual(HostVariables.ComputeHash(first), HostVariables.ComputeHash(same));
        Assert.AreNotEqual(HostVariables.ComputeHash(first), HostVariables.ComputeHash(different));
    }
}
