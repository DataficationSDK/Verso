using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Commands;

namespace Verso.Cli.Tests.Commands;

[TestClass]
public class ReplCommandTests
{
    private Command _command = null!;

    [TestInitialize]
    public void Setup()
    {
        _command = ReplCommand.Create();
    }

    [TestMethod]
    public void Command_HasOptionalNotebookArgument()
    {
        var arg = _command.Arguments.FirstOrDefault(a => a.Name == "notebook");
        Assert.IsNotNull(arg, "Command should have a 'notebook' argument.");
        Assert.AreEqual(ArgumentArity.ZeroOrOne, arg.Arity, "The notebook argument should be optional.");
    }

    [TestMethod]
    public void Parse_NoArguments_Succeeds()
    {
        var result = _command.Parse("");
        Assert.AreEqual(0, result.Errors.Count,
            "A bare 'verso repl' invocation should parse cleanly — the notebook argument is optional.");
    }

    [TestMethod]
    public void Parse_WithNotebook_Succeeds()
    {
        var result = _command.Parse("test.verso");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_AllOptions_AreRecognized()
    {
        var result = _command.Parse(
            "test.verso --kernel csharp --theme \"Verso Dark\" --layout default " +
            "--execute --no-color --plain --history /tmp/history --list-kernels --list-themes");
        Assert.AreEqual(0, result.Errors.Count, string.Join(", ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_ShortFlagExecute_Succeeds()
    {
        var result = _command.Parse("test.verso -x");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_HistoryNone_Succeeds()
    {
        var result = _command.Parse("--history none");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Command_Name_IsRepl()
    {
        Assert.AreEqual("repl", _command.Name);
    }
}
