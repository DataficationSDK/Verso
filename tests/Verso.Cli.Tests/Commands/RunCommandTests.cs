using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Commands;

namespace Verso.Cli.Tests.Commands;

[TestClass]
public class RunCommandTests
{
    private Command _command = null!;

    [TestInitialize]
    public void Setup()
    {
        _command = RunCommand.Create();
    }

    [TestMethod]
    public void Command_HasNotebookArgument()
    {
        var arg = _command.Arguments.FirstOrDefault(a => a.Name == "notebook");
        Assert.IsNotNull(arg, "Command should have a 'notebook' argument.");
    }

    [TestMethod]
    public void Parse_ValidNotebook_Succeeds()
    {
        var result = _command.Parse("test.verso");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MissingNotebook_ProducesError()
    {
        var result = _command.Parse("");
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public void Parse_AllOptions_AreRecognized()
    {
        var result = _command.Parse("test.verso --cell 0 --kernel csharp --output json --fail-fast --save --timeout 60 --verbose");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_MultipleCells_AreAccepted()
    {
        var result = _command.Parse("test.verso --cell 0 --cell 2");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_OutputFormats_AreValid()
    {
        foreach (var format in new[] { "Text", "Json", "None" })
        {
            var result = _command.Parse($"test.verso --output {format}");
            Assert.AreEqual(0, result.Errors.Count, $"Format '{format}' should be valid.");
        }
    }

    [TestMethod]
    public void Parse_InvalidOption_ProducesError()
    {
        var result = _command.Parse("test.verso --nonexistent");
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public void Parse_OutputFile_IsRecognized()
    {
        var result = _command.Parse("test.verso --output-file results.json");
        Assert.AreEqual(0, result.Errors.Count);
    }

    /// <summary>
    /// --output-file documents that it implies JSON, and the handler decides that by asking
    /// whether --output was supplied. An option carrying a default value still produces a result,
    /// so only an implicit one means the user left the format alone. Reading presence instead of
    /// implicitness left the format at Text, and the file was then never written at all.
    /// </summary>
    [TestMethod]
    public void Parse_OutputFileWithoutFormat_LeavesOutputImplicit()
    {
        var result = _command.Parse("test.verso --output-file results.json");

        var output = result.CommandResult.Children
            .OfType<OptionResult>()
            .FirstOrDefault(o => o.Option.Name == "output");

        Assert.IsTrue(output is null or { IsImplicit: true },
            "an unsupplied --output must not read as a format the user chose");
    }

    [TestMethod]
    public void Parse_OutputFileWithExplicitFormat_IsNotImplicit()
    {
        var result = _command.Parse("test.verso --output-file results.json --output Text");

        var output = result.CommandResult.Children
            .OfType<OptionResult>()
            .FirstOrDefault(o => o.Option.Name == "output");

        Assert.IsNotNull(output, "an explicit --output should have a result");
        Assert.IsFalse(output!.IsImplicit, "an explicit --output is the user's choice");
    }

    [TestMethod]
    public void Parse_Extensions_IsRecognized()
    {
        var result = _command.Parse("test.verso --extensions /path/to/extensions");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_SingleParam_IsRecognized()
    {
        var result = _command.Parse("test.verso --param region=us-east");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_MultipleParams_AreAccepted()
    {
        var result = _command.Parse("test.verso --param region=us-east --param batchSize=1000");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_Interactive_IsRecognized()
    {
        var result = _command.Parse("test.verso --interactive");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_IncludeMarkdown_IsRecognized()
    {
        var result = _command.Parse("test.verso --include-markdown");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_ShowParameters_IsRecognized()
    {
        var result = _command.Parse("test.verso --show-parameters");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_AllOptionsIncludingParams_AreRecognized()
    {
        var result = _command.Parse("test.verso --cell 0 --kernel csharp --output json --fail-fast --save --timeout 60 --verbose --param region=us-east --interactive --include-markdown --show-parameters");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }
}
