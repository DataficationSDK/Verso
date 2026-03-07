using Verso.Abstractions;
using Verso.Http.Kernel;

namespace Verso.Http.MagicCommands;

/// <summary>
/// <c>#!http-set-header &lt;name&gt; &lt;value&gt;</c> — adds or updates a default HTTP header.
/// </summary>
[VersoExtension]
public sealed class HttpSetHeaderMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.http.magic.http-set-header";
    string IExtension.Name => "HTTP Set Header Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => "Adds or updates a default HTTP header for all requests.";

    // --- IMagicCommand ---
    public string Name => "http-set-header";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("name", "The header name.", typeof(string), IsRequired: true),
        new ParameterDefinition("value", "The header value.", typeof(string), IsRequired: true),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var trimmed = arguments.Trim();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: Both header name and value are required. Usage: #!http-set-header <name> <value>",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        var headerName = trimmed.Substring(0, spaceIndex);
        var headerValue = trimmed.Substring(spaceIndex + 1).Trim();

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: Header value cannot be empty. Usage: #!http-set-header <name> <value>",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        var headers = context.Variables.Get<Dictionary<string, string>>(HttpKernel.DefaultHeadersStoreKey)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        headers[headerName] = headerValue;
        context.Variables.Set(HttpKernel.DefaultHeadersStoreKey, headers);

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"Default header set: {headerName}: {headerValue}")).ConfigureAwait(false);
    }
}
