namespace MyExtension.Tests;

[TestClass]
public sealed class SampleMagicCommandTests
{
    private readonly SampleMagicCommand _command = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_command.ExtensionId));
    }

    [TestMethod]
    public void Description_IsNotNull()
    {
        Assert.IsNotNull(_command.Description);
        Assert.IsFalse(string.IsNullOrEmpty(_command.Description));
    }

    [TestMethod]
    public void Parameters_ContainsNameParameter()
    {
        Assert.AreEqual(1, _command.Parameters.Count);
        Assert.AreEqual("name", _command.Parameters[0].Name);
    }

    [TestMethod]
    public async Task Execute_WithArguments_GreetsByName()
    {
        var context = new StubMagicCommandContext();
        await _command.ExecuteAsync("Alice", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("Alice"));
    }

    [TestMethod]
    public async Task Execute_WithoutArguments_GreetsWorld()
    {
        var context = new StubMagicCommandContext();
        await _command.ExecuteAsync("", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("World"));
    }
}
