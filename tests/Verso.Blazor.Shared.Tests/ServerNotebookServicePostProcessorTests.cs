using Verso.Abstractions;
using Verso.Blazor.Services;
using Verso.Extensions;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The browser editor runs notebook post-processors on open and on save, as the VS Code host
/// does. Opening uses the built-in Polyglot post-processor, which splits a cell on a leading
/// language directive; saving uses a recording processor registered on the live host.
/// </summary>
[TestClass]
public sealed class ServerNotebookServicePostProcessorTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "verso-postproc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public async Task OpenAsync_RunsPostProcessors()
    {
        var path = Path.Combine(_dir, "polyglot.ipynb");
        await File.WriteAllTextAsync(path, PolyglotNotebook);

        await using var service = new ServerNotebookService(new ThrowingJSRuntime(), new LayoutAssetCache());
        await service.OpenAsync(path);

        AssertPolyglotCellWasSplit(service);
    }

    [TestMethod]
    public async Task OpenFromContentAsync_RunsPostProcessors()
    {
        await using var service = new ServerNotebookService(new ThrowingJSRuntime(), new LayoutAssetCache());
        await service.OpenFromContentAsync("not-on-disk-" + Guid.NewGuid().ToString("N") + ".ipynb", PolyglotNotebook);

        AssertPolyglotCellWasSplit(service);
    }

    [TestMethod]
    public async Task SaveAsync_RunsPostProcessorsAndWritesWhatTheyReturn()
    {
        await using var service = new ServerNotebookService(new ThrowingJSRuntime(), new LayoutAssetCache());
        await service.NewNotebookAsync();

        var recorder = new RecordingPostProcessor();
        var host = (ExtensionHost)service.Scaffold!.ExtensionHostContext;
        await host.LoadExtensionAsync(recorder);

        var path = Path.Combine(_dir, "saved.verso");
        await service.SaveAsync(path);

        Assert.AreEqual(1, recorder.PreSerializeCalls, "PreSerializeAsync should run once per save.");
        Assert.AreEqual("verso-native", recorder.LastFormatId,
            "The native format is reported to post-processors as the VS Code host reports it.");
        StringAssert.Contains(await File.ReadAllTextAsync(path), RecordingPostProcessor.Marker,
            "The notebook the post-processor returned is the one written.");
    }

    private static void AssertPolyglotCellWasSplit(ServerNotebookService service)
    {
        var cells = service.Scaffold!.Notebook.Cells;
        Assert.IsTrue(
            cells.Any(c => c.Language == "powershell" && c.Source.Trim() == "Write-Output 1"),
            "Expected the Polyglot post-processor to turn the #!pwsh cell into a PowerShell cell. Cells: "
                + string.Join(" | ", cells.Select(c => $"{c.Language}: {c.Source}")));
    }

    private const string PolyglotNotebook =
        "{\"cells\":[{\"cell_type\":\"code\",\"execution_count\":null,\"metadata\":{},\"outputs\":[],"
        + "\"source\":[\"#!pwsh\\n\",\"Write-Output 1\"]}],"
        + "\"metadata\":{\"kernelspec\":{\"display_name\":\".NET (C#)\",\"language\":\"C#\",\"name\":\".net-csharp\"},"
        + "\"language_info\":{\"name\":\"C#\"}},\"nbformat\":4,\"nbformat_minor\":5}";

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

    private sealed class RecordingPostProcessor : INotebookPostProcessor
    {
        public const string Marker = "written-by-post-processor";

        public int PreSerializeCalls { get; private set; }
        public string? LastFormatId { get; private set; }

        public string ExtensionId => "verso.tests.recording-post-processor";
        public string Name => "Recording post-processor";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public int Priority => 0;

        public bool CanProcess(string? filePath, string formatId)
        {
            LastFormatId = formatId;
            return true;
        }

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath)
            => Task.FromResult(notebook);

        public Task<NotebookModel> PreSerializeAsync(NotebookModel notebook, string? filePath)
        {
            PreSerializeCalls++;
            notebook.Title = Marker;
            return Task.FromResult(notebook);
        }
    }
}
