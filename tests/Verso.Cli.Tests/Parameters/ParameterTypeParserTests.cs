using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Parameters;

namespace Verso.Cli.Tests.Parameters;

[TestClass]
public class ParameterTypeParserTests
{
    // --- string ---

    [TestMethod]
    public void String_PassesThrough()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("string", "hello", out var result, out _));
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void String_EmptyString_PassesThrough()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("string", "", out var result, out _));
        Assert.AreEqual("", result);
    }

    // --- int ---

    [TestMethod]
    public void Int_ValidPositive()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("int", "1000", out var result, out _));
        Assert.AreEqual(1000L, result);
    }

    [TestMethod]
    public void Int_ValidNegative()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("int", "-42", out var result, out _));
        Assert.AreEqual(-42L, result);
    }

    [TestMethod]
    public void Int_Zero()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("int", "0", out var result, out _));
        Assert.AreEqual(0L, result);
    }

    [TestMethod]
    public void Int_InvalidString_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("int", "abc", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "integer");
    }

    [TestMethod]
    public void Int_FloatValue_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("int", "3.14", out _, out var error));
        Assert.IsNotNull(error);
    }

    // --- float ---

    [TestMethod]
    public void Float_ValidDecimal()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("float", "3.14", out var result, out _));
        Assert.AreEqual(3.14, result);
    }

    [TestMethod]
    public void Float_ValidInteger()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("float", "42", out var result, out _));
        Assert.AreEqual(42.0, result);
    }

    [TestMethod]
    public void Float_Negative()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("float", "-0.5", out var result, out _));
        Assert.AreEqual(-0.5, result);
    }

    [TestMethod]
    public void Float_InvalidString_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("float", "notanumber", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "numeric");
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
        Assert.IsTrue(ParameterTypeParser.TryParse("bool", input, out var result, out _));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Bool_InvalidValue_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("bool", "maybe", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "true/false");
    }

    // --- date ---

    [TestMethod]
    public void Date_ValidIso()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("date", "2024-01-15", out var result, out _));
        Assert.AreEqual(new DateOnly(2024, 1, 15), result);
    }

    [TestMethod]
    public void Date_WrongFormat_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("date", "01/15/2024", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "yyyy-MM-dd");
    }

    [TestMethod]
    public void Date_InvalidDate_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("date", "not-a-date", out _, out _));
    }

    // --- datetime ---

    [TestMethod]
    public void DateTime_WithTimezone()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("datetime", "2024-01-15T08:00:00+05:00", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(5, dto.Offset.Hours);
    }

    [TestMethod]
    public void DateTime_WithUtcZ()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("datetime", "2024-01-15T08:00:00Z", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(TimeSpan.Zero, dto.Offset);
    }

    [TestMethod]
    public void DateTime_WithoutTimezone_DefaultsToUtc()
    {
        Assert.IsTrue(ParameterTypeParser.TryParse("datetime", "2024-01-15T08:00:00", out var result, out _));
        var dto = (DateTimeOffset)result!;
        Assert.AreEqual(TimeSpan.Zero, dto.Offset);
    }

    [TestMethod]
    public void DateTime_Invalid_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("datetime", "not-a-datetime", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "ISO 8601");
    }

    // --- unknown type ---

    [TestMethod]
    public void UnknownType_Fails()
    {
        Assert.IsFalse(ParameterTypeParser.TryParse("complex", "value", out _, out var error));
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "Unknown parameter type");
    }
}
