using System.Runtime.InteropServices;
using Verso.Abstractions;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!about</c> — outputs Verso version, runtime, and loaded extensions; suppresses execution.
/// </summary>
[VersoExtension]
public sealed class AboutMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.about";
    string IExtension.Name => "About Magic Command";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "about";
    public string Description => "Displays Verso version, runtime information, and loaded extensions.";
    public IReadOnlyList<ParameterDefinition> Parameters => Array.Empty<ParameterDefinition>();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var versoVersion = typeof(AboutMagicCommand).Assembly.GetName().Version?.ToString() ?? "0.5.0";
        var framework = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription;

        var lines = new List<string>
        {
            $"Verso v{versoVersion}",
            $"Runtime: {framework}",
            $"OS: {os}",
            ""
        };

        var extensions = context.ExtensionHost.GetLoadedExtensions();
        if (extensions.Count > 0)
        {
            lines.Add("Loaded extensions:");
            foreach (var ext in extensions)
            {
                lines.Add($"  {ext.ExtensionId} ({ext.Name}) v{ext.Version}");
            }
        }
        else
        {
            lines.Add("No extensions loaded.");
        }

        var output = new CellOutput("text/plain", string.Join(Environment.NewLine, lines));
        await context.WriteOutputAsync(output).ConfigureAwait(false);
    }
}
