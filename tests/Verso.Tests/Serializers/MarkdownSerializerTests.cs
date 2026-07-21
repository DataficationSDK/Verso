using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public sealed class MarkdownSerializerTests
{
    private readonly MarkdownSerializer _serializer = new();

    // --- Metadata and CanImport ---

    [TestMethod]
    public void ExtensionMetadata_IsCorrect()
    {
        Assert.AreEqual("verso.serializer.markdown", _serializer.ExtensionId);
        Assert.AreEqual("markdown", _serializer.FormatId);
        Assert.AreEqual(1, _serializer.FileExtensions.Count);
        Assert.AreEqual(".md", _serializer.FileExtensions[0]);
        Assert.IsTrue(_serializer.PreservesFormatByDefault);
    }

    [TestMethod]
    public void CanImport_Md_ReturnsTrue()
    {
        Assert.IsTrue(_serializer.CanImport("notes.md"));
        Assert.IsTrue(_serializer.CanImport("README.MD"));
    }

    [TestMethod]
    public void CanImport_NonMd_ReturnsFalse()
    {
        Assert.IsFalse(_serializer.CanImport("notebook.verso"));
        Assert.IsFalse(_serializer.CanImport("notebook.ipynb"));
        Assert.IsFalse(_serializer.CanImport("notebook.dib"));
    }

    // --- Deserialization: fence recognition ---

    [TestMethod]
    public async Task Deserialize_CSharpFence_BecomesCodeCell()
    {
        var md = "# Title\n\n```csharp\nvar x = 1;\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(2, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        Assert.AreEqual("# Title", notebook.Cells[0].Source);
        Assert.AreEqual("code", notebook.Cells[1].Type);
        Assert.AreEqual("csharp", notebook.Cells[1].Language);
        Assert.AreEqual("var x = 1;", notebook.Cells[1].Source);
        Assert.AreEqual("```csharp", notebook.Cells[1].Metadata[MarkdownSerializer.FenceMetadataKey]);
    }

    [TestMethod]
    public async Task Deserialize_AliasTag_MapsLanguageAndKeepsFence()
    {
        var md = "```cs\nvar x = 1;\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
        Assert.AreEqual("```cs", notebook.Cells[0].Metadata[MarkdownSerializer.FenceMetadataKey]);
    }

    [TestMethod]
    public async Task Deserialize_CellTypeTags_MapToCellTypes()
    {
        var md = "```mermaid\ngraph TD;\n```\n\n```sql\nSELECT 1;\n```\n\n```html\n<b>hi</b>\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(3, notebook.Cells.Count);
        Assert.AreEqual("mermaid", notebook.Cells[0].Type);
        Assert.IsNull(notebook.Cells[0].Language);
        Assert.AreEqual("sql", notebook.Cells[1].Type);
        Assert.AreEqual("html", notebook.Cells[2].Type);
    }

    [TestMethod]
    public async Task Deserialize_UnknownFence_StaysInline()
    {
        var md = "Before.\n\n```json\n{ \"a\": 1 }\n```\n\nAfter.\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        StringAssert.Contains(notebook.Cells[0].Source, "```json");
    }

    [TestMethod]
    public async Task Deserialize_BareFence_StaysInline()
    {
        var md = "Text.\n\n```\nplain output\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
    }

    [TestMethod]
    public async Task Deserialize_MarkdownTaggedFence_StaysInline()
    {
        var md = "Example:\n\n```markdown\n# Sample heading\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        StringAssert.Contains(notebook.Cells[0].Source, "```markdown");
    }

    [TestMethod]
    public async Task Deserialize_BlockquoteNestedFence_StaysInline()
    {
        var md = "> quoted\n> ```csharp\n> var x = 1;\n> ```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
    }

    [TestMethod]
    public async Task Deserialize_ListNestedFence_StaysInline()
    {
        var md = "- item\n\n  ```csharp\n  var x = 1;\n  ```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
    }

    [TestMethod]
    public async Task Deserialize_IndentedCodeBlock_StaysInline()
    {
        var md = "Text.\n\n    var x = 1;\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
    }

    [TestMethod]
    public async Task Deserialize_FourBacktickFence_KeepsInnerBackticks()
    {
        var md = "````csharp\n```\nvar x = 1;\n```\n````\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("```\nvar x = 1;\n```", notebook.Cells[0].Source);
        Assert.AreEqual("````csharp", notebook.Cells[0].Metadata[MarkdownSerializer.FenceMetadataKey]);
    }

    [TestMethod]
    public async Task Deserialize_TildeFence_BecomesCodeCell()
    {
        var md = "~~~python\nprint(1)\n~~~\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("python", notebook.Cells[0].Language);
        Assert.AreEqual("print(1)", notebook.Cells[0].Source);
        Assert.AreEqual("~~~python", notebook.Cells[0].Metadata[MarkdownSerializer.FenceMetadataKey]);
    }

    [TestMethod]
    public async Task Deserialize_UnterminatedFence_RunsToEndOfFile()
    {
        var md = "Before.\n\n```csharp\nvar x = 1;";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(2, notebook.Cells.Count);
        Assert.AreEqual("markdown", notebook.Cells[0].Type);
        Assert.AreEqual("var x = 1;", notebook.Cells[1].Source);
    }

    [TestMethod]
    public async Task Deserialize_FenceAtStart_NoLeadingMarkdownCell()
    {
        var md = "```csharp\nvar x = 1;\n```\n\nAfter.\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(2, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("markdown", notebook.Cells[1].Type);
        Assert.AreEqual("After.", notebook.Cells[1].Source);
    }

    [TestMethod]
    public async Task Deserialize_EmptyContent_NoCells()
    {
        var notebook = await _serializer.DeserializeAsync("");

        Assert.AreEqual(0, notebook.Cells.Count);
    }

    // --- Serialization ---

    [TestMethod]
    public async Task Serialize_EmptyNotebook_EmptyString()
    {
        var result = await _serializer.SerializeAsync(new NotebookModel());

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public async Task Serialize_OutputsDropped()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "var x = 1;",
            Outputs = { new CellOutput("text/plain", "the output value") },
        });

        var result = await _serializer.SerializeAsync(notebook);

        Assert.IsFalse(result.Contains("the output value"));
    }

    [TestMethod]
    public async Task Serialize_NewCodeCell_CanonicalFenceTag()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "code", Language = "powershell", Source = "Get-Date" });

        var result = await _serializer.SerializeAsync(notebook);

        Assert.AreEqual("```powershell\nGet-Date\n```\n", result);
    }

    [TestMethod]
    public async Task Serialize_UnrepresentableCell_TaggedFence()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "chart", Language = null, Source = "{ \"kind\": \"bar\" }" });
        notebook.Cells.Add(new CellModel { Type = "code", Language = "ruby", Source = "puts 1" });

        var result = await _serializer.SerializeAsync(notebook);

        StringAssert.Contains(result, "```chart\n{ \"kind\": \"bar\" }\n```");
        StringAssert.Contains(result, "```ruby\nputs 1\n```");
    }

    [TestMethod]
    public async Task Serialize_AdjacentMarkdownCells_MergeOnReload()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "markdown", Source = "First." });
        notebook.Cells.Add(new CellModel { Type = "markdown", Source = "Second." });

        var serialized = await _serializer.SerializeAsync(notebook);
        var reloaded = await _serializer.DeserializeAsync(serialized);

        Assert.AreEqual("First.\n\nSecond.", serialized.TrimEnd('\n'));
        Assert.AreEqual(1, reloaded.Cells.Count);
        Assert.AreEqual("First.\n\nSecond.", reloaded.Cells[0].Source);
    }

    [TestMethod]
    public async Task Serialize_EmptyCodeCell_NoExtraBlankLine()
    {
        var notebook = await _serializer.DeserializeAsync("```csharp\n```\n");

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("", notebook.Cells[0].Source);
        Assert.AreEqual("```csharp\n```\n", await _serializer.SerializeAsync(notebook));
    }

    // --- Round trips ---

    [TestMethod]
    public async Task RoundTrip_CanonicalFile_ByteIdentical()
    {
        var md = "# Title\n\nSome prose.\n\n```csharp\nvar x = 1;\n```\n\nMore prose with `inline code`.\n\n~~~python\nprint(1)\n~~~\n";

        var notebook = await _serializer.DeserializeAsync(md);
        var result = await _serializer.SerializeAsync(notebook);

        Assert.AreEqual(md, result);
    }

    [TestMethod]
    public async Task RoundTrip_AliasFenceTag_DoesNotChurn()
    {
        var md = "```cs\nvar x = 1;\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);
        var result = await _serializer.SerializeAsync(notebook);

        Assert.AreEqual(md, result);
    }

    [TestMethod]
    public async Task RoundTrip_OddSpacing_NormalizesOnceThenFixedPoint()
    {
        var md = "# Title\n\n\n\n```csharp\nvar x = 1;\n```";

        var first = await _serializer.SerializeAsync(await _serializer.DeserializeAsync(md));
        var second = await _serializer.SerializeAsync(await _serializer.DeserializeAsync(first));

        Assert.AreEqual("# Title\n\n```csharp\nvar x = 1;\n```\n", first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task RoundTrip_CrLf_Preserved()
    {
        var md = "# Title\r\n\r\n```csharp\r\nvar x = 1;\r\n```\r\n";

        var notebook = await _serializer.DeserializeAsync(md);
        var result = await _serializer.SerializeAsync(notebook);

        Assert.AreEqual("var x = 1;", notebook.Cells[1].Source);
        Assert.AreEqual(md, result);
    }

    [TestMethod]
    public async Task Deserialize_BareCarriageReturns_ParseAsLineBreaks()
    {
        // Markdig counts a lone \r as a line break; the offset math must agree with it
        // or fence positions land on the wrong lines (or past the end of the table).
        var md = "A\rB\rC\rD\r```csharp\nvar x = 1;\n```\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(2, notebook.Cells.Count);
        Assert.AreEqual("A\nB\nC\nD", notebook.Cells[0].Source);
        Assert.AreEqual("var x = 1;", notebook.Cells[1].Source);
        Assert.AreEqual("```csharp", notebook.Cells[1].Metadata[MarkdownSerializer.FenceMetadataKey]);
    }

    [TestMethod]
    public async Task Deserialize_SingleBareCarriageReturn_KeepsFenceContentIntact()
    {
        var md = "Intro\r```csharp\nvar x = 1;\n```\nAfter.\n";

        var notebook = await _serializer.DeserializeAsync(md);

        Assert.AreEqual(3, notebook.Cells.Count);
        Assert.AreEqual("Intro", notebook.Cells[0].Source);
        Assert.AreEqual("var x = 1;", notebook.Cells[1].Source);
        Assert.AreEqual("```csharp", notebook.Cells[1].Metadata[MarkdownSerializer.FenceMetadataKey]);
        Assert.AreEqual("After.", notebook.Cells[2].Source);
    }

    [TestMethod]
    public async Task RoundTrip_UnknownFenceInProse_Preserved()
    {
        var md = "Before.\n\n```json\n{ \"a\": 1 }\n```\n\nAfter.\n";

        var notebook = await _serializer.DeserializeAsync(md);
        var result = await _serializer.SerializeAsync(notebook);

        Assert.AreEqual(md, result);
    }
}
