using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class PrimitiveFormatterTests
{
    private readonly PrimitiveFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.primitive", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_IsZero()
        => Assert.AreEqual(0, _formatter.Priority);

    [TestMethod]
    [DataRow(typeof(string))]
    [DataRow(typeof(int))]
    [DataRow(typeof(long))]
    [DataRow(typeof(float))]
    [DataRow(typeof(double))]
    [DataRow(typeof(decimal))]
    [DataRow(typeof(bool))]
    [DataRow(typeof(DateTime))]
    [DataRow(typeof(DateTimeOffset))]
    [DataRow(typeof(char))]
    [DataRow(typeof(Guid))]
    public void SupportedTypes_ContainsType(Type type)
    {
        Assert.IsTrue(_formatter.SupportedTypes.Contains(type), $"Missing supported type: {type.Name}");
    }

    [TestMethod]
    public void CanFormat_String_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public void CanFormat_Int_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(42, _context));

    [TestMethod]
    public void CanFormat_Long_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(42L, _context));

    [TestMethod]
    public void CanFormat_Float_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(3.14f, _context));

    [TestMethod]
    public void CanFormat_Double_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(3.14, _context));

    [TestMethod]
    public void CanFormat_Decimal_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(3.14m, _context));

    [TestMethod]
    public void CanFormat_Bool_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(true, _context));

    [TestMethod]
    public void CanFormat_DateTime_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(DateTime.Now, _context));

    [TestMethod]
    public void CanFormat_DateTimeOffset_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(DateTimeOffset.Now, _context));

    [TestMethod]
    public void CanFormat_Char_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat('x', _context));

    [TestMethod]
    public void CanFormat_Guid_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(Guid.NewGuid(), _context));

    [TestMethod]
    public void CanFormat_Object_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new object(), _context));

    [TestMethod]
    public void CanFormat_List_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new List<int>(), _context));

    [TestMethod]
    public async Task FormatAsync_ProducesTextPlain()
    {
        var result = await _formatter.FormatAsync(42, _context);
        Assert.AreEqual("text/plain", result.MimeType);
        Assert.AreEqual("42", result.Content);
    }

    [TestMethod]
    public async Task FormatAsync_String_ProducesCorrectContent()
    {
        var result = await _formatter.FormatAsync("hello", _context);
        Assert.AreEqual("text/plain", result.MimeType);
        Assert.AreEqual("hello", result.Content);
    }

    [TestMethod]
    public async Task FormatAsync_Bool_ProducesCorrectContent()
    {
        var result = await _formatter.FormatAsync(true, _context);
        Assert.AreEqual("text/plain", result.MimeType);
        Assert.AreEqual("True", result.Content);
    }
}
