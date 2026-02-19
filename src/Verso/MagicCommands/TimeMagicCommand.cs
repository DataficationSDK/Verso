using Verso.Abstractions;
using Verso.Contexts;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!time</c> — signals the pipeline to report elapsed time after kernel execution.
/// </summary>
[VersoExtension]
public sealed class TimeMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.time";
    string IExtension.Name => "Time Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "time";
    public string Description => "Reports elapsed wall-clock time after cell execution.";
    public IReadOnlyList<ParameterDefinition> Parameters => Array.Empty<ParameterDefinition>();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = false;

        if (context is MagicCommandContext mcc)
            mcc.ReportElapsedTime = true;

        return Task.CompletedTask;
    }
}
