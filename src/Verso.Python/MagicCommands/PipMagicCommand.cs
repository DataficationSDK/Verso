using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Python.PackageManagement;
using Verso.Python.Resources;

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
    string IExtension.Name => Strings.Magic_Pip_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "pip";
    public string Description => Strings.Magic_Pip_Description;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("packages", Strings.Magic_Pip_Param_Packages, typeof(string), IsRequired: true),
        new ParameterDefinition("options", Strings.Magic_Pip_Param_Options, typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = false;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", Strings.Magic_Pip_Usage, IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        // Be forgiving: strip leading "install" if the user writes `#!pip install requests`
        var args = arguments.Trim();
        if (args.StartsWith("install ", StringComparison.OrdinalIgnoreCase))
            args = args.Substring("install ".Length).TrimStart();

        var kernel = FindPythonKernel(context);

        var interpreter = await ResolveActiveInterpreterAsync(kernel, context).ConfigureAwait(false);
        if (interpreter is null)
        {
            // Packages go into the interpreter the cells run in, so without one there is nowhere
            // for them to land. The cell is stopped rather than run against an environment that
            // was never prepared.
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                Strings.Magic_Pip_NoInterpreter,
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        await InstallIntoActiveEnvironmentAsync(kernel, interpreter, args, context).ConfigureAwait(false);
    }

    // --- the interpreter the kernel is running ---

    /// <summary>
    /// The executable the Python kernel runs, or null when none could be resolved.
    /// </summary>
    private static async Task<string?> ResolveActiveInterpreterAsync(
        PythonKernel? kernel, IMagicCommandContext context)
    {
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
        PythonKernel? kernel, string interpreter, string args, IMagicCommandContext context)
    {
        var specifiers = PackageInstaller.SplitArguments(args);

        // Said before the install starts rather than after it finishes. Resolving a package with
        // a large dependency set takes long enough that a cell showing nothing looks stuck, and
        // the installer's own narration is no longer there to fill the gap.
        var packages = specifiers.Where(s => !s.StartsWith('-')).ToList();
        if (packages.Count > 0)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", string.Format(Strings.Magic_Pip_Installing, string.Join(", ", packages))))
                .ConfigureAwait(false);
        }

        var succeeded = await PackageInstaller
            .InstallAsync(interpreter, specifiers, context, detail: kernel?.ShowInstallOutput ?? false)
            .ConfigureAwait(false);

        if (!succeeded)
            context.SuppressExecution = true;
    }
}
