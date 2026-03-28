using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Tests.Integration;

[TestClass]
public class ExitCodeIntegrationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"verso_exit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Success_ExitCodeZero()
    {
        var filePath = await CreateNotebookAsync("success.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "var x = 1;"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.Success, result.ExitCode);
    }

    [TestMethod]
    public async Task CellFailure_ExitCodeOne()
    {
        var filePath = await CreateNotebookAsync("failure.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "throw new System.Exception(\"boom\");"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.CellFailure, result.ExitCode);
    }

    [TestMethod]
    public async Task FileNotFound_ExitCodeThree()
    {
        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = Path.Combine(_tempDir, "missing.verso")
        });

        Assert.AreEqual(ExitCodes.FileNotFound, result.ExitCode);
    }

    [TestMethod]
    public async Task InvalidFormat_ExitCodeFour()
    {
        var filePath = Path.Combine(_tempDir, "bad.xyz");
        await File.WriteAllTextAsync(filePath, "not a notebook");

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions { FilePath = filePath });

        Assert.AreEqual(ExitCodes.SerializationError, result.ExitCode);
    }

    [TestMethod]
    public async Task Timeout_ExitCodeTwo()
    {
        var filePath = await CreateNotebookAsync("timeout.verso", new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "System.Threading.Thread.Sleep(5000);"
        });

        var runner = new HeadlessRunner();
        var result = await runner.ExecuteAsync(new RunOptions
        {
            FilePath = filePath,
            TimeoutSeconds = 1
        });

        Assert.AreEqual(ExitCodes.Timeout, result.ExitCode);
    }

    private async Task<string> CreateNotebookAsync(string fileName, params CellModel[] cells)
    {
        var notebook = new NotebookModel { DefaultKernelId = "csharp" };
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
