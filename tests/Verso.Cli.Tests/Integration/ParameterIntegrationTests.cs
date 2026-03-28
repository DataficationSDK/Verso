using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Utilities;
using Verso.Extensions;

namespace Verso.Cli.Tests.Integration;

[TestClass]
public class ParameterIntegrationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"verso_param_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Execute_WithStringParam_AccessibleInCell()
    {
        var filePath = await CreateParameterizedNotebookAsync("string_param.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Required = true }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "var r = Variables.Get<string>(\"region\"); Console.Write(r);"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            Parameters = new Dictionary<string, string> { ["region"] = "us-east" }
        });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.IsNotNull(result.ResolvedParameters);
        Assert.AreEqual("us-east", result.ResolvedParameters["region"]);

        var cell = result.Cells[0];
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("us-east")),
            "Cell output should contain the injected parameter value.");
    }

    [TestMethod]
    public async Task Execute_WithIntParam_ParsedAsLong()
    {
        var filePath = await CreateParameterizedNotebookAsync("int_param.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["batchSize"] = new() { Type = "int" }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "var b = Variables.Get<long>(\"batchSize\"); Console.Write(b.GetType().Name + \":\" + b);"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            Parameters = new Dictionary<string, string> { ["batchSize"] = "5000" }
        });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        var cell = result.Cells[0];
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("Int64:5000")),
            "Parameter should be injected as a long with value 5000.");
    }

    [TestMethod]
    public async Task Execute_WithDefaults_RunsWithoutCliParams()
    {
        var filePath = await CreateParameterizedNotebookAsync("defaults.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-west-2" },
                ["dryRun"] = new() { Type = "bool", Default = true }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "var r = Variables.Get<string>(\"region\"); var d = Variables.Get<bool>(\"dryRun\"); Console.Write($\"{r}:{d}\");"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        var cell = result.Cells[0];
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("us-west-2:True")),
            "Default values should be applied when no --param flags are given.");
    }

    [TestMethod]
    public async Task Execute_MissingRequired_ExitsWithCode5()
    {
        var filePath = await CreateParameterizedNotebookAsync("missing_required.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["date"] = new() { Type = "date", Required = true, Description = "Processing date" }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "Console.Write(\"should not run\");"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.MissingParameters, result.ExitCode);
        Assert.AreEqual(0, result.CellResults.Count, "No cells should execute when parameters are missing.");
    }

    [TestMethod]
    public async Task Execute_InvalidParamValue_ExitsWithCode5()
    {
        var filePath = await CreateParameterizedNotebookAsync("invalid_param.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["batchSize"] = new() { Type = "int" }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "Console.Write(\"should not run\");"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            Parameters = new Dictionary<string, string> { ["batchSize"] = "abc" }
        });

        Assert.AreEqual(ExitCodes.MissingParameters, result.ExitCode);
    }

    [TestMethod]
    public async Task Execute_ResolvedParameters_IncludedInResult()
    {
        var filePath = await CreateParameterizedNotebookAsync("result_params.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-west-2" },
                ["batchSize"] = new() { Type = "int", Default = 1000L }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "Console.Write(\"ok\");"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            Parameters = new Dictionary<string, string> { ["region"] = "eu-west-1" }
        });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.IsNotNull(result.ResolvedParameters);
        Assert.AreEqual("eu-west-1", result.ResolvedParameters["region"]);
        Assert.AreEqual(1000L, result.ResolvedParameters["batchSize"]);
    }

    [TestMethod]
    public async Task Execute_NoParameters_NoDefinitions_Succeeds()
    {
        var filePath = await CreateParameterizedNotebookAsync("no_params.verso",
            parameters: null,
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "Console.Write(\"hello\");"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.IsNull(result.ResolvedParameters);
    }

    [TestMethod]
    public async Task Execute_CrossKernel_ParametersAccessibleFromFSharp()
    {
        var filePath = await CreateParameterizedNotebookAsync("crosskernel.verso",
            parameters: new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Required = true }
            },
            new CellModel
            {
                Type = "code",
                Language = "csharp",
                Source = "var r = Variables.Get<string>(\"region\"); Console.Write($\"cs:{r}\");"
            },
            new CellModel
            {
                Type = "code",
                Language = "fsharp",
                Source = "let r = Variables.Get<string>(\"region\") in printf \"fs:%s\" r"
            });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            Parameters = new Dictionary<string, string> { ["region"] = "us-east" }
        });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
        Assert.AreEqual(2, result.CellResults.Count);

        Assert.IsTrue(result.Cells[0].Outputs.Any(o => o.Content.Contains("cs:us-east")),
            "C# cell should access the injected parameter.");
        Assert.IsTrue(result.Cells[1].Outputs.Any(o => o.Content.Contains("fs:us-east")),
            "F# cell should access the injected parameter.");
    }

    private async Task<string> CreateParameterizedNotebookAsync(
        string fileName,
        Dictionary<string, NotebookParameterDefinition>? parameters,
        params CellModel[] cells)
    {
        var notebook = new NotebookModel
        {
            DefaultKernelId = "csharp",
            Parameters = parameters
        };
        notebook.Cells.AddRange(cells);

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
