using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Tests.Integration;

[TestClass]
public class HeadlessRunnerTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"verso_cli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Execute_SimpleCSharpNotebook_Succeeds()
    {
        var filePath = await CreateNotebookAsync("simple.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "Console.Write(\"hello\");"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(1, result.CellResults.Count);
        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.CellResults[0].Status);
    }

    [TestMethod]
    public async Task Execute_MultiCellNotebook_AllCellsRun()
    {
        var filePath = await CreateNotebookAsync("multi.verso",
            new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" },
            new CellModel { Type = "code", Language = "csharp", Source = "var y = 2;" },
            new CellModel { Type = "code", Language = "csharp", Source = "var z = x + y;" });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(3, result.CellResults.Count);
        Assert.IsTrue(result.CellResults.All(r => r.Status == ExecutionResult.ExecutionStatus.Success));
    }

    [TestMethod]
    public async Task Execute_FailingCell_ReturnsCellFailure()
    {
        var filePath = await CreateNotebookAsync("failing.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "throw new System.Exception(\"test failure\");"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.CellFailure, result.ExitCode);
        Assert.AreEqual(1, result.CellResults.Count);
        // The Verso engine kernel catches exceptions and reports them as error outputs
        // while returning ExecutionResult.Success. The CLI detects failure via cell outputs.
        Assert.IsTrue(result.Cells[0].Outputs.Any(o => o.IsError),
            "Cell should have error outputs.");
    }

    [TestMethod]
    public async Task Execute_FailFast_StopsOnFirstFailure()
    {
        var filePath = await CreateNotebookAsync("failfast.verso",
            new CellModel { Type = "code", Language = "csharp", Source = "throw new System.Exception(\"fail\");" },
            new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath, FailFast = true });

        Assert.AreEqual(ExitCodes.CellFailure, result.ExitCode);
        Assert.AreEqual(1, result.CellResults.Count, "Should stop after first failure.");
    }

    [TestMethod]
    public async Task Execute_Timeout_ReturnsTimeoutCode()
    {
        var filePath = await CreateNotebookAsync("slow.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "System.Threading.Thread.Sleep(5000);"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath, TimeoutSeconds = 1 });

        Assert.AreEqual(ExitCodes.Timeout, result.ExitCode);
    }

    [TestMethod]
    public async Task Execute_CellSelector_RunsOnlySpecifiedCells()
    {
        var filePath = await CreateNotebookAsync("selective.verso",
            new CellModel { Type = "code", Language = "csharp", Source = "var a = 1;" },
            new CellModel { Type = "code", Language = "csharp", Source = "var b = 2;" },
            new CellModel { Type = "code", Language = "csharp", Source = "var c = 3;" });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            CellSelectors = new[] { "0", "2" }
        });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(2, result.CellResults.Count);
    }

    [TestMethod]
    public async Task Execute_Save_WritesBackToFile()
    {
        var filePath = await CreateNotebookAsync("saveme.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "Console.Write(42);"
        });

        var originalContent = await File.ReadAllTextAsync(filePath);

        var runner = new HeadlessRunner();
        await runner.ExecuteAsync(new RunOptions { FilePath = filePath, Save = true });

        var updatedContent = await File.ReadAllTextAsync(filePath);
        Assert.AreNotEqual(originalContent, updatedContent, "File should be updated with execution outputs.");
    }

    [TestMethod]
    public async Task Execute_FileNotFound_ReturnsFileNotFoundCode()
    {
        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = Path.Combine(_tempDir, "nonexistent.verso")
        });

        Assert.AreEqual(ExitCodes.FileNotFound, result.ExitCode);
    }

    [TestMethod]
    public async Task Execute_UnsupportedFormat_ReturnsSerializationError()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "not a notebook");

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.SerializationError, result.ExitCode);
    }

    [TestMethod]
    public async Task Execute_JsonOutput_ProducesValidJson()
    {
        var filePath = await CreateNotebookAsync("json_output.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "Console.Write(\"test\");"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        var doc = JsonOutputWriter.Build(
            result.NotebookPath,
            result.Cells,
            result.CellResults,
            result.TotalElapsed);

        var json = JsonOutputWriter.Serialize(doc);
        var parsed = System.Text.Json.JsonDocument.Parse(json);

        Assert.IsNotNull(parsed.RootElement.GetProperty("notebook"));
        Assert.IsNotNull(parsed.RootElement.GetProperty("cells"));
        Assert.IsNotNull(parsed.RootElement.GetProperty("summary"));
    }

    [TestMethod]
    public async Task Execute_Verbose_IncludesVariables()
    {
        var filePath = await CreateNotebookAsync("verbose.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "var testVar = 42;"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath, Verbose = true });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.IsNotNull(result.Variables, "Verbose mode should include variables.");
    }

    [TestMethod]
    public async Task Execute_IpynbFile_Succeeds()
    {
        // JupyterSerializer is import-only, so construct nbformat v4 JSON directly
        var ipynbContent =
            "{\n" +
            "  \"nbformat\": 4,\n" +
            "  \"nbformat_minor\": 2,\n" +
            "  \"metadata\": { \"kernelspec\": { \"language\": \"csharp\" } },\n" +
            "  \"cells\": [\n" +
            "    {\n" +
            "      \"cell_type\": \"code\",\n" +
            "      \"source\": [\"Console.Write(\\\"ipynb-hello\\\");\"],\n" +
            "      \"metadata\": {},\n" +
            "      \"outputs\": []\n" +
            "    }\n" +
            "  ]\n" +
            "}";

        var filePath = Path.Combine(_tempDir, "test.ipynb");
        await File.WriteAllTextAsync(filePath, ipynbContent);

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(1, result.CellResults.Count);
        Assert.IsTrue(result.Cells[0].Outputs.Any(o => o.Content.Contains("ipynb-hello")),
            "Cell output should contain the expected value from .ipynb execution.");
    }

    [TestMethod]
    public async Task Execute_MultiKernel_CSharpAndFSharp_BothSucceed()
    {
        var filePath = await CreateNotebookAsync("multikernel.verso",
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "Variables.Set(\"fromCsharp\", 42);"
            },
            new CellModel
            {
                Type = "code",
                Language = "fsharp",
                Source = "let v = Variables.Get<int>(\"fromCsharp\") in printf \"%d\" v"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(2, result.CellResults.Count);
        Assert.IsTrue(result.CellResults.All(r => r.Status == ExecutionResult.ExecutionStatus.Success),
            "Both C# and F# cells should execute successfully.");
    }

    private async Task<string> CreateNotebookAsync(string fileName, params CellModel[] cells)
    {
        var notebook = new NotebookModel
        {
            DefaultKernelId = "csharp"
        };
        notebook.Cells.AddRange(cells);

        // Use the serializer to create a proper .verso file
        var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        var serializer = host.GetSerializers().First(s => s.CanImport("test.verso"));
        var content = await serializer.SerializeAsync(notebook);
        await host.DisposeAsync();

        var filePath = Path.Combine(_tempDir, fileName);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }
}
