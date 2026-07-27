using System.Diagnostics;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.Interpreter;

/// <summary>
/// End-to-end interpreter discovery and managed-environment tests that require a Python 3 interpreter
/// on PATH. When none is found they report inconclusive rather than failing, so the suite stays green
/// on machines without Python. The uv-first environment path and the Windows py launcher tier are not
/// exercised here (uv and the launcher are not assumed present); those are covered by CI.
/// </summary>
[TestClass]
public sealed class InterpreterDiscoveryIntegrationTests
{
    private static string? _python;
    private readonly List<string> _tempRoots = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext context) => _python = FindPython();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task Resolve_FindsASupportedInterpreterOnPath()
    {
        RequirePython();
        var realPath = Environment.GetEnvironmentVariable("PATH");
        var locator = new InterpreterLocator(
            new InterpreterValidator(),
            name => name == "PATH" ? realPath : null,
            () => Array.Empty<string>());

        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.IsTrue(result.Found, "expected an interpreter to resolve from PATH");
        Assert.AreEqual("CPython", result.Interpreter!.Implementation);
        Assert.IsTrue(result.Interpreter.Version >= InterpreterValidator.MinimumVersion);
        Assert.AreEqual(InterpreterSource.Path, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task Validate_RealInterpreter_IsAcceptedAsSupportedCPython()
    {
        RequirePython();
        var validation = await new InterpreterValidator().ValidateAsync(_python!, CancellationToken.None);

        Assert.AreEqual(InterpreterValidationStatus.Valid, validation.Status);
        Assert.AreEqual("CPython", validation.Info!.Implementation);
        Assert.IsTrue(validation.Info.Version >= InterpreterValidator.MinimumVersion);
    }

    [TestMethod]
    public async Task VirtualEnv_ResolvesToTheVenvInterpreterNotItsBase()
    {
        RequirePython();
        var venvRoot = NewTempRoot("verso-venv-");
        Assert.IsTrue(await CreateVenvAsync(_python!, venvRoot, systemSitePackages: false),
            "failed to create a test virtual environment");

        var locator = new InterpreterLocator(
            new InterpreterValidator(),
            name => name == "VIRTUAL_ENV" ? venvRoot : null,
            () => Array.Empty<string>());

        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.IsTrue(result.Found);
        Assert.AreEqual(InterpreterSource.VirtualEnv, result.Interpreter!.Source);
        StringAssert.Contains(result.Interpreter.Executable, venvRoot,
            "the resolved interpreter should be the venv's own python, not the base interpreter");
    }

    [TestMethod]
    public async Task VirtualEnv_IsNeverReportedExternallyManaged()
    {
        // A venv installs into its own site directory and is always writable, but its stdlib path
        // resolves back to the base it was made from, where a distribution's install marker sits.
        // Reading the marker there would replace the user's own environment with a managed one and
        // put their packages somewhere they did not ask for.
        RequirePython();
        var venvRoot = NewTempRoot("verso-venv-managed-");
        Assert.IsTrue(await CreateVenvAsync(_python!, venvRoot, systemSitePackages: false),
            "failed to create a test virtual environment");

        var interpreter = ManagedEnvironment.GetInterpreterPath(venvRoot);
        var validation = await new InterpreterValidator()
            .ValidateAsync(interpreter, CancellationToken.None);

        Assert.AreEqual(InterpreterValidationStatus.Valid, validation.Status);
        Assert.IsFalse(validation.Info!.IsExternallyManaged,
            "a virtual environment is writable and must be used directly");
    }

    [TestMethod]
    public async Task ManagedEnvironment_CreatesOnceAndReuses()
    {
        RequirePython();
        var baseValidation = await new InterpreterValidator().ValidateAsync(_python!, CancellationToken.None);
        Assert.AreEqual(InterpreterValidationStatus.Valid, baseValidation.Status);

        var envsRoot = NewTempRoot("verso-managed-");
        var manager = new ManagedEnvironment(envsRoot);

        // Force the venv path; uv is not assumed present on the test host.
        var first = await manager.EnsureDerivedEnvironmentAsync(baseValidation.Info!, UvUsage.Off, CancellationToken.None);
        Assert.IsTrue(File.Exists(first));
        StringAssert.StartsWith(first, envsRoot);
        var createdAt = File.GetLastWriteTimeUtc(first);

        var second = await manager.EnsureDerivedEnvironmentAsync(baseValidation.Info!, UvUsage.Off, CancellationToken.None);

        Assert.AreEqual(first, second);
        Assert.AreEqual(createdAt, File.GetLastWriteTimeUtc(second), "the environment should be reused, not recreated");

        // The derived interpreter is itself a usable CPython.
        var derived = await new InterpreterValidator().ValidateAsync(first, CancellationToken.None);
        Assert.AreEqual(InterpreterValidationStatus.Valid, derived.Status);
    }

    // ---- helpers ----

    private string NewTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        _tempRoots.Add(root);
        return root;
    }

    private static async Task<bool> CreateVenvAsync(string python, string target, bool systemSitePackages)
    {
        var psi = new ProcessStartInfo(python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("venv");
        if (systemSitePackages)
            psi.ArgumentList.Add("--system-site-packages");
        psi.ArgumentList.Add(target);

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RequirePython()
    {
        if (_python is null)
            Prerequisite.Missing("Python 3 was not found on PATH.");
    }

    private static string? FindPython()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process is null) continue;
                process.WaitForExit(5000);
                if (process.HasExited && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }
        return null;
    }
}
