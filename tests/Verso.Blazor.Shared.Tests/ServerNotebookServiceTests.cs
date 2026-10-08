using Microsoft.JSInterop;
using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ServerNotebookServiceTests
{
    [TestMethod]
    public async Task ExecuteCell_ForwardsLiveOutputUpdates()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(
            service,
            "Write-Host 'before'\nStart-Sleep -Seconds 2\nWrite-Host 'after'");
        var outputUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.OnOutputUpdated += () => outputUpdated.TrySetResult();

        var execution = service.ExecuteCellAsync(cell.Id);

        await WaitForAsync(outputUpdated.Task, "Expected live output update while the cell was running.");
        Assert.IsFalse(execution.IsCompleted, "Execution should still be running after the first live output update.");

        var result = await WaitForAsync(execution, "Expected execution to complete.");
        Assert.AreEqual("Success", result.Status);
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("before")), "Expected streamed host output in cell outputs.");
    }

    [TestMethod]
    public async Task ExecuteCell_ReadHost_UsesServerInputRequester()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(
            service,
            "$name = Read-Host 'Name'\nWrite-Host \"hello $name\"");
        var inputRequested = new TaskCompletionSource<ServerInputRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.OnInputRequested += () =>
        {
            var request = service.PendingInputRequest;
            if (request is null)
                return;

            inputRequested.TrySetResult(request);
            service.ResolveInputResult("Ada", cancelled: false);
        };

        var result = await WaitForAsync(
            service.ExecuteCellAsync(cell.Id),
            "Expected Read-Host execution to complete.");
        var request = await WaitForAsync(inputRequested.Task, "Expected server input request.");

        Assert.AreEqual(cell.Id, request.CellId);
        Assert.IsFalse(request.IsPassword);
        Assert.IsTrue(request.Prompt.Contains("Name"), $"Expected Name prompt, got: {request.Prompt}");
        Assert.AreEqual("Success", result.Status);
        Assert.IsTrue(
            cell.Outputs.Any(o => o.Content.Contains("hello Ada")),
            $"Expected supplied input in output, got: {string.Join(" | ", cell.Outputs.Select(o => o.Content))}");
    }

    [TestMethod]
    public async Task ExecuteCell_ReadHostCancellation_CompletesAsCancelled()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(
            service,
            "$name = Read-Host 'Name'\nWrite-Host \"hello $name\"");
        var inputRequested = new TaskCompletionSource<ServerInputRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.OnInputRequested += () =>
        {
            var request = service.PendingInputRequest;
            if (request is null)
                return;

            inputRequested.TrySetResult(request);
            service.ResolveInputResult(null, cancelled: true);
        };

        var result = await WaitForAsync(
            service.ExecuteCellAsync(cell.Id),
            "Expected cancelled Read-Host execution to complete.");

        await WaitForAsync(inputRequested.Task, "Expected server input request.");
        Assert.IsTrue(
            string.Equals(result.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || cell.Outputs.Any(o => o.IsError),
            $"Expected cancelled or error output shape, got status {result.Status} and outputs: {string.Join(" | ", cell.Outputs.Select(o => o.Content))}");
    }

    [TestMethod]
    public async Task ExecuteCell_ReadHostAsSecureString_PropagatesPasswordFlag()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(
            service,
            "$secret = Read-Host 'Secret' -AsSecureString\nWrite-Host 'done'");
        var inputRequested = new TaskCompletionSource<ServerInputRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.OnInputRequested += () =>
        {
            var request = service.PendingInputRequest;
            if (request is null)
                return;

            inputRequested.TrySetResult(request);
            service.ResolveInputResult("s3cr3t", cancelled: false);
        };

        var result = await WaitForAsync(
            service.ExecuteCellAsync(cell.Id),
            "Expected secure Read-Host execution to complete.");
        var request = await WaitForAsync(inputRequested.Task, "Expected server input request.");

        Assert.IsTrue(request.IsPassword, "Expected secure Read-Host to request password input.");
        Assert.AreEqual("Success", result.Status);
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("done")), "Expected cell to continue after password input.");
    }

    [TestMethod]
    public async Task ExecuteCell_WriteInformation_ReturnsInformationOutput()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(
            service,
            "Write-Information 'info' -InformationAction Continue");

        var result = await WaitForAsync(
            service.ExecuteCellAsync(cell.Id),
            "Expected Write-Information execution to complete.");

        Assert.AreEqual("Success", result.Status);
        Assert.IsTrue(
            cell.Outputs.Any(o => o.Content.Contains("info")),
            $"Expected information output, got: {string.Join(" | ", cell.Outputs.Select(o => o.Content))}");
    }

    [TestMethod]
    public async Task GetDiagnostics_SyntaxError_ReturnsError()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(service, "");

        var result = await WaitForAsync(
            service.GetDiagnosticsAsync(cell.Id, "Write-Host 'ok'\nfunction { }"),
            "Expected diagnostics to complete.");

        Assert.IsNotNull(result);
        var error = result!.Items.FirstOrDefault(d => d.Severity == "Error");
        Assert.IsNotNull(error, "Expected a parse error for a function with no name.");
        Assert.AreEqual(1, error!.StartLine, "Positions should be 0-based and cell-relative.");
    }

    [TestMethod]
    public async Task GetDiagnostics_ValidSource_ReturnsEmptyList()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(service, "");

        var result = await WaitForAsync(
            service.GetDiagnosticsAsync(cell.Id, "Write-Host 'ok'"),
            "Expected diagnostics to complete.");

        // An answered request with no problems is an empty list, which clears the editor;
        // null is reserved for "could not answer".
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result!.Items.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_UnknownCell_ReturnsNull()
    {
        await using var service = await CreateServiceAsync();

        var result = await service.GetDiagnosticsAsync(Guid.NewGuid(), "function { }");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetDiagnostics_CellWithoutLanguage_UsesDefaultKernel()
    {
        await using var service = await CreateServiceAsync();
        var cell = await AddPowerShellCellAsync(service, "");
        service.DefaultKernelId = "powershell";
        cell.Language = null;

        var result = await WaitForAsync(
            service.GetDiagnosticsAsync(cell.Id, "function { }"),
            "Expected diagnostics to complete.");

        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Items.Any(d => d.Severity == "Error"),
            "A cell with no language runs on the notebook default, so it is checked by that kernel.");
    }

    [TestMethod]
    public async Task GetDiagnostics_MarkdownCell_ReturnsEmptyList()
    {
        await using var service = await CreateServiceAsync();
        var cell = await service.AddCellAsync("markdown");

        var result = await service.GetDiagnosticsAsync(cell.Id, "function { }");

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result!.Items.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_KernelThatFails_ReturnsNull()
    {
        // Null, not an empty list: the editor keeps the markers it shows when a request fails.
        await using var service = await CreateServiceAsync();
        service.Scaffold!.RegisterKernel(new ThrowingDiagnosticsKernel());
        var cell = await service.AddCellAsync("code", ThrowingDiagnosticsKernel.Id);

        var result = await service.GetDiagnosticsAsync(cell.Id, "x");

        Assert.IsNull(result);
    }

    private sealed class ThrowingDiagnosticsKernel : ILanguageKernel
    {
        public const string Id = "throwing-diagnostics";

        public string ExtensionId => "com.test.throwing-diagnostics";
        public string Name => "Throwing Diagnostics";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LanguageId => Id;
        public string DisplayName => "Throwing Diagnostics";
        public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
            => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
            => throw new InvalidOperationException("analysis failed");
        public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
            => Task.FromResult<HoverInfo?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<ServerNotebookService> CreateServiceAsync()
    {
        var service = new ServerNotebookService(new ThrowingJSRuntime(), new LayoutAssetCache());
        await service.NewNotebookAsync();
        Assert.IsTrue(
            service.RegisteredLanguages.Any(l => string.Equals(l.Id, "powershell", StringComparison.OrdinalIgnoreCase)),
            "Expected PowerShell kernel to be registered.");
        return service;
    }

    private static async Task<CellModel> AddPowerShellCellAsync(ServerNotebookService service, string source)
    {
        var cell = await service.AddCellAsync("code", "powershell");
        await service.UpdateCellSourceAsync(cell.Id, source);
        return cell;
    }

    // The waits below exist to fail a test that hangs, not to time anything. The first cell
    // in the process pays for loading PowerShell and opening a runspace, which on a busy
    // Windows build agent has taken longer than ten seconds, so the limit is set well
    // clear of that. A test that passes never waits anywhere near this long.
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(60);

    private static async Task WaitForAsync(Task task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(HangTimeout));
        Assert.AreSame(task, completed, failureMessage);
        await task;
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(HangTimeout));
        Assert.AreSame(task, completed, failureMessage);
        return await task;
    }

    private sealed class ThrowingJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new InvalidOperationException($"Unexpected JS interop call: {identifier}");
    }
}
