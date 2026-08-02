using Verso.Abstractions;
using Verso.Resources;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!restart [kernelId]</c> restarts the specified kernel (or default); suppresses execution.
/// </summary>
[VersoExtension]
public sealed class RestartMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.restart";
    string IExtension.Name => Strings.Magic_Restart;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "restart";
    public string Description => Strings.Magic_Restart_Description;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("kernelId", Strings.Magic_Restart_Param_KernelId, typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var kernelId = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();

        await context.Notebook.RestartKernelAsync(kernelId).ConfigureAwait(false);

        var message = kernelId is not null
            ? string.Format(Strings.Magic_Restart_Done, kernelId)
            : Strings.Magic_Restart_DoneDefault;

        await context.WriteOutputAsync(new CellOutput("text/plain", message)).ConfigureAwait(false);
    }
}
