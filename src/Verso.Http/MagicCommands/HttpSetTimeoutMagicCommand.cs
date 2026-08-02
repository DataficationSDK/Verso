using Verso.Abstractions;
using Verso.Http.Kernel;
using Verso.Http.Localization;
using Verso.Http.Resources;

namespace Verso.Http.MagicCommands;

/// <summary>
/// <c>#!http-set-timeout &lt;seconds&gt;</c> — sets the default timeout for HTTP requests.
/// </summary>
[VersoExtension]
public sealed class HttpSetTimeoutMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.http.magic.http-set-timeout";
    string IExtension.Name => "HTTP Set Timeout Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => Strings.Magic_SetTimeout_Description;

    // --- IMagicCommand ---
    public string Name => "http-set-timeout";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("seconds", Strings.Magic_SetTimeout_Param_Seconds, typeof(int), IsRequired: true),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var trimmed = arguments.Trim();
        if (!int.TryParse(trimmed, out var seconds) || seconds <= 0)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", CellText.Error(Strings.Magic_SetTimeout_Invalid),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        context.Variables.Set(HttpKernel.TimeoutStoreKey, seconds);

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", string.Format(Strings.Magic_SetTimeout_Done, seconds))).ConfigureAwait(false);
    }
}
