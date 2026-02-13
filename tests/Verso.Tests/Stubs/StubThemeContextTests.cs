using Verso.Stubs;

namespace Verso.Tests.Stubs;

[TestClass]
public sealed class StubThemeContextTests
{
    private StubThemeContext _stub = null!;

    [TestInitialize]
    public void Setup() => _stub = new StubThemeContext();

    [TestMethod]
    public void ThemeKind_Returns_Light()
    {
        Assert.AreEqual(ThemeKind.Light, _stub.ThemeKind);
    }

    [TestMethod]
    public void GetColor_ReturnsKnownToken()
    {
        var color = _stub.GetColor("EditorBackground");
        Assert.AreEqual("#FFFFFF", color);
    }

    [TestMethod]
    public void GetColor_CaseInsensitive()
    {
        var color = _stub.GetColor("editorbackground");
        Assert.AreEqual("#FFFFFF", color);
    }

    [TestMethod]
    public void GetColor_UnknownToken_ReturnsEmpty()
    {
        Assert.AreEqual("", _stub.GetColor("NonExistentToken"));
    }

    [TestMethod]
    public void GetFont_ReturnsKnownFont()
    {
        var font = _stub.GetFont("EditorFont");
        Assert.AreEqual("Cascadia Code", font.Family);
        Assert.AreEqual(14, font.SizePx);
    }

    [TestMethod]
    public void GetFont_CaseInsensitive()
    {
        var font = _stub.GetFont("editorfont");
        Assert.AreEqual("Cascadia Code", font.Family);
    }

    [TestMethod]
    public void GetFont_UnknownRole_ReturnsFallback()
    {
        var font = _stub.GetFont("NonExistent");
        Assert.AreEqual("sans-serif", font.Family);
        Assert.AreEqual(13, font.SizePx);
    }

    [TestMethod]
    public void GetSpacing_ReturnsKnownToken()
    {
        Assert.AreEqual(12, _stub.GetSpacing("CellPadding"));
    }

    [TestMethod]
    public void GetSpacing_CaseInsensitive()
    {
        Assert.AreEqual(12, _stub.GetSpacing("cellpadding"));
    }

    [TestMethod]
    public void GetSpacing_UnknownToken_ReturnsZero()
    {
        Assert.AreEqual(0, _stub.GetSpacing("NonExistent"));
    }

    [TestMethod]
    public void GetSyntaxColor_ReturnsNull()
    {
        Assert.IsNull(_stub.GetSyntaxColor("keyword"));
    }

    [TestMethod]
    public void GetCustomToken_ReturnsNull()
    {
        Assert.IsNull(_stub.GetCustomToken("custom.key"));
    }

    [TestMethod]
    public void GetColor_StatusError_ReturnsExpected()
    {
        Assert.AreEqual("#DC3545", _stub.GetColor("StatusError"));
    }

    [TestMethod]
    public void GetSpacing_ToolbarHeight_ReturnsExpected()
    {
        Assert.AreEqual(40, _stub.GetSpacing("ToolbarHeight"));
    }
}
