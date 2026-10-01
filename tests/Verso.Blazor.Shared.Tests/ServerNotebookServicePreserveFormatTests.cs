using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The editor's Save redirects a notebook opened from another format to a sibling .verso file
/// unless <see cref="ServerNotebookService.PreservesFormat"/> says the opened format is kept.
/// <c>verso serve --preserve-format</c> has to reach that decision, or the redirect replaces
/// the .verso file the user asked to leave alone.
/// </summary>
[TestClass]
public sealed class ServerNotebookServicePreserveFormatTests
{
    [TestMethod]
    public async Task PreservesFormat_Ipynb_WithPreserveFormatOption_IsTrue()
    {
        await using var service = await CreateServiceAsync(preserveFormat: true);

        Assert.IsTrue(service.PreservesFormat("report.ipynb"),
            "With --preserve-format, saving an .ipynb writes the .ipynb rather than a sibling .verso.");
    }

    [TestMethod]
    public async Task PreservesFormat_Ipynb_WithoutPreserveFormatOption_IsFalse()
    {
        await using var service = await CreateServiceAsync(preserveFormat: false);

        Assert.IsFalse(service.PreservesFormat("report.ipynb"),
            "By default an .ipynb is saved to a sibling .verso file.");
    }

    private static async Task<ServerNotebookService> CreateServiceAsync(bool preserveFormat)
    {
        var service = new ServerNotebookService(
            new ThrowingJSRuntime(),
            new LayoutAssetCache(),
            new NotebookServiceOptions { PreserveFormat = preserveFormat });
        await service.NewNotebookAsync();
        return service;
    }

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
}
