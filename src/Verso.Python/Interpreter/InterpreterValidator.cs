using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Verso.Python.Interpreter;

/// <summary>The outcome of validating a candidate interpreter.</summary>
internal enum InterpreterValidationStatus
{
    /// <summary>A supported interpreter.</summary>
    Valid,

    /// <summary>A Python interpreter that does not meet the requirements (too old, or not CPython).</summary>
    Rejected,

    /// <summary>Nothing runnable was found at the candidate path.</summary>
    NotFound,
}

/// <summary>The result of validating a candidate interpreter, carrying either the facts or a reason.</summary>
internal sealed record InterpreterValidation(
    InterpreterValidationStatus Status,
    InterpreterInfo? Info,
    string? RejectionReason)
{
    public static readonly InterpreterValidation NotFound =
        new(InterpreterValidationStatus.NotFound, null, null);

    public static InterpreterValidation Rejected(string reason) =>
        new(InterpreterValidationStatus.Rejected, null, reason);

    public static InterpreterValidation Valid(InterpreterInfo info) =>
        new(InterpreterValidationStatus.Valid, info, null);
}

/// <summary>Validates candidate Python executables by running a short introspection probe.</summary>
internal interface IInterpreterValidator
{
    Task<InterpreterValidation> ValidateAsync(string executablePath, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a candidate interpreter with a small standard-library probe that reports its version,
/// resolved executable, implementation, and whether the standard library carries a PEP 668
/// externally-managed marker. Interpreters below the minimum version or not built on CPython are
/// rejected with an actionable message. Results are cached per path and invalidated by file mtime.
/// </summary>
internal sealed class InterpreterValidator : IInterpreterValidator
{
    /// <summary>The lowest interpreter version Verso supports.</summary>
    public static readonly Version MinimumVersion = new(3, 8);

    private const string RequiredImplementation = "CPython";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    // A single-argument probe passed via ArgumentList so no shell quoting is involved. It prints one
    // JSON line describing the interpreter. The externally-managed marker lives beside the standard
    // library per PEP 668.
    private const string ProbeScript =
        "import json, sys, sysconfig, os, platform\n" +
        "stdlib = sysconfig.get_path('stdlib') or ''\n" +
        "marker = os.path.join(stdlib, 'EXTERNALLY-MANAGED')\n" +
        "print(json.dumps({\n" +
        "    'version': platform.python_version(),\n" +
        "    'executable': sys.executable,\n" +
        "    'implementation': platform.python_implementation(),\n" +
        "    'managed': os.path.exists(marker),\n" +
        "}))";

    private static readonly Regex VersionPrefix = new(@"^\s*(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _cache = new();

    public async Task<InterpreterValidation> ValidateAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return InterpreterValidation.NotFound;

        // A full path we can stat lets us cache and invalidate by mtime. Bare command names (resolved
        // through PATH by the caller in most cases) are re-probed each time, which is rare.
        DateTime? mtime = null;
        if (File.Exists(executablePath))
        {
            mtime = File.GetLastWriteTimeUtc(executablePath);
            if (_cache.TryGetValue(executablePath, out var cached) && cached.MtimeUtc == mtime)
                return cached.Result;
        }

        var probe = await RunProbeAsync(executablePath, cancellationToken).ConfigureAwait(false);
        var result = probe is null ? InterpreterValidation.NotFound : ParseProbeOutput(probe, executablePath);

        if (mtime is not null)
            _cache[executablePath] = new CacheEntry(mtime.Value, result);

        return result;
    }

    /// <summary>
    /// Parses the JSON emitted by the probe into a validation result. Factored out so it can be tested
    /// without launching a Python process.
    /// </summary>
    internal static InterpreterValidation ParseProbeOutput(string probeOutput, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(probeOutput))
            return InterpreterValidation.NotFound;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(probeOutput);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // The candidate ran but did not produce our JSON; treat it as not a usable interpreter.
            return InterpreterValidation.NotFound;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return InterpreterValidation.NotFound;

        var rawVersion = GetString(root, "version");
        var implementation = GetString(root, "implementation");
        var executable = GetString(root, "executable");
        var managed = root.TryGetProperty("managed", out var m) && m.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(rawVersion) || !TryParseVersion(rawVersion!, out var version))
            return InterpreterValidation.NotFound;

        var resolvedExecutable = string.IsNullOrWhiteSpace(executable) ? candidatePath : executable!;
        var impl = string.IsNullOrWhiteSpace(implementation) ? "unknown" : implementation!;

        if (!string.Equals(impl, RequiredImplementation, StringComparison.OrdinalIgnoreCase))
            return InterpreterValidation.Rejected(
                $"Found {impl} {rawVersion} at {resolvedExecutable}, but only CPython is supported.");

        if (version < MinimumVersion)
            return InterpreterValidation.Rejected(
                $"Found Python {rawVersion} at {resolvedExecutable}, but Python {MinimumVersion.Major}.{MinimumVersion.Minor} or newer is required.");

        return InterpreterValidation.Valid(new InterpreterInfo(
            resolvedExecutable, version, rawVersion!, impl, managed));
    }

    /// <summary>Parses the numeric prefix of a Python version string, ignoring any pre-release suffix.</summary>
    internal static bool TryParseVersion(string rawVersion, out Version version)
    {
        var match = VersionPrefix.Match(rawVersion);
        if (!match.Success)
        {
            version = new Version(0, 0);
            return false;
        }

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var micro = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        version = new Version(major, minor, micro);
        return true;
    }

    private static async Task<string?> RunProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-I"); // isolate from user site and environment for a clean probe
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(ProbeScript);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch
        {
            return null; // nothing runnable at this path
        }

        if (process is null)
            return null;

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            return process.ExitCode == 0 ? stdout : null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct CacheEntry(DateTime MtimeUtc, InterpreterValidation Result);
}
