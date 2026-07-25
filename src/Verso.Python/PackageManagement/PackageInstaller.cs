using System.Diagnostics;
using System.Text;
using Verso.Abstractions;
using Verso.Python.Interpreter;

namespace Verso.Python.PackageManagement;

/// <summary>
/// Installs distributions into the environment the kernel is running, streaming the installer's
/// output into the cell as it arrives. Shared by the <c>#!pip</c> magic command and the
/// automatic install path so both behave identically, including which tool runs.
/// </summary>
internal static class PackageInstaller
{
    /// <summary>
    /// Run an install and report whether it succeeded. The failure message is written to the
    /// cell before returning, so a caller only has to decide what to do next.
    /// </summary>
    /// <param name="interpreter">The interpreter whose environment receives the packages.</param>
    /// <param name="arguments">Specifiers and any pass-through installer options.</param>
    /// <param name="context">Where the installer's output goes.</param>
    /// <param name="useUv">Overrides tool selection; null probes for uv.</param>
    public static async Task<bool> InstallAsync(
        string interpreter,
        IReadOnlyList<string> arguments,
        IVersoContext context,
        bool? useUv = null)
    {
        // The active environment is always writable: an interpreter carrying a marker that
        // forbids installs is replaced by a derived environment before the kernel runs it.
        var psi = BuildInstallCommand(interpreter, arguments, useUv);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                await WriteAsync(context, "Failed to start the package installer.", isError: true)
                    .ConfigureAwait(false);
                return false;
            }

            return await StreamAsync(process, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteAsync(context, $"Failed to run the package installer: {ex.Message}", isError: true)
                .ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Builds the install command, preferring uv when it is available because it resolves and
    /// installs considerably faster. pip remains the fallback and the behavioral reference.
    /// </summary>
    public static ProcessStartInfo BuildInstallCommand(
        string interpreter, IReadOnlyList<string> arguments, bool? useUv = null)
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

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    /// <summary>
    /// Builds the install command from an unparsed argument string, as a magic command
    /// receives it.
    /// </summary>
    public static ProcessStartInfo BuildInstallCommand(
        string interpreter, string arguments, bool? useUv = null)
        => BuildInstallCommand(interpreter, SplitArguments(arguments), useUv);

    /// <summary>Splits an argument string on whitespace, honoring quoted spans.</summary>
    public static IReadOnlyList<string> SplitArguments(string arguments)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var character in arguments ?? string.Empty)
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
    private static async Task<bool> StreamAsync(Process process, IVersoContext context)
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
            return true;

        await WriteAsync(context, $"Package install failed (exit code {process.ExitCode}).", isError: true)
            .ConfigureAwait(false);
        return false;
    }

    private static Task WriteAsync(IVersoContext context, string message, bool isError = false)
        => context.WriteOutputAsync(new CellOutput("text/plain", message, IsError: isError));
}
