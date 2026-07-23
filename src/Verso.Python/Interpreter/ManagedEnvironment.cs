using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Verso.Python.Kernel;

namespace Verso.Python.Interpreter;

/// <summary>
/// Provides a writable Python environment derived from an externally managed base interpreter (such as
/// a Homebrew or Debian system Python that carries a PEP 668 marker). The derived environment is a venv
/// created with <c>--system-site-packages</c>, so everything importable in the base stays importable
/// while package installs land in the writable layer. It is created once per base interpreter and
/// reused thereafter; user-owned environments (a venv, conda, or uv interpreter) are never substituted.
/// </summary>
internal sealed class ManagedEnvironment
{
    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".verso", "python", "envs");

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly string _root;
    private bool? _uvAvailable;

    /// <param name="environmentsRoot">Root directory for derived environments; defaults to the per-user location.</param>
    public ManagedEnvironment(string? environmentsRoot = null)
        => _root = environmentsRoot ?? DefaultRoot;

    /// <summary>The directory a derived environment for <paramref name="baseInterpreter"/> occupies.</summary>
    public string ComputeEnvironmentPath(InterpreterInfo baseInterpreter)
        => Path.Combine(_root, $"{baseInterpreter.Version}-{HashPath(baseInterpreter.Executable)}");

    /// <summary>The interpreter executable inside a derived environment directory.</summary>
    public static string GetInterpreterPath(string environmentPath)
        => IsWindows
            ? Path.Combine(environmentPath, "Scripts", "python.exe")
            : Path.Combine(environmentPath, "bin", "python");

    /// <summary>Returns whether the uv tool is available on the search path.</summary>
    public bool IsUvAvailable()
    {
        _uvAvailable ??= ProbeUv();
        return _uvAvailable.Value;
    }

    /// <summary>
    /// Ensures a derived environment exists for the given base interpreter and returns its executable.
    /// Reuses an existing environment; creates one with uv when available and permitted, otherwise with
    /// the standard-library <c>venv</c> module.
    /// </summary>
    public async Task<string> EnsureDerivedEnvironmentAsync(
        InterpreterInfo baseInterpreter, UvUsage useUv, CancellationToken cancellationToken)
    {
        var environmentPath = ComputeEnvironmentPath(baseInterpreter);
        var interpreter = GetInterpreterPath(environmentPath);

        if (File.Exists(interpreter))
            return interpreter;

        Directory.CreateDirectory(_root);

        var created = useUv == UvUsage.Auto && IsUvAvailable()
            ? await CreateWithUvAsync(baseInterpreter.Executable, environmentPath, cancellationToken).ConfigureAwait(false)
            : await CreateWithVenvAsync(baseInterpreter.Executable, environmentPath, cancellationToken).ConfigureAwait(false);

        if (!created || !File.Exists(interpreter))
            throw new InvalidOperationException(
                $"Failed to create a managed Python environment at {environmentPath}.");

        return interpreter;
    }

    private static Task<bool> CreateWithUvAsync(string basePython, string environmentPath, CancellationToken ct)
        => RunAsync("uv", new[] { "venv", "--python", basePython, "--system-site-packages", environmentPath }, ct);

    private static Task<bool> CreateWithVenvAsync(string basePython, string environmentPath, CancellationToken ct)
        => RunAsync(basePython, new[] { "-m", "venv", "--system-site-packages", environmentPath }, ct);

    private static async Task<bool> RunAsync(string executable, string[] arguments, CancellationToken ct)
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

            _ = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ProbeUv()
    {
        try
        {
            var psi = new ProcessStartInfo("uv")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string HashPath(string path)
    {
        var normalized = IsWindows ? path.ToLowerInvariant() : path;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }
}
