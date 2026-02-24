using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public sealed class DibSerializerTests
{
    private readonly DibSerializer _serializer = new();

    [TestMethod]
    public void ExtensionMetadata_IsCorrect()
    {
        Assert.AreEqual("verso.serializer.dib", _serializer.ExtensionId);
        Assert.AreEqual("dib", _serializer.FormatId);
        Assert.AreEqual(1, _serializer.FileExtensions.Count);
        Assert.AreEqual(".dib", _serializer.FileExtensions[0]);
    }

    [TestMethod]
    public void CanImport_Dib_ReturnsTrue()
    {
        Assert.IsTrue(_serializer.CanImport("notebook.dib"));
    }

    [TestMethod]
    public void CanImport_UpperCase_ReturnsTrue()
    {
        Assert.IsTrue(_serializer.CanImport("Notebook.DIB"));
    }

    [TestMethod]
    public void CanImport_NonDib_ReturnsFalse()
    {
        Assert.IsFalse(_serializer.CanImport("notebook.ipynb"));
        Assert.IsFalse(_serializer.CanImport("notebook.verso"));
    }

    [TestMethod]
    public void SerializeAsync_ThrowsNotSupported()
    {
        Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => _serializer.SerializeAsync(new NotebookModel()));
    }

    [TestMethod]
    public async Task Deserialize_SingleCSharpCell_NoMagicLine()
    {
        var dib = "Console.WriteLine(\"Hello\");";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
        Assert.AreEqual("Console.WriteLine(\"Hello\");", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_MultiLanguageCells()
    {
        var dib = @"#!csharp
Console.WriteLine(""Hello"");

#!fsharp
printfn ""Hello from F#""

#!markdown
## A heading
Some text.";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(3, notebook.Cells.Count);

        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
        Assert.AreEqual("Console.WriteLine(\"Hello\");", notebook.Cells[0].Source);

        Assert.AreEqual("code", notebook.Cells[1].Type);
        Assert.AreEqual("fsharp", notebook.Cells[1].Language);
        Assert.AreEqual("printfn \"Hello from F#\"", notebook.Cells[1].Source);

        Assert.AreEqual("markdown", notebook.Cells[2].Type);
        Assert.IsNull(notebook.Cells[2].Language);
        Assert.AreEqual("## A heading\nSome text.", notebook.Cells[2].Source);
    }

    [TestMethod]
    public async Task Deserialize_MetaBlock_ExtractsDefaultKernel()
    {
        var dib = @"#!meta
{""kernelInfo"":{""defaultKernelName"":""fsharp"",""items"":[]}}

#!fsharp
printfn ""Hello""";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual("fsharp", notebook.DefaultKernelId);
        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("fsharp", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_MetaBlock_DefaultKernelUsedForPrefixContent()
    {
        var dib = @"#!meta
{""kernelInfo"":{""defaultKernelName"":""fsharp"",""items"":[]}}

printfn ""Hello""";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual("fsharp", notebook.DefaultKernelId);
        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("fsharp", notebook.Cells[0].Language);
        Assert.AreEqual("printfn \"Hello\"", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_SkipsEmptyCells()
    {
        var dib = @"#!csharp

#!fsharp

#!csharp
var x = 1;";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
        Assert.AreEqual("var x = 1;", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_TrimsTrailingWhitespace()
    {
        var dib = "#!csharp\nvar x = 1;\n\n\n";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("var x = 1;", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_LanguageAliases_CSharp()
    {
        var dib = "#!c#\nvar x = 1;";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_LanguageAliases_FSharp()
    {
        var dib = "#!f#\nlet x = 1";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("fsharp", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_LanguageAliases_PowerShell()
    {
        var dib = "#!pwsh\nGet-Process";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("powershell", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_LanguageAliases_JavaScript()
    {
        var dib = "#!js\nconsole.log('hi');";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("javascript", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_UnknownDirective_PreservesAsLanguage()
    {
        var dib = "#!kql\nStormEvents | take 10";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("kql", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_OutputsAlwaysEmpty()
    {
        var dib = "#!csharp\nConsole.WriteLine(\"test\");";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(0, notebook.Cells[0].Outputs.Count);
    }

    [TestMethod]
    public async Task Deserialize_HtmlDirective()
    {
        var dib = "#!html\n<h1>Hello</h1>";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("html", notebook.Cells[0].Type);
        Assert.IsNull(notebook.Cells[0].Language);
        Assert.AreEqual("<h1>Hello</h1>", notebook.Cells[0].Source);
    }

    [TestMethod]
    public async Task Deserialize_SqlDirective()
    {
        var dib = "#!sql\nSELECT * FROM Orders";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("sql", notebook.Cells[0].Type);
        Assert.IsNull(notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_MermaidDirective()
    {
        var dib = "#!mermaid\ngraph TD\n  A-->B";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("mermaid", notebook.Cells[0].Type);
        Assert.IsNull(notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_ValueDirective()
    {
        var dib = "#!value\nsome data";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(1, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("value", notebook.Cells[0].Language);
    }

    [TestMethod]
    public async Task Deserialize_EmptyContent_ReturnsEmptyNotebook()
    {
        var notebook = await _serializer.DeserializeAsync("");

        Assert.AreEqual(0, notebook.Cells.Count);
    }

    [TestMethod]
    public async Task Deserialize_ContentBeforeFirstDirective_DefaultsToCSharp()
    {
        var dib = @"var greeting = ""Hello"";
Console.WriteLine(greeting);

#!markdown
## Notes";

        var notebook = await _serializer.DeserializeAsync(dib);

        Assert.AreEqual(2, notebook.Cells.Count);
        Assert.AreEqual("code", notebook.Cells[0].Type);
        Assert.AreEqual("csharp", notebook.Cells[0].Language);
        Assert.AreEqual("markdown", notebook.Cells[1].Type);
    }
}
