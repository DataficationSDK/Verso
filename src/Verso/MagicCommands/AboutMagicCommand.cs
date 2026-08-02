using System.Runtime.InteropServices;
using Verso.Abstractions;
using Verso.Resources;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!about</c> outputs Verso version, runtime, and loaded extensions; suppresses execution.
/// </summary>
[VersoExtension]
public sealed class AboutMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.about";
    string IExtension.Name => "About Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "about";
    public string Description => Strings.Magic_About_Description;
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
            string.Format(Strings.Magic_About_Runtime, framework),
            string.Format(Strings.Magic_About_Os, os),
            ""
        };

        var extensions = context.ExtensionHost.GetLoadedExtensions();
        if (extensions.Count > 0)
        {
            lines.Add(Strings.Magic_About_LoadedExtensions);
            foreach (var ext in extensions)
            {
                lines.Add($"  {ext.ExtensionId} ({ext.Name}) v{ext.Version}");
            }
        }
        else
        {
            lines.Add(Strings.Magic_About_NoExtensions);
        }

        var output = new CellOutput("text/plain", string.Join(Environment.NewLine, lines));
        await context.WriteOutputAsync(output).ConfigureAwait(false);
    }
}
