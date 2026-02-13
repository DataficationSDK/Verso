using Verso.Abstractions;
using Verso.Execution;
using Verso.Tests.Helpers;

namespace Verso.Tests.Scaffold;

[TestClass]
public sealed class ScaffoldExecutionTests
{
    private Verso.Scaffold _scaffold = null!;

    [TestInitialize]
    public void Setup()
    {
        _scaffold = new Verso.Scaffold();
        _scaffold.DefaultKernelId = "fake";
        _scaffold.RegisterKernel(new FakeLanguageKernel("fake"));
    }

    [TestMethod]
    public async Task ExecuteCell_ReturnsSuccess()
    {
        var cell = _scaffold.AddCell(source: "hello");
        var result = await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(cell.Id, result.CellId);
    }

    [TestMethod]
    public async Task ExecuteCell_CapturesOutputs()
    {
        var cell = _scaffold.AddCell(source: "test code");
        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(1, cell.Outputs.Count);
        Assert.AreEqual("Executed: test code", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task ExecuteCell_IncrementsExecutionCount()
    {
        var cell = _scaffold.AddCell(source: "x");

        var r1 = await _scaffold.ExecuteCellAsync(cell.Id);
        Assert.AreEqual(1, r1.ExecutionCount);

        var r2 = await _scaffold.ExecuteCellAsync(cell.Id);
        Assert.AreEqual(2, r2.ExecutionCount);
    }

    [TestMethod]
    public async Task ExecuteCell_InitializesKernel_Once()
    {
        var kernel = new FakeLanguageKernel("init-test");
        _scaffold.RegisterKernel(kernel);
        var cell = _scaffold.AddCell(language: "init-test", source: "x");

        await _scaffold.ExecuteCellAsync(cell.Id);
        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(1, kernel.InitializeCallCount);
    }

    [TestMethod]
    public async Task ExecuteCell_UsesCellLanguage_OverDefault()
    {
        var pythonKernel = new FakeLanguageKernel("python", executeFunc: (code, ctx) =>
            Task.FromResult<IReadOnlyList<CellOutput>>(new[] { new CellOutput("text/plain", "from python") }));
        _scaffold.RegisterKernel(pythonKernel);

        var cell = _scaffold.AddCell(language: "python", source: "x");
        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual("from python", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task ExecuteCell_UsesDefaultKernel_WhenCellLanguageNull()
    {
        var cell = _scaffold.AddCell(source: "x");
        var result = await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
    }

    [TestMethod]
    public async Task ExecuteCell_NonExistentCell_Throws()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _scaffold.ExecuteCellAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ExecuteCell_NoKernel_ReturnsFailed()
    {
        var scaffold = new Verso.Scaffold();
        var cell = scaffold.AddCell(language: "unknown", source: "x");
        var result = await scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Failed, result.Status);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task ExecuteCell_NoLanguageNoDefault_ReturnsFailed()
    {
        var scaffold = new Verso.Scaffold();
        var cell = scaffold.AddCell(source: "x");
        var result = await scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Failed, result.Status);
    }

    [TestMethod]
    public async Task ExecuteCell_KernelThrows_ReturnsFailed_WithErrorOutput()
    {
        var throwing = new FakeLanguageKernel("throws", executeFunc: (_, _) =>
            throw new InvalidOperationException("kernel error"));
        _scaffold.RegisterKernel(throwing);
        var cell = _scaffold.AddCell(language: "throws", source: "x");

        var result = await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Failed, result.Status);
        Assert.IsTrue(cell.Outputs.Any(o => o.IsError));
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("kernel error")));
    }

    [TestMethod]
    public async Task ExecuteCell_Cancellation_ReturnsCancelled()
    {
        var cts = new CancellationTokenSource();
        var kernel = new FakeLanguageKernel("slow", executeFunc: async (code, ctx) =>
        {
            await Task.Delay(5000, ctx.CancellationToken);
            return Array.Empty<CellOutput>();
        });
        _scaffold.RegisterKernel(kernel);
        var cell = _scaffold.AddCell(language: "slow", source: "x");

        cts.CancelAfter(50);
        var result = await _scaffold.ExecuteCellAsync(cell.Id, cts.Token);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Cancelled, result.Status);
    }

    [TestMethod]
    public async Task ExecuteCell_StreamingOutputs_AppearDuringExecution()
    {
        var kernel = new FakeLanguageKernel("streaming", executeFunc: async (code, ctx) =>
        {
            await ctx.WriteOutputAsync(new CellOutput("text/plain", "line1"));
            await ctx.WriteOutputAsync(new CellOutput("text/plain", "line2"));
            return Array.Empty<CellOutput>();
        });
        _scaffold.RegisterKernel(kernel);
        var cell = _scaffold.AddCell(language: "streaming", source: "x");

        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.AreEqual(2, cell.Outputs.Count);
        Assert.AreEqual("line1", cell.Outputs[0].Content);
        Assert.AreEqual("line2", cell.Outputs[1].Content);
    }

    [TestMethod]
    public async Task ExecuteCell_ClearsOutputs_BeforeExecution()
    {
        var cell = _scaffold.AddCell(source: "x");
        cell.Outputs.Add(new CellOutput("text/plain", "old output"));

        await _scaffold.ExecuteCellAsync(cell.Id);

        Assert.IsFalse(cell.Outputs.Any(o => o.Content == "old output"));
    }

    [TestMethod]
    public async Task ExecuteAll_ExecutesAllCells()
    {
        _scaffold.AddCell(source: "a");
        _scaffold.AddCell(source: "b");
        _scaffold.AddCell(source: "c");

        var results = await _scaffold.ExecuteAllAsync();

        Assert.AreEqual(3, results.Count);
        Assert.IsTrue(results.All(r => r.Status == ExecutionResult.ExecutionStatus.Success));
    }

    [TestMethod]
    public async Task ExecuteAll_Cancellation_StopsBetweenCells()
    {
        var callCount = 0;
        var cts = new CancellationTokenSource();
        var kernel = new FakeLanguageKernel("counting", executeFunc: (code, ctx) =>
        {
            callCount++;
            if (callCount >= 2) cts.Cancel();
            return Task.FromResult<IReadOnlyList<CellOutput>>(new[] { new CellOutput("text/plain", "ok") });
        });

        var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "counting";
        scaffold.RegisterKernel(kernel);
        scaffold.AddCell(source: "a");
        scaffold.AddCell(source: "b");
        scaffold.AddCell(source: "c");

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => scaffold.ExecuteAllAsync(cts.Token));
    }

    [TestMethod]
    public async Task ExecuteCell_VariablePersistence()
    {
        var kernel = new FakeLanguageKernel("vars", executeFunc: (code, ctx) =>
        {
            if (code == "set")
                ctx.Variables.Set("myVar", 42);
            else if (code == "get")
            {
                var val = ctx.Variables.Get<int>("myVar");
                return Task.FromResult<IReadOnlyList<CellOutput>>(new[] { new CellOutput("text/plain", val.ToString()) });
            }
            return Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());
        });

        var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "vars";
        scaffold.RegisterKernel(kernel);

        var setCell = scaffold.AddCell(source: "set");
        var getCell = scaffold.AddCell(source: "get");

        await scaffold.ExecuteCellAsync(setCell.Id);
        await scaffold.ExecuteCellAsync(getCell.Id);

        Assert.AreEqual("42", getCell.Outputs[0].Content);
    }

    [TestMethod]
    public void Scaffold_Properties_Accessible()
    {
        var notebook = new NotebookModel { Title = "Test Notebook", DefaultKernelId = "csharp" };
        var scaffold = new Verso.Scaffold(notebook);

        Assert.AreEqual("Test Notebook", scaffold.Title);
        Assert.AreEqual("csharp", scaffold.DefaultKernelId);
        Assert.AreSame(notebook, scaffold.Notebook);
        Assert.IsNotNull(scaffold.Variables);
        Assert.IsNotNull(scaffold.ThemeContext);
        Assert.IsNotNull(scaffold.ExtensionHostContext);
    }

    [TestMethod]
    public void Scaffold_Title_IsSettable()
    {
        _scaffold.Title = "My Notebook";
        Assert.AreEqual("My Notebook", _scaffold.Title);
        Assert.AreEqual("My Notebook", _scaffold.Notebook.Title);
    }

    [TestMethod]
    public void Scaffold_LayoutCapabilities_DefaultsAllSet()
    {
        var scaffold = new Verso.Scaffold();
        var allFlags = LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
                       LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit |
                       LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute |
                       LayoutCapabilities.MultiSelect;
        Assert.AreEqual(allFlags, scaffold.LayoutCapabilities);
    }

    [TestMethod]
    public void Scaffold_LayoutCapabilities_IsSettable()
    {
        _scaffold.LayoutCapabilities = LayoutCapabilities.CellEdit;
        Assert.AreEqual(LayoutCapabilities.CellEdit, _scaffold.LayoutCapabilities);
    }

    [TestMethod]
    public void Scaffold_NullNotebook_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new Verso.Scaffold(null!));
    }
}
