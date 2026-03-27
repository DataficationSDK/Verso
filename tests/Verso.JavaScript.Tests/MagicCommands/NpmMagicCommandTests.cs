using Verso.Abstractions;
using Verso.JavaScript.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.JavaScript.Tests.MagicCommands;

[TestClass]
public class NpmMagicCommandTests
{
    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var cmd = new NpmMagicCommand();
        Assert.AreEqual("verso.magic.npm", cmd.ExtensionId);
        Assert.AreEqual("npm", cmd.Name);
        Assert.AreEqual("1.0.0", cmd.Version);
        Assert.IsNotNull(cmd.Description);
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(NpmMagicCommand), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "NpmMagicCommand should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsIMagicCommand()
    {
        Assert.IsTrue(typeof(IMagicCommand).IsAssignableFrom(typeof(NpmMagicCommand)));
    }

    [TestMethod]
    public void Parameters_HasPackagesParameter()
    {
        var cmd = new NpmMagicCommand();
        Assert.AreEqual(1, cmd.Parameters.Count);
        Assert.AreEqual("packages", cmd.Parameters[0].Name);
        Assert.IsTrue(cmd.Parameters[0].IsRequired);
    }

    [TestMethod]
    public async Task EmptyArguments_WritesErrorAndSuppresses()
    {
        var cmd = new NpmMagicCommand();
        var context = new StubMagicCommandContext();

        await cmd.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution, "Should suppress execution for empty arguments");
        Assert.IsTrue(context.WrittenOutputs.Any(o => o.IsError),
            "Should write an error output for empty arguments");
    }

    [TestMethod]
    public async Task WhitespaceArguments_WritesErrorAndSuppresses()
    {
        var cmd = new NpmMagicCommand();
        var context = new StubMagicCommandContext();

        await cmd.ExecuteAsync("   ", context);

        Assert.IsTrue(context.SuppressExecution, "Should suppress execution for whitespace arguments");
    }

    [TestMethod]
    public async Task NoJsKernel_WritesErrorAndSuppresses()
    {
        var cmd = new NpmMagicCommand();
        var context = new StubMagicCommandContext();

        await cmd.ExecuteAsync("lodash", context);

        Assert.IsTrue(context.SuppressExecution, "Should suppress execution when no JS kernel is loaded");
        Assert.IsTrue(context.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("JavaScript kernel")),
            "Should mention JavaScript kernel in error");
    }
}
