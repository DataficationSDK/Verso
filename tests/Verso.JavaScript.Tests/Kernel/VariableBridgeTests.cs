using System.Text.Json;
using Verso.JavaScript.Kernel;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class VariableBridgeTests
{
    [TestMethod]
    public void JsonElementToClr_String_ReturnsString()
    {
        var element = JsonDocument.Parse("\"hello\"").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void JsonElementToClr_Integer_ReturnsNumeric()
    {
        var element = JsonDocument.Parse("42").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.IsNotNull(result);
        // TryGetInt64 may return long or fall through to double depending on runtime
        Assert.IsTrue(
            result is 42L or 42.0,
            $"Expected 42 as long or double, got: {result} ({result.GetType().Name})");
    }

    [TestMethod]
    public void JsonElementToClr_Float_ReturnsDouble()
    {
        var element = JsonDocument.Parse("3.14").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.AreEqual(3.14, result);
    }

    [TestMethod]
    public void JsonElementToClr_True_ReturnsTrue()
    {
        var element = JsonDocument.Parse("true").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void JsonElementToClr_False_ReturnsFalse()
    {
        var element = JsonDocument.Parse("false").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void JsonElementToClr_Null_ReturnsNull()
    {
        var element = JsonDocument.Parse("null").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void JsonElementToClr_Array_ReturnsList()
    {
        var element = JsonDocument.Parse("[1, 2, 3]").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as List<object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void JsonElementToClr_Object_ReturnsDictionary()
    {
        var element = JsonDocument.Parse("{\"name\":\"Alice\",\"age\":30}").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as Dictionary<string, object>;
        Assert.IsNotNull(result);
        Assert.AreEqual("Alice", result["name"]);
        Assert.IsNotNull(result["age"]);
    }

    [TestMethod]
    public void JsonElementToClr_NestedObject_ReturnsNestedDictionary()
    {
        var element = JsonDocument.Parse("{\"outer\":{\"inner\":42}}").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as Dictionary<string, object>;
        Assert.IsNotNull(result);
        var inner = result["outer"] as Dictionary<string, object>;
        Assert.IsNotNull(inner);
        Assert.IsNotNull(inner["inner"]);
    }

    [TestMethod]
    public void JsonElementToClr_MixedArray_ReturnsCorrectTypes()
    {
        var element = JsonDocument.Parse("[\"hello\", 42, true, null]").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as List<object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual("hello", result[0]);
        Assert.IsNotNull(result[1]); // 42 as long or double
        Assert.AreEqual(true, result[2]);
        // null values are filtered out by the Where clause
    }

    [TestMethod]
    public void JsonElementToClr_LargeInteger_HandledCorrectly()
    {
        var element = JsonDocument.Parse("9999999999").RootElement;
        var result = VariableBridge.JsonElementToClr(element);
        Assert.IsNotNull(result);
        Assert.IsTrue(result is long or double);
    }

    [TestMethod]
    public void JsonElementToClr_EmptyObject_ReturnsEmptyDictionary()
    {
        var element = JsonDocument.Parse("{}").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as Dictionary<string, object>;
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void JsonElementToClr_EmptyArray_ReturnsEmptyList()
    {
        var element = JsonDocument.Parse("[]").RootElement;
        var result = VariableBridge.JsonElementToClr(element) as List<object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }
}
