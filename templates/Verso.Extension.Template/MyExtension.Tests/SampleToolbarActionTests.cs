namespace MyExtension.Tests;

[TestClass]
public sealed class SampleToolbarActionTests
{
    private readonly SampleToolbarAction _action = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_action.ExtensionId));
    }

    [TestMethod]
    public async Task IsEnabled_ReturnsTrue()
    {
        var context = new StubToolbarActionContext();
        Assert.IsTrue(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task Execute_WritesOutput()
    {
        var context = new StubToolbarActionContext();
        await _action.ExecuteAsync(context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("Hello"));
    }
}
