using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class ArgumentParserTests
{
    [TestMethod]
    public void Parse_SimpleKeyValue_ReturnsCorrectPair()
    {
        var result = ArgumentParser.Parse("--name mydb");

        Assert.AreEqual("mydb", result["name"]);
    }

    [TestMethod]
    public void Parse_MultipleKeyValues_ReturnsAll()
    {
        var result = ArgumentParser.Parse("--name mydb --connection-string Server=localhost");

        Assert.AreEqual("mydb", result["name"]);
        Assert.AreEqual("Server=localhost", result["connection-string"]);
    }

    [TestMethod]
    public void Parse_DoubleQuotedValue_StripsQuotes()
    {
        var result = ArgumentParser.Parse("--connection-string \"Server=localhost;Database=mydb\"");

        Assert.AreEqual("Server=localhost;Database=mydb", result["connection-string"]);
    }

    [TestMethod]
    public void Parse_SingleQuotedValue_StripsQuotes()
    {
        var result = ArgumentParser.Parse("--connection-string 'Server=localhost;Database=mydb'");

        Assert.AreEqual("Server=localhost;Database=mydb", result["connection-string"]);
    }

    [TestMethod]
    public void Parse_FlagArgument_StoredAsNull()
    {
        var result = ArgumentParser.Parse("--name mydb --default");

        Assert.AreEqual("mydb", result["name"]);
        Assert.IsTrue(result.ContainsKey("default"));
        Assert.IsNull(result["default"]);
    }

    [TestMethod]
    public void Parse_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = ArgumentParser.Parse("");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Parse_WhitespaceInput_ReturnsEmptyDictionary()
    {
        var result = ArgumentParser.Parse("   ");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Parse_KeysCaseInsensitive()
    {
        var result = ArgumentParser.Parse("--Name mydb");

        Assert.IsTrue(result.ContainsKey("name"));
        Assert.AreEqual("mydb", result["name"]);
    }

    [TestMethod]
    public void Parse_MixedQuotedAndUnquoted_ReturnsAll()
    {
        var result = ArgumentParser.Parse("--name mydb --connection-string \"Data Source=:memory:\" --default");

        Assert.AreEqual("mydb", result["name"]);
        Assert.AreEqual("Data Source=:memory:", result["connection-string"]);
        Assert.IsTrue(result.ContainsKey("default"));
    }

    [TestMethod]
    public void Parse_ValueWithoutKey_Ignored()
    {
        var result = ArgumentParser.Parse("orphan --name mydb");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("mydb", result["name"]);
    }

    [TestMethod]
    public void Parse_FlagFollowedByKey_BothParsed()
    {
        var result = ArgumentParser.Parse("--default --name mydb");

        Assert.IsTrue(result.ContainsKey("default"));
        Assert.IsNull(result["default"]);
        Assert.AreEqual("mydb", result["name"]);
    }
}
