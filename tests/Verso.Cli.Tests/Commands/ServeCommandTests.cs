using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Commands;

namespace Verso.Cli.Tests.Commands;

[TestClass]
public class ServeCommandTests
{
    private Command _command = null!;

    [TestInitialize]
    public void Setup()
    {
        _command = ServeCommand.Create();
    }

    [TestMethod]
    public void Command_HasCorrectName()
    {
        Assert.AreEqual("serve", _command.Name);
    }

    [TestMethod]
    public void Command_HasDescription()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_command.Description));
    }

    [TestMethod]
    public void Parse_NoArguments_Succeeds()
    {
        var result = _command.Parse("");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_WithNotebook_Succeeds()
    {
        var result = _command.Parse("notebook.verso");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_PortOption_IsRecognized()
    {
        var result = _command.Parse("--port 9090");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_NoBrowserOption_IsRecognized()
    {
        var result = _command.Parse("--no-browser");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_NoHttpsOption_IsRecognized()
    {
        var result = _command.Parse("--no-https");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_ExtensionsOption_IsRecognized()
    {
        var result = _command.Parse("--extensions /path/to/extensions");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_VerboseOption_IsRecognized()
    {
        var result = _command.Parse("--verbose");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_AllOptions_AreRecognized()
    {
        var result = _command.Parse("notebook.verso --port 9090 --no-browser --no-https --extensions /path --verbose");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_InvalidOption_ProducesError()
    {
        var result = _command.Parse("notebook.verso --nonexistent");
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
