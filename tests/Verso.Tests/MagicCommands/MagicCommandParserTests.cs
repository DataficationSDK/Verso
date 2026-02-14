using Verso.MagicCommands;

namespace Verso.Tests.MagicCommands;

[TestClass]
public sealed class MagicCommandParserTests
{
    [TestMethod]
    public void Parse_NoMagicPrefix_ReturnsFalse()
    {
        var result = MagicCommandParser.Parse("var x = 1;");

        Assert.IsFalse(result.IsMagicCommand);
        Assert.IsNull(result.CommandName);
        Assert.IsNull(result.Arguments);
        Assert.AreEqual("var x = 1;", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_TimeCommand_ExtractsNameAndEmptyArgs()
    {
        var result = MagicCommandParser.Parse("#!time");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("time", result.CommandName);
        Assert.AreEqual("", result.Arguments);
        Assert.AreEqual("", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_NuGetWithArgs_ExtractsCommandAndArguments()
    {
        var result = MagicCommandParser.Parse("#!nuget Newtonsoft.Json 13.0.1");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("nuget", result.CommandName);
        Assert.AreEqual("Newtonsoft.Json 13.0.1", result.Arguments);
        Assert.AreEqual("", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_TimeWithRemainingCode_ExtractsRemaining()
    {
        var result = MagicCommandParser.Parse("#!time\nvar x = 1;");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("time", result.CommandName);
        Assert.AreEqual("var x = 1;", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_LeadingWhitespace_StillDetected()
    {
        var result = MagicCommandParser.Parse("  #!time");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("time", result.CommandName);
    }

    [TestMethod]
    public void Parse_MagicMidCell_NotDetected()
    {
        var result = MagicCommandParser.Parse("var x = 1;\n#!time");

        Assert.IsFalse(result.IsMagicCommand);
    }

    [TestMethod]
    public void Parse_EmptyInput_ReturnsFalse()
    {
        var result = MagicCommandParser.Parse("");

        Assert.IsFalse(result.IsMagicCommand);
        Assert.AreEqual("", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_NullInput_ReturnsFalse()
    {
        var result = MagicCommandParser.Parse(null);

        Assert.IsFalse(result.IsMagicCommand);
        Assert.AreEqual("", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_LeadingBlankLines_StillDetected()
    {
        var result = MagicCommandParser.Parse("\n\n#!about");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("about", result.CommandName);
    }

    [TestMethod]
    public void Parse_WindowsLineEndings_HandledCorrectly()
    {
        var result = MagicCommandParser.Parse("#!time\r\nvar x = 1;");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("time", result.CommandName);
        Assert.AreEqual("var x = 1;", result.RemainingCode);
    }

    [TestMethod]
    public void Parse_MultipleRemainingLines_PreservesAll()
    {
        var result = MagicCommandParser.Parse("#!time\nvar x = 1;\nvar y = 2;");

        Assert.IsTrue(result.IsMagicCommand);
        Assert.AreEqual("var x = 1;\nvar y = 2;", result.RemainingCode);
    }
}
