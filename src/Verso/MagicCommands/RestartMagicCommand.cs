using Verso.Abstractions;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!restart [kernelId]</c> — restarts the specified kernel (or default); suppresses execution.
/// </summary>
[VersoExtension]
public sealed class RestartMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.restart";
    string IExtension.Name => "Restart Magic Command";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "restart";
    public string Description => "Restarts the specified kernel, or the default kernel if no argument is given.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("kernelId", "The language ID of the kernel to restart.", typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var kernelId = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();

        await context.Notebook.RestartKernelAsync(kernelId).ConfigureAwait(false);

        var message = kernelId is not null
            ? $"Kernel '{kernelId}' restarted."
            : "Default kernel restarted.";

        await context.WriteOutputAsync(new CellOutput("text/plain", message)).ConfigureAwait(false);
    }
}
