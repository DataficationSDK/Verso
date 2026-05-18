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

    private static async Task<ServerNotebookService> CreateServiceAsync()
    {
        var service = new ServerNotebookService(new ThrowingJSRuntime());
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

    private static async Task WaitForAsync(Task task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(task, completed, failureMessage);
        await task;
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
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
