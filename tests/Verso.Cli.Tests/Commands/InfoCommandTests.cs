using System.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Commands;

namespace Verso.Cli.Tests.Commands;

[TestClass]
public class InfoCommandTests
{
    [TestMethod]
    public void Create_ReturnsCommandWithCorrectName()
    {
        var command = InfoCommand.Create();
        Assert.AreEqual("info", command.Name);
    }

    [TestMethod]
    public void Create_ReturnsCommandWithDescription()
    {
        var command = InfoCommand.Create();
        Assert.IsFalse(string.IsNullOrEmpty(command.Description));
    }

    [TestMethod]
    public void Parse_NoArgs_Succeeds()
    {
        var command = InfoCommand.Create();
        var result = command.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }
}
