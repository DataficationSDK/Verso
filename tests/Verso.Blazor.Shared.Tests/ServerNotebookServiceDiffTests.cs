using System.Diagnostics;
using Verso.Abstractions;
using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ServerNotebookServiceDiffTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void CreateTempDir()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"verso-diff-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task GetDiffSourcesAsync_NoFilePath_OnlyFileSourceAvailable()
    {
        await using var service = NewService();
        await service.NewNotebookAsync();

        var sources = await service.GetDiffSourcesAsync();

        Assert.IsFalse(sources.Single(s => s.Id == "lastSaved").Available);
        Assert.IsFalse(sources.Single(s => s.Id == "gitHead").Available);
        Assert.IsFalse(sources.Single(s => s.Id == "gitRef").Available);
        Assert.IsTrue(sources.Single(s => s.Id == "file").Available);
    }

    [TestMethod]
    public async Task GetDiffSourcesAsync_SavedOutsideGitRepo_GitSourcesUnavailable()
    {
        var path = Path.Combine(_tempDir, "notebook.verso");
        await using var service = NewService();
        await service.NewNotebookAsync();
        await service.SaveAsync(path);

        var sources = await service.GetDiffSourcesAsync();

        Assert.IsTrue(sources.Single(s => s.Id == "lastSaved").Available);
        Assert.IsFalse(sources.Single(s => s.Id == "gitHead").Available);
    }

    [TestMethod]
    public async Task ComputeDiffAsync_LastSaved_ReportsUnsavedEditAsModified()
    {
        var path = Path.Combine(_tempDir, "notebook.verso");
        await using var service = NewService();
        await service.NewNotebookAsync();
        var cell = await service.AddCellAsync("code", "csharp");
        await service.UpdateCellSourceAsync(cell.Id, "var x = 1;");
        await service.SaveAsync(path);

        await service.UpdateCellSourceAsync(cell.Id, "var x = 2;");
        var result = await service.ComputeDiffAsync("lastSaved");

        Assert.IsNotNull(result);
        Assert.AreEqual("Last Saved", result.BaselineLabel);
        Assert.AreEqual(1, result.Summary.Modified);
        var modified = result.Cells.Single(e => e.Kind == CellDiffKind.Modified);
        Assert.AreEqual("var x = 1;", modified.BaselineCell!.Source);
        Assert.AreEqual("var x = 2;", modified.CurrentCell!.Source);
        Assert.IsFalse(modified.MatchedByContent, "Same cell id on both sides should match by id.");
    }

    [TestMethod]
    public async Task ComputeDiffAsync_GitHead_ComparesAgainstCommittedVersion()
    {
        RunGit("init");
        var path = Path.Combine(_tempDir, "notebook.verso");
        await using var service = NewService();
        await service.NewNotebookAsync();
        var cell = await service.AddCellAsync("code", "csharp");
        await service.UpdateCellSourceAsync(cell.Id, "var committed = true;");
        await service.SaveAsync(path);
        RunGit("add", "-A");
        RunGit("-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "-m", "baseline");

        await service.UpdateCellSourceAsync(cell.Id, "var committed = false;");
        var result = await service.ComputeDiffAsync("gitHead");

        Assert.IsNotNull(result);
        Assert.AreEqual("Git: HEAD", result.BaselineLabel);
        Assert.AreEqual(1, result.Summary.Modified);
        Assert.AreEqual("var committed = true;",
            result.Cells.Single(e => e.Kind == CellDiffKind.Modified).BaselineCell!.Source);
    }

    [TestMethod]
    public async Task ComputeDiffAsync_GitHead_UncommittedFile_ThrowsFriendlyError()
    {
        RunGit("init");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "README.md"), "seed");
        RunGit("add", "-A");
        RunGit("-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "-m", "seed");
        var path = Path.Combine(_tempDir, "notebook.verso");
        await using var service = NewService();
        await service.NewNotebookAsync();
        await service.SaveAsync(path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.ComputeDiffAsync("gitHead"));
        StringAssert.Contains(ex.Message, "not tracked");
    }

    [TestMethod]
    public async Task ComputeDiffAsync_File_UsesContentAlignmentAcrossUnrelatedFiles()
    {
        var baselinePath = Path.Combine(_tempDir, "baseline.verso");
        await using (var baselineService = NewService())
        {
            await baselineService.NewNotebookAsync();
            var baselineCell = await baselineService.AddCellAsync("code", "csharp");
            await baselineService.UpdateCellSourceAsync(baselineCell.Id, "Console.WriteLine(42);");
            await baselineService.SaveAsync(baselinePath);
        }

        await using var service = NewService();
        await service.NewNotebookAsync();
        var cell = await service.AddCellAsync("code", "csharp");
        await service.UpdateCellSourceAsync(cell.Id, "Console.WriteLine(42);");

        var result = await service.ComputeDiffAsync("file", baselinePath);

        Assert.IsNotNull(result);
        var pair = result.Cells.Single(e => e.CurrentCell?.Source == "Console.WriteLine(42);");
        Assert.IsNotNull(pair.BaselineCell);
        Assert.AreEqual(CellDiffKind.Unchanged, pair.Kind);
        Assert.IsTrue(pair.MatchedByContent, "Different notebooks have unrelated ids; expected a content match.");
    }

    [TestMethod]
    public async Task ComputeDiffAsync_UnknownSourceId_Throws()
    {
        await using var service = NewService();
        await service.NewNotebookAsync();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ComputeDiffAsync("nonsense"));
    }

    [TestMethod]
    public async Task ComputeDiffAsync_MalformedBaselineFile_ThrowsFriendlyError()
    {
        var badPath = Path.Combine(_tempDir, "broken.verso");
        await File.WriteAllTextAsync(badPath, "{ this is not json");
        await using var service = NewService();
        await service.NewNotebookAsync();

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.ComputeDiffAsync("file", badPath));
        StringAssert.Contains(ex.Message, "Could not parse");
    }

    private static ServerNotebookService NewService()
        => new(new ThrowingJSRuntime(), new LayoutAssetCache());

    private sealed class ThrowingJSRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}");
    }

    private void RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit(15_000);
        Assert.AreEqual(0, process.ExitCode,
            $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }
}
