namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class SupportingTypesTests
{
    [TestMethod]
    public void RenderResult_Construction()
    {
        var result = new RenderResult("text/html", "<b>hi</b>");
        Assert.AreEqual("text/html", result.MimeType);
        Assert.AreEqual("<b>hi</b>", result.Content);
    }

    [TestMethod]
    public void Completion_DefaultsAndConstruction()
    {
        var c = new Completion("Log", "Console.WriteLine", "Method");
        Assert.AreEqual("Log", c.DisplayText);
        Assert.AreEqual("Console.WriteLine", c.InsertText);
        Assert.AreEqual("Method", c.Kind);
        Assert.IsNull(c.Description);
        Assert.IsNull(c.SortText);
    }

    [TestMethod]
    public void Diagnostic_Construction()
    {
        var d = new Diagnostic(DiagnosticSeverity.Error, "Syntax error", 1, 0, 1, 5, Code: "CS1001");
        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("Syntax error", d.Message);
        Assert.AreEqual(1, d.StartLine);
        Assert.AreEqual(0, d.StartColumn);
        Assert.AreEqual(1, d.EndLine);
        Assert.AreEqual(5, d.EndColumn);
        Assert.AreEqual("CS1001", d.Code);
    }

    [TestMethod]
    public void HoverInfo_DefaultMimeType()
    {
        var h = new HoverInfo("Type: int");
        Assert.AreEqual("text/plain", h.MimeType);
        Assert.IsNull(h.Range);
    }

    [TestMethod]
    public void HoverInfo_WithRange()
    {
        var h = new HoverInfo("info", Range: (1, 0, 1, 5));
        Assert.IsNotNull(h.Range);
        Assert.AreEqual(1, h.Range!.Value.StartLine);
        Assert.AreEqual(5, h.Range.Value.EndColumn);
    }

    [TestMethod]
    public void CellContainerInfo_Defaults()
    {
        var id = Guid.NewGuid();
        var c = new CellContainerInfo(id, 0, 0, 800, 200);
        Assert.AreEqual(id, c.CellId);
        Assert.IsTrue(c.IsVisible);
    }

    [TestMethod]
    public void ParameterDefinition_Defaults()
    {
        var p = new ParameterDefinition("lang", "Language", typeof(string));
        Assert.AreEqual("lang", p.Name);
        Assert.AreEqual("Language", p.Description);
        Assert.AreEqual(typeof(string), p.ParameterType);
        Assert.IsFalse(p.IsRequired);
        Assert.IsNull(p.DefaultValue);
    }

    [TestMethod]
    public void VariableDescriptor_Construction()
    {
        var v = new VariableDescriptor("x", 42, typeof(int), KernelId: "csharp");
        Assert.AreEqual("x", v.Name);
        Assert.AreEqual(42, v.Value);
        Assert.AreEqual(typeof(int), v.Type);
        Assert.AreEqual("csharp", v.KernelId);
    }

    [TestMethod]
    public void FontDescriptor_Defaults()
    {
        var f = new FontDescriptor("Consolas", 14);
        Assert.AreEqual("Consolas", f.Family);
        Assert.AreEqual(14, f.SizePx);
        Assert.AreEqual(400, f.Weight);
        Assert.AreEqual(1.4, f.LineHeight);
    }

    [TestMethod]
    public void FontDescriptor_WithExpression()
    {
        var f = new FontDescriptor("Consolas", 14);
        var bold = f with { Weight = 700 };
        Assert.AreEqual(700, bold.Weight);
        Assert.AreEqual(14, bold.SizePx);
    }

    [TestMethod]
    public void RecordEquality_Completion()
    {
        var a = new Completion("X", "Y", "Method");
        var b = new Completion("X", "Y", "Method");
        Assert.AreEqual(a, b);
    }
}
