using System.Diagnostics;
using System.Runtime.InteropServices;
using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;

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
        if (interpreter is not null)
        {
            await InstallIntoActiveEnvironmentAsync(interpreter, args, context).ConfigureAwait(false);
            return;
        }

        await InstallIntoEmbeddedVenvAsync(args, context).ConfigureAwait(false);
    }

    // --- the interpreter the kernel is running ---

    /// <summary>
    /// The executable the Python kernel runs, or null when Python runs inside this process and
    /// there is no separate environment to install into.
    /// </summary>
    private static async Task<string?> ResolveActiveInterpreterAsync(IMagicCommandContext context)
    {
        var kernel = FindPythonKernel(context);
        if (kernel is null || !kernel.UsesHostProcess)
            return null;

        try
        {
            return await kernel.GetActiveInterpreterAsync(context, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // No interpreter, or one that will not start. The cell's own execution reports the
            // actionable message; there is nothing useful to add from here.
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
        // The active environment is always writable: an interpreter carrying a marker that
        // forbids installs is replaced by a derived environment before the kernel runs it.
        var psi = BuildInstallCommand(interpreter, args);

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

            var failure = await StreamInstallAsync(process, context).ConfigureAwait(false);
            if (failure)
                context.SuppressExecution = true;
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Failed to run pip: {ex.Message}", IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
    }

    /// <summary>
    /// Builds the install command, preferring uv when it is available because it resolves and
    /// installs considerably faster. pip remains the fallback and the behavioral reference.
    /// </summary>
    internal static ProcessStartInfo BuildInstallCommand(string interpreter, string args, bool? useUv = null)
    {
        var withUv = useUv ?? UvTool.IsAvailable();

        var psi = withUv
            ? new ProcessStartInfo(UvTool.Executable)
            : new ProcessStartInfo(interpreter);

        if (withUv)
        {
            psi.ArgumentList.Add("pip");
            psi.ArgumentList.Add("install");
            psi.ArgumentList.Add("--python");
            psi.ArgumentList.Add(interpreter);
        }
        else
        {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("pip");
            psi.ArgumentList.Add("install");
            psi.ArgumentList.Add("--no-input");
        }

        foreach (var argument in SplitArguments(args))
            psi.ArgumentList.Add(argument);

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    /// <summary>Splits an argument string on whitespace, honoring quoted spans.</summary>
    internal static IReadOnlyList<string> SplitArguments(string args)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';

        foreach (var character in args)
        {
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                else current.Append(character);
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    /// <summary>
    /// Forwards installer output as it arrives. Resolving a large dependency set takes long
    /// enough that a silent cell looks stalled.
    /// </summary>
    private static async Task<bool> StreamInstallAsync(Process process, IMagicCommandContext context)
    {
        var cancellationToken = context.CancellationToken;

        // Both pipes are read at once so neither can fill and block the installer, but the cell
        // takes one line at a time: the output pipeline is not written for concurrent callers.
        using var writing = new SemaphoreSlim(1, 1);

        async Task PumpAsync(StreamReader reader)
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;

                await writing.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await context.WriteOutputAsync(new CellOutput("text/plain", line))
                        .ConfigureAwait(false);
                }
                finally
                {
                    writing.Release();
                }
            }
        }

        var stdout = PumpAsync(process.StandardOutput);

        // pip writes progress and warnings to stderr even on success, so it is reported as
        // ordinary output and the exit code decides whether the install failed.
        var stderr = PumpAsync(process.StandardError);

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode == 0)
            return false;

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"pip install failed (exit code {process.ExitCode}).", IsError: true))
            .ConfigureAwait(false);
        return true;
    }

    // --- the embedded runtime's own environment ---

    private static async Task InstallIntoEmbeddedVenvAsync(string args, IMagicCommandContext context)
    {
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

        // Fast-path: if every requested package directory already exists in the
        // install location, skip the expensive pip subprocess entirely.
        if (VenvManager.ArePackagesInstalled(args))
        {
            var packagePaths = await VenvManager.GetAllPackagePathsAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (packagePaths.Count > 0)
            {
                context.Variables.Set(VenvManager.SitePackagesStoreKey,
                    string.Join(Path.PathSeparator.ToString(), packagePaths));
            }
            return; // packages present, silently continue to cell code
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
