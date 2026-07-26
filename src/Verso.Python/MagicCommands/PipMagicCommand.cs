using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Python.PackageManagement;

namespace Verso.Python.MagicCommands;

/// <summary>
/// <c>#!pip &lt;packages&gt; [options]</c> installs Python packages before the remaining cell code
/// executes. Packages land in the environment the kernel is actually running, so the import in the
/// next line finds them.
/// </summary>
[VersoExtension]
public sealed class PipMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.pip";
    string IExtension.Name => "Pip Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "pip";
    public string Description => "Installs Python packages via pip for use in subsequent code.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("packages", "One or more package specifiers (e.g. pandas==2.0 numpy).", typeof(string), IsRequired: true),
        new ParameterDefinition("options", "Additional pip options passed through to the install command.", typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = false;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Usage: #!pip <package> [package2 ...] [--options]", IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        // Be forgiving: strip leading "install" if the user writes `#!pip install requests`
        var args = arguments.Trim();
        if (args.StartsWith("install ", StringComparison.OrdinalIgnoreCase))
            args = args.Substring("install ".Length).TrimStart();

        var interpreter = await ResolveActiveInterpreterAsync(context).ConfigureAwait(false);
        if (interpreter is null)
        {
            // Packages go into the interpreter the cells run in, so without one there is nowhere
            // for them to land. The cell is stopped rather than run against an environment that
            // was never prepared.
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                "No Python interpreter is available, so there is nothing to install into. " +
                "Install Python 3.8 or newer, or select an interpreter with #!python.",
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        await InstallIntoActiveEnvironmentAsync(interpreter, args, context).ConfigureAwait(false);
    }

    // --- the interpreter the kernel is running ---

    /// <summary>
    /// The executable the Python kernel runs, or null when none could be resolved.
    /// </summary>
    private static async Task<string?> ResolveActiveInterpreterAsync(IMagicCommandContext context)
    {
        var kernel = FindPythonKernel(context);
        if (kernel is null)
            return null;

        try
        {
            return await kernel.GetActiveInterpreterAsync(context, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // No interpreter, or one that will not start. Either way there is nowhere to install
            // to, which the caller reports.
            return null;
        }
    }

    private static PythonKernel? FindPythonKernel(IMagicCommandContext context)
    {
        try { return context.ExtensionHost?.GetKernels().OfType<PythonKernel>().FirstOrDefault(); }
        catch { return null; }
    }

    private static async Task InstallIntoActiveEnvironmentAsync(
        string interpreter, string args, IMagicCommandContext context)
    {
        var succeeded = await PackageInstaller
            .InstallAsync(interpreter, PackageInstaller.SplitArguments(args), context)
            .ConfigureAwait(false);

        if (!succeeded)
            context.SuppressExecution = true;
    }
}
