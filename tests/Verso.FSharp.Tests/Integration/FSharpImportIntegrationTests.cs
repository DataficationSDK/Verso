using Verso.Abstractions;
using Verso.FSharp.Import;
using Verso.FSharp.Kernel;
using Verso.FSharp.Tests.Helpers;

namespace Verso.FSharp.Tests.Integration;

[TestClass]
public sealed class FSharpImportIntegrationTests
{
    private FSharpKernel _kernel = null!;
    private StubExecutionContext _execCtx = null!;
    private JupyterFSharpPostProcessor _postProcessor = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
        _execCtx = new StubExecutionContext();
        _postProcessor = new JupyterFSharpPostProcessor();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task ImportFSharpCells_ExecuteConverted_VerifyOutput()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!fsharp\nlet result = 6 * 7"
                },
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!fsharp\nprintfn \"Answer: %d\" result"
                }
            }
        };

        // Import
        var imported = await _postProcessor.PostDeserializeAsync(notebook, "test.ipynb");

        // Verify conversion
        Assert.AreEqual("fsharp", imported.Cells[0].Language);
        Assert.AreEqual("fsharp", imported.Cells[1].Language);
        Assert.IsFalse(imported.Cells[0].Source.Contains("#!fsharp"));
        Assert.IsFalse(imported.Cells[1].Source.Contains("#!fsharp"));

        // Execute converted cells
        var outputs1 = await _kernel.ExecuteAsync(imported.Cells[0].Source, _execCtx);
        var outputs2 = await _kernel.ExecuteAsync(imported.Cells[1].Source, _execCtx);

        // Verify execution worked
        var allText = string.Join(" ", outputs2.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Answer: 42"), $"Expected 'Answer: 42' in output, got: {allText}");
    }

    [TestMethod]
    public async Task ImportSharePattern_ExecuteConverted_VerifyVariableAccess()
    {
        // Pre-populate a variable (simulating another kernel setting it)
        _execCtx.Variables.Set("sharedData", 100);

        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!share --from csharp sharedData"
                }
            }
        };

        // Import
        var imported = await _postProcessor.PostDeserializeAsync(notebook, "test.ipynb");

        // Verify conversion
        Assert.AreEqual("fsharp", imported.Cells[0].Language);
        Assert.IsTrue(imported.Cells[0].Source.Contains("Variables.Get<obj>(\"sharedData\")"),
            $"Expected Variables.Get call, got: {imported.Cells[0].Source}");

        // Execute the converted cell — it should bind the shared variable
        var outputs = await _kernel.ExecuteAsync(imported.Cells[0].Source, _execCtx);

        // Verify no errors
        Assert.IsFalse(outputs.Any(o => o.IsError),
            $"Unexpected error: {string.Join("; ", outputs.Where(o => o.IsError).Select(o => o.Content))}");
    }

    [TestMethod]
    public async Task ImportSetPattern_ExecuteConverted_VerifyVariableSet()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!set --name answer --value @fsharp:42"
                }
            }
        };

        // Import
        var imported = await _postProcessor.PostDeserializeAsync(notebook, "test.ipynb");

        // Verify conversion
        Assert.AreEqual("fsharp", imported.Cells[0].Language);
        Assert.IsTrue(imported.Cells[0].Source.Contains("Variables.Set(\"answer\", 42)"),
            $"Expected Variables.Set call, got: {imported.Cells[0].Source}");

        // Execute the converted cell
        var outputs = await _kernel.ExecuteAsync(imported.Cells[0].Source, _execCtx);

        // Verify no errors
        Assert.IsFalse(outputs.Any(o => o.IsError),
            $"Unexpected error: {string.Join("; ", outputs.Where(o => o.IsError).Select(o => o.Content))}");

        // Verify the variable was set
        var answer = _execCtx.Variables.Get<object>("answer");
        Assert.IsNotNull(answer, "Variable 'answer' should be set");
        Assert.AreEqual(42, answer);
    }
}
