using Verso.Abstractions;
using Verso.Execution;
using Verso.Kernels;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelScaffoldIntegrationTests
{
    private Verso.Scaffold _scaffold = null!;

    [TestInitialize]
    public void Setup()
    {
        _scaffold = new Verso.Scaffold();
        _scaffold.DefaultKernelId = "csharp";
        _scaffold.RegisterKernel(new CSharpKernel());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _scaffold.DisposeAsync();
    }

    [TestMethod]
    public async Task MultiCell_VariablePersistence()
    {
        var cell1 = _scaffold.AddCell(source: "var x = 10;");
        var cell2 = _scaffold.AddCell(source: "x * 2");

        var result1 = await _scaffold.ExecuteCellAsync(cell1.Id);
        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result1.Status);

        var result2 = await _scaffold.ExecuteCellAsync(cell2.Id);
        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result2.Status);

        Assert.IsTrue(cell2.Outputs.Any(o => o.Content == "20"),
            $"Expected output '20', got: [{string.Join(", ", cell2.Outputs.Select(o => o.Content))}]");
    }

    [TestMethod]
    public async Task MultiCell_MethodPersistence()
    {
        var cell1 = _scaffold.AddCell(source: "int Double(int n) => n * 2;");
        var cell2 = _scaffold.AddCell(source: "Double(7)");

        await _scaffold.ExecuteCellAsync(cell1.Id);
        await _scaffold.ExecuteCellAsync(cell2.Id);

        Assert.IsTrue(cell2.Outputs.Any(o => o.Content == "14"));
    }

    [TestMethod]
    public async Task ExecuteAll_SequentialCells()
    {
        _scaffold.AddCell(source: "var count = 0;");
        _scaffold.AddCell(source: "count += 5;");
        var cell3 = _scaffold.AddCell(source: "count");

        var results = await _scaffold.ExecuteAllAsync();

        Assert.IsTrue(results.All(r => r.Status == ExecutionResult.ExecutionStatus.Success));
        Assert.IsTrue(cell3.Outputs.Any(o => o.Content == "5"));
    }

    [TestMethod]
    public async Task CompilationError_FailsGracefully()
    {
        var cell = _scaffold.AddCell(source: "int x = \"not an int\";");
        var result = await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status,
            "Pipeline treats kernel-returned error outputs as success.");
        Assert.IsTrue(cell.Outputs.Any(o => o.IsError),
            "Expected error output from compilation failure.");
    }

    [TestMethod]
    public async Task ConsoleOutput_CapturedThroughScaffold()
    {
        var cell = _scaffold.AddCell(source: "Console.Write(\"scaffold-test\")");
        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("scaffold-test")),
            "Expected console output through scaffold.");
    }

    [TestMethod]
    public async Task CompletionsAfterExecution_IncludeUserVariable()
    {
        var cell1 = _scaffold.AddCell(source: "var items = new List<int>();");
        await _scaffold.ExecuteCellAsync(cell1.Id);

        var kernel = _scaffold.GetKernel("csharp")!;
        var completions = await kernel.GetCompletionsAsync("items.", 6);

        Assert.IsTrue(completions.Count > 0, "Expected completions after execution.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Add"),
            "Expected 'Add' in completions for executed variable.");
    }

    [TestMethod]
    public async Task VariableStore_PopulatedAfterExecution()
    {
        var cell = _scaffold.AddCell(source: "var answer = 42;");
        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.IsTrue(_scaffold.Variables.TryGet<int>("answer", out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public async Task MultiCell_ListAccumulation()
    {
        _scaffold.AddCell(source: "var items = new List<int>();");
        _scaffold.AddCell(source: "items.Add(1); items.Add(2); items.Add(3);");
        var cell3 = _scaffold.AddCell(source: "items.Count");

        var results = await _scaffold.ExecuteAllAsync();

        Assert.IsTrue(results.All(r => r.Status == ExecutionResult.ExecutionStatus.Success));
        Assert.IsTrue(cell3.Outputs.Any(o => o.Content == "3"));
    }
}
