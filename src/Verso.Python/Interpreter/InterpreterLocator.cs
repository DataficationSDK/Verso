using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Verso.Python.Interpreter;

/// <summary>The inputs that steer interpreter resolution for a given notebook and session.</summary>
internal sealed record InterpreterQuery(
    string? ExplicitExecutable = null,
    string? SessionSelection = null,
    string? NotebookDirectory = null,
    string? WorkspaceRoot = null);

/// <summary>The outcome of resolving an interpreter, including what was searched when nothing qualified.</summary>
internal sealed record InterpreterResolution(
    InterpreterInfo? Interpreter,
    IReadOnlyList<string> SearchedLocations,
    IReadOnlyList<string> Rejections)
{
    public bool Found => Interpreter is not null;

    /// <summary>Builds a single actionable message describing what was searched and how to install Python.</summary>
    public string BuildNotFoundMessage()
    {
        var lines = new List<string> { "No suitable Python interpreter was found." };

        if (Rejections.Count > 0)
        {
            lines.Add("");
            foreach (var rejection in Rejections)
                lines.Add("  - " + rejection);
        }

        lines.Add("");
        lines.Add(InstallHint());
        return string.Join("\n", lines);
    }

    private static string InstallHint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "Install Python from python.org or run 'brew install python', then try again.";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Install Python from python.org or the Microsoft Store, then try again.";
        return "Install Python with your package manager (for example 'sudo apt install python3'), then try again.";
    }
}

/// <summary>
/// Resolves the Python executable to run for a notebook by consulting, in order: an explicit
/// configuration or the <c>VERSO_PYTHON</c> override, a session selection, the active virtual
/// environment, the active conda environment, a <c>.venv</c>/<c>venv</c> near the notebook, the
/// executable search path, and finally well-known installation locations. The first candidate that
/// validates wins. Unlike the shared-library detection it replaces, this resolves an executable and
/// does not collapse a virtual environment's interpreter to its base.
/// </summary>
internal sealed class InterpreterLocator
{
    private readonly IInterpreterValidator _validator;
    private readonly Func<string, string?> _getEnv;
    private readonly Func<IEnumerable<string>> _wellKnown;

    internal InterpreterLocator(
        IInterpreterValidator validator,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<IEnumerable<string>>? wellKnownCandidates = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _getEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _wellKnown = wellKnownCandidates ?? WellKnownInterpreterPaths.EnumerateCandidates;
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Resolves the highest-precedence valid interpreter, or a not-found result.</summary>
    public async Task<InterpreterResolution> ResolveAsync(InterpreterQuery query, CancellationToken cancellationToken)
    {
        var searched = new List<string>();
        var rejections = new List<string>();

        foreach (var (source, candidate) in EnumerateCandidates(query))
        {
            searched.Add(candidate);
            var validation = await _validator.ValidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            switch (validation.Status)
            {
                case InterpreterValidationStatus.Valid:
                    return new InterpreterResolution(validation.Info! with { Source = source }, searched, rejections);
                case InterpreterValidationStatus.Rejected:
                    rejections.Add(validation.RejectionReason!);
                    break;
            }
        }

        return new InterpreterResolution(null, searched, rejections);
    }

    /// <summary>Validates a single candidate path or command through the same validator used by resolution.</summary>
    public Task<InterpreterValidation> ValidateAsync(string candidate, CancellationToken cancellationToken)
        => _validator.ValidateAsync(candidate, cancellationToken);

    /// <summary>Discovers every valid interpreter across all tiers, de-duplicated by resolved executable.</summary>
    public async Task<IReadOnlyList<InterpreterInfo>> DiscoverAllAsync(
        InterpreterQuery query, CancellationToken cancellationToken)
    {
        var found = new List<InterpreterInfo>();
        var seen = new HashSet<string>(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var (source, candidate) in EnumerateCandidates(query))
        {
            var validation = await _validator.ValidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (validation.Status != InterpreterValidationStatus.Valid)
                continue;

            var info = validation.Info! with { Source = source };
            if (seen.Add(info.Executable))
                found.Add(info);
        }

        return found;
    }

    private IEnumerable<(InterpreterSource Source, string Candidate)> EnumerateCandidates(InterpreterQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.ExplicitExecutable))
            yield return (InterpreterSource.Explicit, query.ExplicitExecutable!);

        var envOverride = _getEnv("VERSO_PYTHON");
        if (!string.IsNullOrWhiteSpace(envOverride))
            yield return (InterpreterSource.EnvironmentOverride, envOverride!);

        if (!string.IsNullOrWhiteSpace(query.SessionSelection))
            yield return (InterpreterSource.SessionSelection, query.SessionSelection!);

        var virtualEnv = _getEnv("VIRTUAL_ENV");
        if (!string.IsNullOrWhiteSpace(virtualEnv))
            yield return (InterpreterSource.VirtualEnv, VenvPython(virtualEnv!));

        var conda = _getEnv("CONDA_PREFIX");
        if (!string.IsNullOrWhiteSpace(conda))
            yield return (InterpreterSource.CondaPrefix, CondaPython(conda!));

        foreach (var candidate in WorkspaceVenvCandidates(query))
            yield return (InterpreterSource.WorkspaceVenv, candidate);

        foreach (var candidate in PathCandidates())
            yield return (InterpreterSource.Path, candidate);

        foreach (var candidate in _wellKnown())
            yield return (InterpreterSource.WellKnown, candidate);
    }

    private static string VenvPython(string root)
        => IsWindows ? Path.Combine(root, "Scripts", "python.exe") : Path.Combine(root, "bin", "python");

    private static string CondaPython(string prefix)
        => IsWindows ? Path.Combine(prefix, "python.exe") : Path.Combine(prefix, "bin", "python");

    private static IEnumerable<string> WorkspaceVenvCandidates(InterpreterQuery query)
    {
        if (string.IsNullOrEmpty(query.NotebookDirectory))
            yield break;

        DirectoryInfo? current;
        try { current = new DirectoryInfo(query.NotebookDirectory!); }
        catch { yield break; }

        while (current is not null)
        {
            foreach (var name in new[] { ".venv", "venv" })
            {
                var candidate = VenvPython(Path.Combine(current.FullName, name));
                if (File.Exists(candidate))
                    yield return candidate;
            }

            if (!string.IsNullOrEmpty(query.WorkspaceRoot) && PathsEqual(current.FullName, query.WorkspaceRoot!))
                break;

            current = current.Parent;
        }
    }

    private IEnumerable<string> PathCandidates()
    {
        if (IsWindows)
        {
            var launcher = ResolvePyLauncher();
            if (launcher is not null && File.Exists(launcher))
                yield return launcher;
        }

        var pathVar = _getEnv("PATH");
        if (string.IsNullOrEmpty(pathVar))
            yield break;

        string[] leaves = IsWindows ? new[] { "python3.exe", "python.exe" } : new[] { "python3", "python" };

        foreach (var dir in pathVar!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // Skip the Windows Store stub that reports itself on PATH even when Python is not installed.
            if (dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var leaf in leaves)
            {
                var candidate = Path.Combine(dir, leaf);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private static string? ResolvePyLauncher()
    {
        try
        {
            var psi = new ProcessStartInfo("py")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-3");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import sys;print(sys.executable)");

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        static string Normalize(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Normalize(a), Normalize(b), comparison);
    }
}
