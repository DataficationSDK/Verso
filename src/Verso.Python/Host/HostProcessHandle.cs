using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Verso.Python.Host;

/// <summary>
/// A running Python subprocess, abstracted over the platform-specific spawn mechanism so that
/// lifecycle handling is uniform.
/// </summary>
internal interface IHostProcessHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill();
}

/// <summary>Describes how to start a Python host subprocess.</summary>
internal sealed record HostProcessStartInfo(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>Starts a Python host subprocess using the best mechanism for the current platform.</summary>
internal static class HostProcessLauncher
{
    public static IHostProcessHandle Launch(HostProcessStartInfo startInfo)
    {
        // Windows needs the child placed in its own process group at creation time so a later
        // console control event can be delivered to it. The managed process API cannot request
        // that flag, so Windows uses a native spawn; other platforms use the managed path.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHostProcessHandle(startInfo);
        return new ManagedHostProcessHandle(startInfo);
    }
}

/// <summary>
/// Subprocess backed by <see cref="Process"/>. Used on Unix platforms, where the managed launcher
/// already provides the required output capture and lifecycle behavior.
/// </summary>
internal sealed class ManagedHostProcessHandle : IHostProcessHandle
{
    private readonly Process _process;

    public ManagedHostProcessHandle(HostProcessStartInfo startInfo)
    {
        var psi = new ProcessStartInfo(startInfo.Executable)
        {
            WorkingDirectory = startInfo.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in startInfo.Arguments)
            psi.ArgumentList.Add(argument);

        foreach (var pair in startInfo.Environment)
            psi.EnvironmentVariables[pair.Key] = pair.Value;

        _process = Process.Start(psi)
            ?? throw new PythonHostException($"Failed to start the Python subprocess '{startInfo.Executable}'.");
    }

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    public TextReader StandardOutput => _process.StandardOutput;
    public TextReader StandardError => _process.StandardError;

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already exited */ }
    }

    public void Dispose() => _process.Dispose();
}
