using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers the translation from editor-assistance replies to the models the editors consume. No
/// subprocess is involved: the point is that a reply which is absent, incomplete, or malformed
/// produces no assistance rather than an error, since a bad frame must not surface in a notebook.
/// </summary>
[TestClass]
public sealed class IntelliSenseMappingTests
{
    private static JsonObject Reply(string field, JsonNode? payload)
        => new() { [field] = payload };

    // --- completions ---

    [TestMethod]
    public void Completions_MapNameKindAndDocumentation()
    {
        var reply = Reply("completions", new JsonArray
        {
            new JsonObject
            {
                ["name"] = "append",
                ["insert"] = "append",
                ["kind"] = "function",
                ["doc"] = "Append an item.",
            },
        });

        var completions = HostIntelliSense.MapCompletions(reply);

        Assert.AreEqual(1, completions.Count);
        Assert.AreEqual("append", completions[0].DisplayText);
        Assert.AreEqual("append", completions[0].InsertText);
        Assert.AreEqual("Method", completions[0].Kind);
        Assert.AreEqual("Append an item.", completions[0].Description);
    }

    [TestMethod]
    public void Completions_WithoutAKindFallBackToText()
    {
        // The standard-library completer reports no kind, and an editor still needs one.
        var reply = Reply("completions", new JsonArray
        {
            new JsonObject { ["name"] = "len", ["insert"] = "len", ["kind"] = "", ["doc"] = "" },
        });

        var completions = HostIntelliSense.MapCompletions(reply);

        Assert.AreEqual("Text", completions[0].Kind);
        Assert.IsNull(completions[0].Description);
    }

    [TestMethod]
    public void Completions_WithoutInsertTextUseTheirName()
    {
        var reply = Reply("completions", new JsonArray { new JsonObject { ["name"] = "path" } });

        var completions = HostIntelliSense.MapCompletions(reply);

        Assert.AreEqual("path", completions[0].InsertText);
    }

    [TestMethod]
    public void Completions_WithoutANameAreDropped()
    {
        var reply = Reply("completions", new JsonArray
        {
            new JsonObject { ["kind"] = "function" },
            new JsonObject { ["name"] = "" },
            new JsonObject { ["name"] = "keep" },
        });

        var completions = HostIntelliSense.MapCompletions(reply);

        Assert.AreEqual(1, completions.Count);
        Assert.AreEqual("keep", completions[0].DisplayText);
    }

    [TestMethod]
    public void Completions_FromAnAbsentOrMalformedReplyAreEmpty()
    {
        Assert.AreEqual(0, HostIntelliSense.MapCompletions(null).Count);
        Assert.AreEqual(0, HostIntelliSense.MapCompletions(new JsonObject()).Count);
        Assert.AreEqual(0, HostIntelliSense.MapCompletions(Reply("completions", "not a list")).Count);
        Assert.AreEqual(0, HostIntelliSense.MapCompletions(Reply("completions", new JsonArray())).Count);
    }

    // --- diagnostics ---

    [TestMethod]
    public void Diagnostics_MapPositionsAsTheyArrive()
    {
        var reply = Reply("diagnostics", new JsonArray
        {
            new JsonObject
            {
                ["line"] = 2,
                ["column"] = 6,
                ["end_line"] = 2,
                ["end_column"] = 7,
                ["message"] = "invalid syntax",
                ["code"] = "SyntaxError",
            },
        });

        var diagnostics = HostIntelliSense.MapDiagnostics(reply);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.AreEqual("invalid syntax", diagnostics[0].Message);
        Assert.AreEqual(2, diagnostics[0].StartLine);
        Assert.AreEqual(6, diagnostics[0].StartColumn);
        Assert.AreEqual(2, diagnostics[0].EndLine);
        Assert.AreEqual(7, diagnostics[0].EndColumn);
        Assert.AreEqual("SyntaxError", diagnostics[0].Code);
    }

    [TestMethod]
    public void Diagnostics_WithoutAnEndDefaultToOneCharacter()
    {
        var reply = Reply("diagnostics", new JsonArray
        {
            new JsonObject { ["line"] = 0, ["column"] = 4, ["message"] = "invalid syntax" },
        });

        var diagnostics = HostIntelliSense.MapDiagnostics(reply);

        Assert.AreEqual(0, diagnostics[0].EndLine);
        Assert.AreEqual(5, diagnostics[0].EndColumn);
    }

    [TestMethod]
    public void Diagnostics_NeverEndBeforeTheyStart()
    {
        var reply = Reply("diagnostics", new JsonArray
        {
            new JsonObject
            {
                ["line"] = 3,
                ["column"] = 2,
                ["end_line"] = 1,
                ["end_column"] = 0,
                ["message"] = "invalid syntax",
            },
        });

        var diagnostics = HostIntelliSense.MapDiagnostics(reply);

        Assert.AreEqual(3, diagnostics[0].EndLine);
    }

    [TestMethod]
    public void Diagnostics_WithoutAMessageAreDropped()
    {
        var reply = Reply("diagnostics", new JsonArray
        {
            new JsonObject { ["line"] = 0, ["column"] = 0 },
        });

        Assert.AreEqual(0, HostIntelliSense.MapDiagnostics(reply).Count);
    }

    [TestMethod]
    public void Diagnostics_FromAnAbsentOrMalformedReplyAreEmpty()
    {
        Assert.AreEqual(0, HostIntelliSense.MapDiagnostics(null).Count);
        Assert.AreEqual(0, HostIntelliSense.MapDiagnostics(new JsonObject()).Count);
        Assert.AreEqual(0, HostIntelliSense.MapDiagnostics(Reply("diagnostics", 7)).Count);
    }

    // --- hover ---

    [TestMethod]
    public void Hover_CarriesContentAndTheIdentifierRange()
    {
        var reply = Reply("hover", new JsonObject { ["content"] = "builtins.len\n\nReturn the length." });
        const string code = "total = len(items)";

        // Cursor inside "len", which is what the range has to cover.
        var info = HostIntelliSense.MapHover(reply, code, 9);

        Assert.IsNotNull(info);
        StringAssert.Contains(info!.Content, "builtins.len");
        Assert.IsNotNull(info.Range);
        Assert.AreEqual((0, 8, 0, 11), info.Range!.Value);
    }

    [TestMethod]
    public void Hover_RangeIsRelativeToTheCursorsOwnLine()
    {
        var reply = Reply("hover", new JsonObject { ["content"] = "value" });
        const string code = "first = 1\nsecond = value";

        var info = HostIntelliSense.MapHover(reply, code, 20);

        Assert.IsNotNull(info!.Range);
        Assert.AreEqual(1, info.Range!.Value.StartLine);
        Assert.AreEqual(9, info.Range.Value.StartColumn);
        Assert.AreEqual(14, info.Range.Value.EndColumn);
    }

    [TestMethod]
    public void Hover_OnWhitespaceHasNoRange()
    {
        var reply = Reply("hover", new JsonObject { ["content"] = "something" });

        var info = HostIntelliSense.MapHover(reply, "x = 1   ", 7);

        Assert.IsNotNull(info);
        Assert.IsNull(info!.Range);
    }

    [TestMethod]
    public void Hover_FromAnEmptyOrAbsentReplyIsNull()
    {
        Assert.IsNull(HostIntelliSense.MapHover(null, "len", 1));
        Assert.IsNull(HostIntelliSense.MapHover(new JsonObject(), "len", 1));
        Assert.IsNull(HostIntelliSense.MapHover(Reply("hover", null), "len", 1));
        Assert.IsNull(HostIntelliSense.MapHover(
            Reply("hover", new JsonObject { ["content"] = "" }), "len", 1));
    }
}
