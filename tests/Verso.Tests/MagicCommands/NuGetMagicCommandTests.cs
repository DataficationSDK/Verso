using Verso.MagicCommands;
using Verso.Tests.Helpers;

namespace Verso.Tests.MagicCommands;

[TestClass]
public sealed class NuGetMagicCommandTests
{
    [TestMethod]
    public void Metadata_IsCorrect()
    {
        var command = new NuGetMagicCommand();

        Assert.AreEqual("nuget", command.Name);
        Assert.AreEqual("verso.magic.nuget", command.ExtensionId);
        Assert.AreEqual("0.1.0", command.Version);
    }

    [TestMethod]
    public async Task EmptyArguments_SuppressesAndWritesUsageError()
    {
        var command = new NuGetMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("Usage"));
    }

    [TestMethod]
    public async Task WhitespaceArguments_SuppressesAndWritesUsageError()
    {
        var command = new NuGetMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("   ", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
    }

    [TestMethod]
    public void DoesNotSuppressExecution_ByDefault()
    {
        var command = new NuGetMagicCommand();

        Assert.AreEqual(2, command.Parameters.Count);
        Assert.AreEqual("packageId", command.Parameters[0].Name);
        Assert.IsTrue(command.Parameters[0].IsRequired);
        Assert.AreEqual("version", command.Parameters[1].Name);
        Assert.IsFalse(command.Parameters[1].IsRequired);
    }
}
