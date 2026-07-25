using System.Diagnostics;

namespace Verso.Python.Interpreter;

/// <summary>
/// Runs a tool to completion and reports only whether it succeeded. Used by the environment and
/// package provisioning paths, where the interesting outcome is the file system afterwards and a
/// failure is reported to the user through the caller's own message rather than the tool's output.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Start <paramref name="executable"/> with the given arguments and wait for it to exit.
    /// Returns whether it exited successfully; a tool that cannot be started returns false.
    /// </summary>
    public static async Task<bool> RunAsync(
        string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            // Both pipes are drained so a chatty tool cannot fill a buffer and deadlock.
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
