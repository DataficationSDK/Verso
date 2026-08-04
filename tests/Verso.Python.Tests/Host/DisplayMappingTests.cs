using System.Text.Json.Nodes;
using Verso.Abstractions;
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

    [TestMethod]
    public void Map_AWidgetKeepsItsOwnType()
    {
        // Deliberately not folded into text/html: a widget is a whole document, and the surface
        // showing it has to give it a frame with a real address rather than paste it inline.
        var document = "<!DOCTYPE html><html><body><script>widget</script></body></html>";

        var output = PythonHostSession.MapPayload(CellOutput.WidgetMimeType, document);

        Assert.AreEqual(CellOutput.WidgetMimeType, output.MimeType);
        Assert.AreEqual(document, output.Content);
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
    public void ToJson_RefusesWhatDescribesTheProgramAndSaysWhy()
    {
        // These describe the running program rather than data, so there is nothing to send. What
        // matters is that each says why: the reason is what the notebook is shown in its place.
        Assert.IsFalse(HostVariables.TryToJson(new Action(() => { }), out _, out var callback));
        StringAssert.Contains(callback!, "function");

        Assert.IsFalse(HostVariables.TryToJson(Task.CompletedTask, out _, out var task));
        StringAssert.Contains(task!, "operation");

        Assert.IsFalse(HostVariables.TryToJson(CancellationToken.None, out _, out var token));
        StringAssert.Contains(token!, "operation");
    }

    [TestMethod]
    public void ToJson_CarriesAPlainObjectAsItsProperties()
    {
        // Previously refused outright, which is what left a record from another language absent
        // from the Python scope with nothing said about it.
        Assert.IsTrue(HostVariables.TryToJson(new { Name = "row", Count = 2 }, out var node));
        Assert.AreEqual("{\"Name\":\"row\",\"Count\":2}", node!.ToJsonString());
    }

    [TestMethod]
    public void ToJson_TagsNonFiniteNumbersRatherThanRefusingThem()
    {
        // JSON has no token for these, so they travel tagged. Refusing them used to discard every
        // other element of whatever array one of them appeared in.
        Assert.IsTrue(HostVariables.TryToJson(double.NaN, out var nan));
        StringAssert.Contains(nan!.ToJsonString(), "NaN");

        Assert.IsTrue(HostVariables.TryToJson(new List<object> { 1.0, double.NaN, 3.0 }, out var list));
        // Written with their fraction intact, so a column of measurements that happens to land on
        // whole values is still read as fractional on the other side.
        var array = (JsonArray)list!;
        Assert.AreEqual(3, array.Count);
        Assert.AreEqual("1.0", array[0]!.ToJsonString());
        Assert.AreEqual("3.0", array[2]!.ToJsonString());
    }

    [TestMethod]
    public void ToJson_StringifiesNonStringDictionaryKeys()
    {
        var value = new Dictionary<int, string> { [1] = "one" };

        Assert.IsTrue(HostVariables.TryToJson(value, out var node));
        Assert.AreEqual("{\"1\":\"one\"}", node!.ToJsonString());
    }

    [TestMethod]
    public void ToJson_ReplacesABackReferenceRatherThanTheWholeValue()
    {
        var cyclic = new List<object>();
        cyclic.Add(cyclic);

        // The cycle itself cannot be described, but everything around it can, so only the back
        // reference is lost. Refusing the whole value for it lost data that was perfectly fine.
        Assert.IsTrue(HostVariables.TryToJson(cyclic, out var node));
        Assert.AreEqual("[null]", node!.ToJsonString());
    }

    [TestMethod]
    public void ToJson_IsolatesAPropertyThatThrowsWhenRead()
    {
        Assert.IsTrue(HostVariables.TryToJson(new ThrowingProperty(), out var node));

        var text = node!.ToJsonString();
        StringAssert.Contains(text, "\"Good\":1");
        StringAssert.Contains(text, WireTag.Unavailable);
    }

    private sealed class ThrowingProperty
    {
        public int Good => 1;

        public int Bad => throw new InvalidOperationException("no");
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
    public void ToJson_KeepsWhatJsonHasNoFormFor()
    {
        // A date stays a date, and an exact decimal stays exact. Written as plain JSON a decimal
        // is indistinguishable from a double, so the reason for choosing it would be lost quietly.
        Assert.IsTrue(HostVariables.TryToJson(new DateTime(2026, 4, 16, 8, 0, 0), out var stamp));
        StringAssert.Contains(stamp!.ToJsonString(), "2026-04-16T08:00:00.000000");

        Assert.IsTrue(HostVariables.TryToJson(1.005m, out var money));
        StringAssert.Contains(money!.ToJsonString(), "\"1.005\"");
    }

    [TestMethod]
    public void ToJson_WritesADateInTheFormOlderInterpretersAccept()
    {
        // CPython before 3.11 rejects both a seven digit fraction, which is what .NET writes by
        // default, and a bare Z. Six digits with an explicit offset is accepted by every version.
        Assert.IsTrue(HostVariables.TryToJson(
            new DateTime(2026, 4, 16, 8, 0, 0, DateTimeKind.Utc), out var utc));

        // Read from the tag rather than the rendered JSON, which escapes the plus sign.
        Assert.IsTrue(WireTag.TryRead(utc, out var tag, out var value));
        Assert.AreEqual(WireTag.DateTime, tag);

        var text = value!.GetValue<string>();
        StringAssert.EndsWith(text, "+00:00");
        Assert.IsFalse(text.EndsWith('Z'), "A bare Z suffix cannot be parsed before 3.11.");
        Assert.IsFalse(text.Contains(".0000000"), "Seven fractional digits cannot be parsed before 3.11.");
    }

    [TestMethod]
    public void FromJson_RebuildsTheTaggedTypes()
    {
        Assert.AreEqual(
            new DateTime(2026, 4, 16, 8, 0, 0),
            HostVariables.FromJson(WireTag.Node(WireTag.DateTime, "2026-04-16T08:00:00.000000")));

        Assert.AreEqual(1.005m, HostVariables.FromJson(WireTag.Node(WireTag.Decimal, "1.005")));
        Assert.AreEqual(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            HostVariables.FromJson(WireTag.Node(WireTag.Guid, "00000000-0000-0000-0000-000000000001")));
        Assert.AreEqual(double.NaN, HostVariables.FromJson(WireTag.Node(WireTag.Float, "NaN")));
    }

    [TestMethod]
    public void FromJson_KeepsTheTextWhenATagWillNotParse()
    {
        // The caller reads a whole cell's variables with no handler of its own, so one malformed
        // value must not cost every value after it. Python's Decimal has states .NET's has not.
        Assert.AreEqual("NaN", HostVariables.FromJson(WireTag.Node(WireTag.Decimal, "NaN")));
    }

    [TestMethod]
    public void FromJson_ReadsAnOrdinaryObjectThatBorrowsATagName()
    {
        // Only an object of exactly the two reserved keys is a tag. One that merely contains a
        // key by that name is a dictionary the notebook built, and stays one.
        var node = JsonNode.Parse($"{{\"{WireTag.TypeKey}\":\"datetime\",\"other\":1}}");
        var map = (Dictionary<string, object>)HostVariables.FromJson(node)!;

        Assert.AreEqual(2, map.Count);
        Assert.AreEqual(1L, map["other"]);
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

    /// <summary>
    /// The spellings Python and System.Text.Json disagree on. A value that fingerprints differently
    /// on the two sides is taken for a value the cell changed, and the store copy is then injected
    /// over the live object, which is how an array of small numbers arrives in the next cell as a
    /// list. Both of these reached a notebook: the first is any probability or residual, the second
    /// any count past a quadrillion.
    /// </summary>
    [DataTestMethod]
    [DataRow("1e-05")]
    [DataRow("1.5e-07")]
    [DataRow("1e+16")]
    [DataRow("-2.5e-11")]
    [DataRow("3.0")]
    [DataRow("0.25")]
    public void Hash_IgnoresHowEitherSideSpelledANumber(string pythonSpelling)
    {
        // What Python sent, kept exactly as its own JSON writer wrote it.
        var fromPython = JsonNode.Parse($"[{pythonSpelling}]");

        // The same value after a round trip through the store, rewritten by System.Text.Json.
        HostVariables.TryToJson(HostVariables.FromJson(fromPython), out var fromStore);

        Assert.AreEqual(
            HostVariables.ComputeHash(fromPython),
            HostVariables.ComputeHash(fromStore),
            $"{pythonSpelling} was re-sent to Python because the two sides spelled it differently");
    }

    [TestMethod]
    public void Hash_StillSeparatesNumbersTooCloseForADoubleToTell()
    {
        // Adjacent integers past a double's exact range. Rewriting numbers must not round them
        // together, or a changed value would look unchanged and never reach the store.
        var first = JsonNode.Parse("[9007199254740993]");
        var second = JsonNode.Parse("[9007199254740992]");

        Assert.AreNotEqual(HostVariables.ComputeHash(first), HostVariables.ComputeHash(second));
    }

    [TestMethod]
    public void Hash_SeparatesValuesThatDifferOnlyInWhereAKeyEnds()
    {
        var first = JsonNode.Parse("{\"a\":\"b\",\"c\":\"d\"}");
        var second = JsonNode.Parse("{\"a\":\"bc\",\"\":\"d\"}");

        Assert.AreNotEqual(HostVariables.ComputeHash(first), HostVariables.ComputeHash(second));
    }

    // --- bootstrap configuration ---

    [TestMethod]
    public void Bootstrap_CarriesTheWidgetAssetSourceAsTheSubprocessSpellsIt()
    {
        var config = new HostBootstrap(
            Array.Empty<string>(), null, WidgetAssets: Verso.Python.Kernel.WidgetAssetSource.Bundled).ToJson();

        Assert.AreEqual("bundled", config["widget_asset_source"]!.GetValue<string>());
    }

    [TestMethod]
    public void Bootstrap_DefaultsToThePublishedBundle()
    {
        var config = new HostBootstrap(Array.Empty<string>(), null).ToJson();

        Assert.AreEqual("cdn", config["widget_asset_source"]!.GetValue<string>());
    }
}
