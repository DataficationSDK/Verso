using Verso.Abstractions;
using Verso.Http.Kernel;

namespace Verso.Http.MagicCommands;

/// <summary>
/// <c>#!http-set-base &lt;url&gt;</c> — sets the base URL for relative HTTP request URLs.
/// </summary>
[VersoExtension]
public sealed class HttpSetBaseMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.http.magic.http-set-base";
    string IExtension.Name => "HTTP Set Base Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => "Sets the base URL for relative HTTP request URLs.";

    // --- IMagicCommand ---
    public string Name => "http-set-base";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("url", "The base URL to prepend to relative request URLs.", typeof(string), IsRequired: true),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var url = arguments.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: URL is required. Usage: #!http-set-base <url>",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        context.Variables.Set(HttpKernel.BaseUrlStoreKey, url);

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"HTTP base URL set to: {url}")).ConfigureAwait(false);
    }
}
