using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Meta;
using Verso.Cli.Repl.Meta.Commands;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class MetaCommandRegistryTests
{
    [TestMethod]
    public void TryResolve_KnownName_ReturnsCommand()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new HelpMeta());

        Assert.IsTrue(registry.TryResolve("help", out var command));
        Assert.AreEqual("help", command.Name);
    }

    [TestMethod]
    public void TryResolve_IsCaseInsensitive()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new HelpMeta());

        Assert.IsTrue(registry.TryResolve("HELP", out _));
        Assert.IsTrue(registry.TryResolve("Help", out _));
    }

    [TestMethod]
    public void TryResolve_Alias_ResolvesToPrimary()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new ExitMeta());

        Assert.IsTrue(registry.TryResolve("quit", out var command),
            "The 'quit' alias should resolve.");
        Assert.AreEqual("exit", command.Name,
            "Aliases resolve to the same command instance; the primary name is returned.");
    }

    [TestMethod]
    public void TryResolve_UnknownName_Fails()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new HelpMeta());

        Assert.IsFalse(registry.TryResolve("nonsense", out _));
    }

    [TestMethod]
    public void AllOrdered_PreservesRegistrationOrder()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new HelpMeta());
        registry.Register(new ExitMeta());
        registry.Register(new ClearMeta());

        var names = registry.AllOrdered.Select(c => c.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "help", "exit", "clear" }, names);
    }
}
