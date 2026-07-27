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
    /// <summary>How long the launcher is given to name an interpreter before it is abandoned.</summary>
    private static readonly TimeSpan LauncherTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long the launcher's output is collected for after it has already exited.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

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
            var launcherPath = SearchDirectories()
                .Select(dir => Path.Combine(dir, "py.exe"))
                .FirstOrDefault(File.Exists);

            if (launcherPath is not null)
            {
                var launcher = ResolvePyLauncher(launcherPath);
                if (launcher is not null && File.Exists(launcher))
                    yield return launcher;
            }
        }

        string[] leaves = IsWindows ? new[] { "python3.exe", "python.exe" } : new[] { "python3", "python" };

        foreach (var dir in SearchDirectories())
        {
            foreach (var leaf in leaves)
            {
                var candidate = Path.Combine(dir, leaf);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    /// <summary>
    /// The executable search path, without the Windows Store stubs that report themselves there even
    /// when Python is not installed. The launcher is looked for here rather than run by bare command
    /// name, so that a stub cannot answer for it either.
    /// </summary>
    private IEnumerable<string> SearchDirectories()
    {
        var pathVar = _getEnv("PATH");
        if (string.IsNullOrEmpty(pathVar))
            yield break;

        foreach (var dir in pathVar!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return dir;
        }
    }

    /// <summary>
    /// Asks the launcher which interpreter it would run for Python 3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The launcher hands its own standard output to the interpreter it starts, so that pipe reaches
    /// end of stream only once both processes have let go of it. Reading to the end therefore cannot
    /// bound this call, and a launcher whose child does not exit would otherwise block the search
    /// indefinitely. The wait does the bounding, and a launcher that overruns it is killed along with
    /// the interpreter it started.
    /// </para>
    /// <para>
    /// Standard input is redirected and immediately closed rather than left to be inherited. A tool
    /// that decides to prompt then reads end of stream and gives up, instead of waiting on a handle
    /// belonging to whatever started this process.
    /// </para>
    /// </remarks>
    private static string? ResolvePyLauncher(string launcherPath)
    {
        try
        {
            var psi = new ProcessStartInfo(launcherPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-3");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import sys;print(sys.executable)");

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            process.StandardInput.Close();

            // Started before the wait so neither pipe can fill and stall the process we are waiting on.
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)LauncherTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            // The process has exited, so what it wrote is already in the pipe; this is a guard against
            // a lingering grandchild holding the handle open, not a second budget of any substance.
            if (!stdout.Wait(DrainTimeout))
                return null;

            var output = stdout.Result.Trim();
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
