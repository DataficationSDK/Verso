using System.Diagnostics;
using System.Text;
using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Resources;

namespace Verso.Python.PackageManagement;

/// <summary>
/// Installs distributions into the environment the kernel is running and reports what it did.
/// Shared by the <c>#!pip</c> magic command and the automatic install path so both behave
/// identically, including which tool runs and how much of its output the cell shows.
/// </summary>
internal static class PackageInstaller
{
    /// <summary>
    /// Run an install and report whether it succeeded. What happened is written to the cell
    /// before returning, so a caller only has to decide what to do next.
    /// </summary>
    /// <param name="interpreter">The interpreter whose environment receives the packages.</param>
    /// <param name="arguments">Specifiers and any pass-through installer options.</param>
    /// <param name="context">Where the report goes.</param>
    /// <param name="useUv">Overrides tool selection; null probes for uv.</param>
    /// <param name="detail">
    /// Whether to name every distribution that arrived rather than summarize the ones that were
    /// not asked for. Either way this is a report rather than a relay of the installer's output.
    /// </param>
    public static async Task<bool> InstallAsync(
        string interpreter,
        IReadOnlyList<string> arguments,
        IVersoContext context,
        bool? useUv = null,
        bool detail = false)
    {
        // The active environment is always writable: an interpreter carrying a marker that
        // forbids installs is replaced by a derived environment before the kernel runs it.
        var psi = BuildInstallCommand(interpreter, arguments, useUv);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                await WriteAsync(context, Strings.Install_StartFailed, isError: true)
                    .ConfigureAwait(false);
                return false;
            }

            var lines = await CollectAsync(process, context.CancellationToken).ConfigureAwait(false);
            return await ReportAsync(process.ExitCode, lines, arguments, context, detail)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteAsync(context, string.Format(Strings.Install_RunFailed, ex.Message), isError: true)
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
    /// Collects the installer's output rather than forwarding it. The whole run has to be in hand
    /// before it can be described, because the line naming the versions that were settled on is
    /// the last one printed. The caller says it is installing before this starts, so the cell is
    /// not silent while a large dependency set resolves.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CollectAsync(
        Process process, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        // Both pipes are read at once so neither can fill and block the installer. They share one
        // list, in arrival order, so a failure reads back the way the installer wrote it.
        var collecting = new object();

        async Task PumpAsync(StreamReader reader)
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;

                lock (collecting)
                    lines.Add(line);
            }
        }

        var stdout = PumpAsync(process.StandardOutput);

        // pip writes progress and warnings to stderr even on success, so both pipes are read the
        // same way and the exit code decides whether the install failed.
        var stderr = PumpAsync(process.StandardError);

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return lines;
    }

    /// <summary>
    /// Says what the run did. A failure reports the installer's output in full whatever the
    /// display setting is: that output is the only account of why it failed, and suppressing it
    /// would leave the reader with a package that did not arrive and no way to find out why.
    /// </summary>
    private static async Task<bool> ReportAsync(
        int exitCode,
        IReadOnlyList<string> lines,
        IReadOnlyList<string> arguments,
        IVersoContext context,
        bool detail)
    {
        var report = InstallReport.Parse(lines);

        if (exitCode != 0)
        {
            foreach (var line in report.Output)
                await WriteAsync(context, line).ConfigureAwait(false);

            await WriteAsync(context, string.Format(Strings.Install_ExitedWithCode, exitCode), isError: true)
                .ConfigureAwait(false);
            return false;
        }

        // An installer whose output says nothing recognizable is relayed as it stands when the
        // detail was asked for, rather than summarized into a claim about a run not understood.
        if (!report.Recognized && detail)
        {
            foreach (var line in report.Output)
                await WriteAsync(context, line).ConfigureAwait(false);
            return true;
        }

        foreach (var line in report.Summarize(arguments, detail))
        {
            // Written as ordinary output even when the installer called it an error, because a
            // cell carrying an error output is reported as having failed, and this one did not.
            await WriteAsync(context, line).ConfigureAwait(false);
        }

        return true;
    }

    private static Task WriteAsync(IVersoContext context, string message, bool isError = false)
        => context.WriteOutputAsync(new CellOutput("text/plain", message, IsError: isError));
}
