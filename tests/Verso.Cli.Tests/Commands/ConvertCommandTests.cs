using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Commands;

namespace Verso.Cli.Tests.Commands;

[TestClass]
public class ConvertCommandTests
{
    private Command _command = null!;

    [TestInitialize]
    public void Setup()
    {
        _command = ConvertCommand.Create();
    }

    [TestMethod]
    public void Command_HasCorrectName()
    {
        Assert.AreEqual("convert", _command.Name);
    }

    [TestMethod]
    public void Command_HasDescription()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(_command.Description));
    }

    [TestMethod]
    public void Parse_ValidInputAndTo_Succeeds()
    {
        var result = _command.Parse("notebook.verso --to ipynb");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_MissingInput_ProducesError()
    {
        var result = _command.Parse("--to ipynb");
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public void Parse_MissingTo_ProducesError()
    {
        var result = _command.Parse("notebook.verso");
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    [DataRow("verso")]
    [DataRow("ipynb")]
    [DataRow("dib")]
    public void Parse_ValidToFormats_Succeed(string format)
    {
        var result = _command.Parse($"notebook.verso --to {format}");
        Assert.AreEqual(0, result.Errors.Count, $"Format '{format}' should be valid. Errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [TestMethod]
    public void Parse_InvalidToFormat_ProducesError()
    {
        var result = _command.Parse("notebook.verso --to pdf");
        Assert.IsTrue(result.Errors.Count > 0, "Unsupported format 'pdf' should produce a validation error.");
    }

    [TestMethod]
    public void Parse_OutputOption_IsRecognized()
    {
        var result = _command.Parse("notebook.verso --to ipynb --output converted.ipynb");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_StripOutputsOption_IsRecognized()
    {
        var result = _command.Parse("notebook.verso --to verso --strip-outputs");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_ExtensionsOption_IsRecognized()
    {
        var result = _command.Parse("notebook.verso --to ipynb --extensions /path/to/extensions");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_AllOptions_AreRecognized()
    {
        var result = _command.Parse("notebook.verso --to ipynb --output out.ipynb --strip-outputs --extensions /path");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_InvalidOption_ProducesError()
    {
        var result = _command.Parse("notebook.verso --to ipynb --nonexistent");
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
