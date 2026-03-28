using Verso.Parameters;

namespace Verso.Tests.Parameters;

[TestClass]
public sealed class ParameterValueParserTests
{
    // --- string ---

    [TestMethod]
    public void String_PassesThrough()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("string", "hello", out var result, out _));
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void String_EmptyValue()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("string", "", out var result, out _));
        Assert.AreEqual("", result);
    }

    // --- int ---

    [TestMethod]
    public void Int_ValidPositive()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("int", "42", out var result, out _));
        Assert.AreEqual(42L, result);
    }

    [TestMethod]
    public void Int_ValidNegative()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("int", "-100", out var result, out _));
        Assert.AreEqual(-100L, result);
    }

    [TestMethod]
    public void Int_ValidZero()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("int", "0", out var result, out _));
        Assert.AreEqual(0L, result);
    }

    [TestMethod]
    public void Int_InvalidFloat()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("int", "3.14", out _, out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Int_InvalidText()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("int", "abc", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- float ---

    [TestMethod]
    public void Float_ValidDecimal()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("float", "3.14", out var result, out _));
        Assert.AreEqual(3.14, result);
    }

    [TestMethod]
    public void Float_ValidInteger()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("float", "42", out var result, out _));
        Assert.AreEqual(42.0, result);
    }

    [TestMethod]
    public void Float_ValidNegative()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("float", "-0.5", out var result, out _));
        Assert.AreEqual(-0.5, result);
    }

    [TestMethod]
    public void Float_InvalidText()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("float", "nope", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- bool ---

    [TestMethod]
    [DataRow("true", true)]
    [DataRow("True", true)]
    [DataRow("TRUE", true)]
    [DataRow("yes", true)]
    [DataRow("Yes", true)]
    [DataRow("1", true)]
    [DataRow("false", false)]
    [DataRow("False", false)]
    [DataRow("FALSE", false)]
    [DataRow("no", false)]
    [DataRow("No", false)]
    [DataRow("0", false)]
    public void Bool_ValidValues(string input, bool expected)
    {
        Assert.IsTrue(ParameterValueParser.TryParse("bool", input, out var result, out _));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Bool_InvalidValue()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("bool", "maybe", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- date ---

    [TestMethod]
    public void Date_ValidFormat()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("date", "2024-01-15", out var result, out _));
        Assert.AreEqual(new DateOnly(2024, 1, 15), result);
    }

    [TestMethod]
    public void Date_InvalidFormat()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("date", "01/15/2024", out _, out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Date_InvalidText()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("date", "not-a-date", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- datetime ---

    [TestMethod]
    public void Datetime_ValidIso8601()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("datetime", "2024-01-15T10:30:00Z", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(2024, dto.Year);
        Assert.AreEqual(1, dto.Month);
        Assert.AreEqual(15, dto.Day);
        Assert.AreEqual(TimeSpan.Zero, dto.Offset);
    }

    [TestMethod]
    public void Datetime_WithOffset()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("datetime", "2024-01-15T10:30:00+05:00", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(TimeSpan.FromHours(5), dto.Offset);
    }

    [TestMethod]
    public void Datetime_NoOffset_DefaultsToUtc()
    {
        Assert.IsTrue(ParameterValueParser.TryParse("datetime", "2024-01-15T10:30:00", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(TimeSpan.Zero, dto.Offset);
    }

    [TestMethod]
    public void Datetime_InvalidText()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("datetime", "not-a-datetime", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- Unknown type ---

    [TestMethod]
    public void UnknownType_ReturnsError()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("complex", "value", out _, out var error));
        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("Unknown parameter type"));
    }

    // --- SupportedTypes ---

    [TestMethod]
    public void SupportedTypes_ContainsAllExpected()
    {
        var types = ParameterValueParser.SupportedTypes;
        CollectionAssert.Contains(types.ToList(), "string");
        CollectionAssert.Contains(types.ToList(), "int");
        CollectionAssert.Contains(types.ToList(), "float");
        CollectionAssert.Contains(types.ToList(), "bool");
        CollectionAssert.Contains(types.ToList(), "date");
        CollectionAssert.Contains(types.ToList(), "datetime");
    }
}
