using System.Diagnostics;
using System.Runtime.InteropServices;
using Verso.Abstractions;
using Verso.Python.Kernel;

namespace Verso.Python.MagicCommands;

/// <summary>
/// <c>#!pip &lt;packages&gt; [options]</c> — installs Python packages into an isolated
/// virtual environment before the remaining cell code executes.
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

        // Resolve the system Python (used to create the venv if needed).
        // Prefer the executable that matches the DLL pythonnet loaded so the
        // venv uses the same Python version as the embedded runtime.
        var systemPython = PythonEngineManager.GetMatchingPythonExecutable()
            ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python.exe" : "python3");

        // Ensure the venv exists
        if (!await VenvManager.EnsureCreatedAsync(systemPython, context, context.CancellationToken)
                .ConfigureAwait(false))
        {
            context.SuppressExecution = true;
            return;
        }

        var venvPython = VenvManager.GetPythonPath();
        var pipExtra = VenvManager.GetPipInstallArgs();

        var psi = new ProcessStartInfo(venvPython, $"-m pip install --quiet --no-input {pipExtra} {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", "Failed to start pip process.", IsError: true))
                    .ConfigureAwait(false);
                context.SuppressExecution = true;
                return;
            }

            // Collect output into single blocks (--quiet keeps this minimal)
            var stdoutTask = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(context.CancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(context.CancellationToken).ConfigureAwait(false);

            var stdout = stdoutTask.Result.Trim();
            var stderr = stderrTask.Result.Trim();

            if (process.ExitCode != 0)
            {
                var errorMsg = !string.IsNullOrEmpty(stderr) ? stderr : stdout;
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain",
                    $"pip install failed (exit code {process.ExitCode}):\n{errorMsg}",
                    IsError: true))
                    .ConfigureAwait(false);
                context.SuppressExecution = true;
                return;
            }

            // Resolve and store package paths so the kernel can add them to sys.path.
            // On Windows this includes both the venv site-packages (jedi) and the
            // overlay directory (user #!pip installs).
            var packagePaths = await VenvManager.GetAllPackagePathsAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (packagePaths.Count > 0)
            {
                context.Variables.Set(VenvManager.SitePackagesStoreKey,
                    string.Join(Path.PathSeparator.ToString(), packagePaths));
            }

            // Show pip's summary (e.g. "Successfully installed X-1.0 Y-2.0") or our own confirmation
            var message = !string.IsNullOrEmpty(stdout) ? stdout : $"Installed: {args}";
            await context.WriteOutputAsync(new CellOutput("text/plain", message))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Failed to run pip: {ex.Message}",
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
    }
}
