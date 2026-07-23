using System.Runtime.InteropServices;
using Verso.Python.Interpreter;

namespace Verso.Python.Tests.Interpreter;

[TestClass]
public sealed class InterpreterLocatorTests
{
    private readonly List<string> _tempRoots = new();
    private readonly Dictionary<string, string?> _env = new(StringComparer.Ordinal);

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ---- helpers ----

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string VenvPython(string root)
        => IsWindows ? Path.Combine(root, "Scripts", "python.exe") : Path.Combine(root, "bin", "python");

    private static string CondaPython(string prefix)
        => IsWindows ? Path.Combine(prefix, "python.exe") : Path.Combine(prefix, "bin", "python");

    private static string PathExecutable(string dir)
        => Path.Combine(dir, IsWindows ? "python3.exe" : "python3");

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verso-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempRoots.Add(dir);
        return dir;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private InterpreterLocator MakeLocator(FakeInterpreterValidator validator, IEnumerable<string>? wellKnown = null)
        => new(validator, name => _env.TryGetValue(name, out var v) ? v : null, () => wellKnown ?? Array.Empty<string>());

    // ---- precedence: each tier wins in isolation and over the one below it ----

    [TestMethod]
    public async Task Explicit_WinsOverEverythingElse()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        _env["VIRTUAL_ENV"] = "/venv";
        _env["CONDA_PREFIX"] = "/conda";

        var validator = new FakeInterpreterValidator()
            .Valid("/explicit/python")
            .Valid("/env/python")
            .Valid(VenvPython("/venv"))
            .Valid(CondaPython("/conda"));

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(ExplicitExecutable: "/explicit/python"), CancellationToken.None);

        Assert.IsTrue(result.Found);
        Assert.AreEqual("/explicit/python", result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.Explicit, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task EnvironmentOverride_BeatsSessionAndBelow()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        var validator = new FakeInterpreterValidator()
            .Valid("/env/python")
            .Valid("/session/python");

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(SessionSelection: "/session/python"), CancellationToken.None);

        Assert.AreEqual("/env/python", result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.EnvironmentOverride, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task SessionSelection_BeatsVirtualEnv()
    {
        _env["VIRTUAL_ENV"] = "/venv";
        var validator = new FakeInterpreterValidator()
            .Valid("/session/python")
            .Valid(VenvPython("/venv"));

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(SessionSelection: "/session/python"), CancellationToken.None);

        Assert.AreEqual("/session/python", result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.SessionSelection, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task VirtualEnv_BeatsConda()
    {
        _env["VIRTUAL_ENV"] = "/venv";
        _env["CONDA_PREFIX"] = "/conda";
        var validator = new FakeInterpreterValidator()
            .Valid(VenvPython("/venv"))
            .Valid(CondaPython("/conda"));

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.AreEqual(VenvPython("/venv"), result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.VirtualEnv, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task Conda_BeatsWorkspaceVenv()
    {
        _env["CONDA_PREFIX"] = "/conda";
        var notebookDir = NewTempDir();
        Touch(VenvPython(Path.Combine(notebookDir, ".venv")));

        var validator = new FakeInterpreterValidator()
            .Valid(CondaPython("/conda"))
            .Valid(VenvPython(Path.Combine(notebookDir, ".venv")));

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(NotebookDirectory: notebookDir), CancellationToken.None);

        Assert.AreEqual(CondaPython("/conda"), result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.CondaPrefix, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task WorkspaceVenv_BeatsPath()
    {
        var notebookDir = NewTempDir();
        var venvPython = VenvPython(Path.Combine(notebookDir, ".venv"));
        Touch(venvPython);

        var pathDir = NewTempDir();
        var pathPython = PathExecutable(pathDir);
        Touch(pathPython);
        _env["PATH"] = pathDir;

        var validator = new FakeInterpreterValidator().Valid(venvPython).Valid(pathPython);
        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(NotebookDirectory: notebookDir), CancellationToken.None);

        Assert.AreEqual(venvPython, result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.WorkspaceVenv, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task Path_BeatsWellKnown()
    {
        var pathDir = NewTempDir();
        var pathPython = PathExecutable(pathDir);
        Touch(pathPython);
        _env["PATH"] = pathDir;

        var validator = new FakeInterpreterValidator().Valid(pathPython).Valid("/well/known/python");
        var locator = MakeLocator(validator, wellKnown: new[] { "/well/known/python" });
        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.AreEqual(pathPython, result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.Path, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task WellKnown_UsedAsLastResort()
    {
        var validator = new FakeInterpreterValidator().Valid("/well/known/python");
        var locator = MakeLocator(validator, wellKnown: new[] { "/well/known/python" });
        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.AreEqual("/well/known/python", result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.WellKnown, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task RejectedHigherTier_FallsThroughAndRecordsRejection()
    {
        _env["VIRTUAL_ENV"] = "/venv";
        var validator = new FakeInterpreterValidator()
            .Rejected("/explicit/python", "Found Python 3.7.0 at /explicit/python, but Python 3.8 or newer is required.")
            .Valid(VenvPython("/venv"));

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(ExplicitExecutable: "/explicit/python"), CancellationToken.None);

        Assert.AreEqual(VenvPython("/venv"), result.Interpreter!.Executable);
        Assert.AreEqual(1, result.Rejections.Count);
        StringAssert.Contains(result.Rejections[0], "3.7.0");
    }

    [TestMethod]
    public async Task NotFound_ReportsSearchedLocationsAndActionableMessage()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        var validator = new FakeInterpreterValidator(); // nothing validates
        var locator = MakeLocator(validator);

        var result = await locator.ResolveAsync(new InterpreterQuery(ExplicitExecutable: "/explicit/python"), CancellationToken.None);

        Assert.IsFalse(result.Found);
        CollectionAssert.Contains(result.SearchedLocations.ToList(), "/explicit/python");
        CollectionAssert.Contains(result.SearchedLocations.ToList(), "/env/python");
        StringAssert.Contains(result.BuildNotFoundMessage(), "No suitable Python interpreter");
    }

    [TestMethod]
    public async Task WorkspaceVenv_NearestDirectoryWins()
    {
        var parent = NewTempDir();
        var child = Path.Combine(parent, "project", "notebooks");
        Directory.CreateDirectory(child);

        var parentVenv = VenvPython(Path.Combine(parent, ".venv"));
        var childVenv = VenvPython(Path.Combine(child, ".venv"));
        Touch(parentVenv);
        Touch(childVenv);

        var validator = new FakeInterpreterValidator().Valid(parentVenv).Valid(childVenv);
        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(
            new InterpreterQuery(NotebookDirectory: child, WorkspaceRoot: parent), CancellationToken.None);

        Assert.AreEqual(childVenv, result.Interpreter!.Executable);
        Assert.AreEqual(InterpreterSource.WorkspaceVenv, result.Interpreter.Source);
    }

    [TestMethod]
    public async Task VirtualEnv_ResolvesTheVenvInterpreterNotABaseElsewhere()
    {
        // The candidate handed to validation is the venv's own interpreter, never a base path.
        _env["VIRTUAL_ENV"] = "/home/user/project/.venv";
        var venvPython = VenvPython("/home/user/project/.venv");
        var validator = new FakeInterpreterValidator().Valid(venvPython);

        var locator = MakeLocator(validator);
        var result = await locator.ResolveAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.AreEqual(venvPython, result.Interpreter!.Executable);
        CollectionAssert.Contains(validator.ValidatedPaths, venvPython);
    }

    [TestMethod]
    public async Task DiscoverAll_ReturnsEveryValidTierWithItsSource()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        _env["VIRTUAL_ENV"] = "/venv";
        var validator = new FakeInterpreterValidator()
            .Valid("/env/python")
            .Valid(VenvPython("/venv"))
            .Valid("/well/known/python");

        var locator = MakeLocator(validator, wellKnown: new[] { "/well/known/python" });
        var all = await locator.DiscoverAllAsync(new InterpreterQuery(), CancellationToken.None);

        var sources = all.Select(i => i.Source).ToList();
        CollectionAssert.AreEquivalent(
            new[] { InterpreterSource.EnvironmentOverride, InterpreterSource.VirtualEnv, InterpreterSource.WellKnown },
            sources);
    }

    [TestMethod]
    public async Task DiscoverAll_DeduplicatesByResolvedExecutable()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        _env["VIRTUAL_ENV"] = "/venv";
        // Both candidates resolve to the same real executable.
        var validator = new FakeInterpreterValidator()
            .Alias("/env/python", "/shared/python")
            .Alias(VenvPython("/venv"), "/shared/python");

        var locator = MakeLocator(validator);
        var all = await locator.DiscoverAllAsync(new InterpreterQuery(), CancellationToken.None);

        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("/shared/python", all[0].Executable);
    }
}
