using Verso.Python.Helpers;

namespace Verso.Python.Tests.Helpers;

[TestClass]
public sealed class JediTypeMapperTests
{
    [TestMethod]
    public void Map_Function_ReturnsMethod() =>
        Assert.AreEqual("Method", JediTypeMapper.Map("function"));

    [TestMethod]
    public void Map_Class_ReturnsClass() =>
        Assert.AreEqual("Class", JediTypeMapper.Map("class"));

    [TestMethod]
    public void Map_Instance_ReturnsVariable() =>
        Assert.AreEqual("Variable", JediTypeMapper.Map("instance"));

    [TestMethod]
    public void Map_Module_ReturnsNamespace() =>
        Assert.AreEqual("Namespace", JediTypeMapper.Map("module"));

    [TestMethod]
    public void Map_Keyword_ReturnsKeyword() =>
        Assert.AreEqual("Keyword", JediTypeMapper.Map("keyword"));

    [TestMethod]
    public void Map_Statement_ReturnsVariable() =>
        Assert.AreEqual("Variable", JediTypeMapper.Map("statement"));

    [TestMethod]
    public void Map_Param_ReturnsVariable() =>
        Assert.AreEqual("Variable", JediTypeMapper.Map("param"));

    [TestMethod]
    public void Map_Property_ReturnsProperty() =>
        Assert.AreEqual("Property", JediTypeMapper.Map("property"));

    [TestMethod]
    public void Map_Unknown_ReturnsText() =>
        Assert.AreEqual("Text", JediTypeMapper.Map("something_else"));

    [TestMethod]
    public void Map_EmptyString_ReturnsText() =>
        Assert.AreEqual("Text", JediTypeMapper.Map(""));
}
