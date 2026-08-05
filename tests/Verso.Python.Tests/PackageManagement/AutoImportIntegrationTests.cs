using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.PackageManagement;
using Verso.Python.Tests.Host;
using Verso.Python.Tests.Interpreter;
using Verso.Stubs;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Exercises the install path against a real Python subprocess and a real install, into a throwaway
/// environment and from a wheel this suite builds. Nothing here reaches a package index: the
/// installer is given a local directory and told not to use one, so the tests are the same offline
/// and on a machine with no network at all.
/// </summary>
[TestClass]
public sealed class AutoImportIntegrationTests
{
    private static string Python => PythonProbe.Require();

    private string _root = "";
    private string _interpreter = "";
    private Wheelhouse _wheelhouse = null!;
    private StubExecutionContext _context = null!;
    private StubExtensionHostContext _host = null!;
    private int _requestCount;
    private bool _approve;

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("verso-autoimport-").FullName;
        _wheelhouse = Wheelhouse.Create();

        _requestCount = 0;
        _approve = true;
        _host = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>())
        {
            ConsentHandler = (_, _) =>
            {
                _requestCount++;
                return Task.FromResult(_approve);
            },
        };

        _context = new StubExecutionContext { ExtensionHost = _host };
    }

    [TestCleanup]
    public void Cleanup()
    {
        _wheelhouse.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A fresh environment to install into, so a test never changes the developer's own. Creating
    /// one needs nothing but the interpreter, so a machine that cannot is reported as a missing
    /// prerequisite rather than worked around.
    /// </summary>
    private async Task<string> EnvironmentAsync()
    {
        if (_interpreter.Length > 0)
            return _interpreter;

        var path = Path.Combine(_root, "env");
        if (!await ProcessRunner.RunAsync(
                Python, new[] { "-m", "venv", path }, CancellationToken.None))
        {
            Prerequisite.Missing("A virtual environment could not be created.");
        }

        _interpreter = ManagedEnvironment.GetInterpreterPath(path);
        return _interpreter;
    }

    private async Task<PythonHostSession> StartedSessionAsync(
        AutoInstallPolicy policy = AutoInstallPolicy.Prompt,
        IReadOnlyList<string>? dependencies = null)
    {
        var interpreter = await EnvironmentAsync();

        var validator = new FakeInterpreterValidator().Valid(interpreter);
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var options = new PythonKernelOptions
        {
            PythonExecutable = interpreter,
            AutoInstall = policy,
            Dependencies = dependencies ?? Array.Empty<string>(),
        };

        var session = new PythonHostSession(options, locator: locator);

        // Pinned to pip against the local wheel directory. uv would resolve against an index.
        session.AutoInstall.UseUvOverride = false;
        session.AutoInstall.ExtraInstallArguments = _wheelhouse.OfflineArguments;

        await session.StartAsync(CancellationToken.None);
        return session;
    }

    private string Output => string.Join("\n", _context.WrittenOutputs.Select(o => o.Content));

    private static string OutputOf(IReadOnlyList<CellOutput> outputs)
        => string.Join("\n", outputs.Select(o => o.Content));

    // --- the pre-execution scan ---

    [TestMethod]
    public async Task AMissingImportIsInstalledBeforeTheCellRuns()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync(
            $"import {_wheelhouse.Module}\nprint({_wheelhouse.Module}.greet())", _context);

        Assert.AreEqual(1, _requestCount, "The install should have been asked about once.");
        StringAssert.Contains(
            OutputOf(outputs) + Output, _wheelhouse.Module + " loaded",
            "The cell should have run against the package that was just installed.");
    }

    [TestMethod]
    public async Task TheConsentRequestNamesTheDistributionAndTheEnvironment()
    {
        IReadOnlyList<ExtensionConsentInfo>? seen = null;
        _host.ConsentHandler = (items, _) => { seen = items; return Task.FromResult(true); };

        await using var session = await StartedSessionAsync();
        await session.ExecuteAsync($"import {_wheelhouse.Module}", _context);

        Assert.IsNotNull(seen);
        var item = seen!.Single();

        // Nothing knows a mapping for this name, so the module name is the distribution name,
        // which is what the user is shown and what pip is given.
        Assert.AreEqual(_wheelhouse.Module, item.PackageId);
        Assert.AreEqual(ConsentKind.Package, item.Kind);
        Assert.AreEqual($"import {_wheelhouse.Module}", item.Source);
        StringAssert.Contains(item.Target ?? "", _interpreter);
    }

    [TestMethod]
    public async Task DecliningStillRunsTheCellSoItFailsOnItsOwn()
    {
        _approve = false;
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync($"import {_wheelhouse.Module}", _context);

        Assert.AreEqual(1, _requestCount);
        StringAssert.Contains(OutputOf(outputs), "ModuleNotFoundError",
            "A refusal must not stop the cell; it fails at the import as it would have anyway.");
    }

    [TestMethod]
    public async Task AnAlreadySatisfiedImportAsksNothing()
    {
        await using var session = await StartedSessionAsync();

        await session.ExecuteAsync("import os, json", _context);

        Assert.AreEqual(0, _requestCount);
    }

    [TestMethod]
    public async Task AGuardedImportIsLeftAlone()
    {
        await using var session = await StartedSessionAsync();

        var code = $"try:\n    import {_wheelhouse.Module}\nexcept ImportError:\n    print('absent')";
        var outputs = await session.ExecuteAsync(code, _context);

        Assert.AreEqual(0, _requestCount, "A cell that handles the absence is not asking for it.");
        StringAssert.Contains(OutputOf(outputs) + Output, "absent");
    }

    [TestMethod]
    public async Task NothingIsInstalledWhenThePolicyIsOff()
    {
        await using var session = await StartedSessionAsync(AutoInstallPolicy.Off);

        var outputs = await session.ExecuteAsync($"import {_wheelhouse.Module}", _context);

        Assert.AreEqual(0, _requestCount);
        StringAssert.Contains(OutputOf(outputs), "ModuleNotFoundError");
    }

    // --- declared dependencies ---

    [TestMethod]
    public async Task ADeclaredDependencyIsInstalledOnTheFirstCell()
    {
        await using var session = await StartedSessionAsync(
            dependencies: new[] { _wheelhouse.Distribution });

        var outputs = await session.ExecuteAsync(
            $"import {_wheelhouse.Module}\nprint({_wheelhouse.Module}.VALUE)", _context);

        Assert.AreEqual(1, _requestCount);
        StringAssert.Contains(OutputOf(outputs) + Output, _wheelhouse.Module + " loaded");
    }

    [TestMethod]
    public async Task ADeclaredDependencyIsNotCheckedAgainOnLaterCells()
    {
        await using var session = await StartedSessionAsync(
            dependencies: new[] { _wheelhouse.Distribution });

        await session.ExecuteAsync("x = 1", _context);
        var before = _requestCount;
        await session.ExecuteAsync("y = 2", _context);

        Assert.AreEqual(before, _requestCount, "The list is checked once for the session.");
    }

    [TestMethod]
    public async Task AnInlineMetadataBlockDeclaresTheCellsDependencies()
    {
        await using var session = await StartedSessionAsync();

        var code =
            "# /// script\n" +
            $"# dependencies = [\"{_wheelhouse.Distribution}\"]\n" +
            "# ///\n" +
            $"import {_wheelhouse.Module}\n" +
            $"print({_wheelhouse.Module}.VALUE)";

        var outputs = await session.ExecuteAsync(code, _context);

        StringAssert.Contains(OutputOf(outputs) + Output, _wheelhouse.Module + " loaded");
    }

    // --- what the cell says about the install ---

    [TestMethod]
    public async Task AnInstallIsReportedAsThePackageAndVersionItAdded()
    {
        // Against a real pip, so the summary is held to what the installer actually printed
        // rather than to a sample of it.
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync($"import {_wheelhouse.Module}", _context);
        var all = OutputOf(outputs) + Output;

        // Named as the installer spelled it, which for a wheel is the underscore form.
        StringAssert.Contains(all, $"Installed {_wheelhouse.Module} 1.0.0.");
    }

    [TestMethod]
    public async Task TheInstallersOwnNarrationDoesNotReachTheCell()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync($"import {_wheelhouse.Module}", _context);
        var all = OutputOf(outputs) + Output;

        Assert.IsFalse(all.Contains("Processing "), "pip's per-file narration is the thing being removed");
        Assert.IsFalse(all.Contains("Looking in links"));
        Assert.IsFalse(all.Contains("Installing collected packages"));
    }

    [TestMethod]
    public async Task AFailedInstallStillReportsInFull()
    {
        // Hiding output must never hide a diagnosis: the installer's account of the failure is
        // the only one there is.
        await using var session = await StartedSessionAsync(AutoInstallPolicy.Auto);

        var code =
            "# /// script\n" +
            "# dependencies = [\"verso-no-such-distribution\"]\n" +
            "# ///\n" +
            "x = 1";

        var outputs = await session.ExecuteAsync(code, _context);
        var all = OutputOf(outputs) + Output;

        StringAssert.Contains(all, "verso-no-such-distribution");
        StringAssert.Contains(all, "Package install failed");
    }

    [TestMethod]
    public async Task AnInlineMetadataBlockCannotPassAnInstallerOption()
    {
        // Auto is the case that matters: a declared dependency installs there without asking, so
        // a notebook that could name an installer switch would be choosing where packages come
        // from on a machine whose owner never saw the string.
        await using var session = await StartedSessionAsync(AutoInstallPolicy.Auto);

        var code =
            "# /// script\n" +
            "# dependencies = [\"--index-url=http://127.0.0.1:1/simple\"]\n" +
            "# ///\n" +
            "print('ran')";

        var outputs = await session.ExecuteAsync(code, _context);
        var all = OutputOf(outputs) + Output;

        StringAssert.Contains(all, "installer options");
        Assert.IsFalse(all.Contains("Installing --index-url"), "The option must never reach the installer.");

        // The cell is not the casualty of its own dependency list.
        StringAssert.Contains(all, "ran");
    }

    [TestMethod]
    public async Task AnInlineMetadataBlockReportsAnInterpreterFloorItCannotMeet()
    {
        await using var session = await StartedSessionAsync(AutoInstallPolicy.Off);

        var code = "# /// script\n# requires-python = \">=99.0\"\n# ///\nx = 1";
        var outputs = await session.ExecuteAsync(code, _context);

        Assert.IsTrue(
            (OutputOf(outputs) + Output).Contains("99.0"),
            "A floor the running interpreter cannot meet is worth saying once.");
    }

    // --- the dynamic-import backstop ---

    [TestMethod]
    public async Task ADynamicImportIsInstalledAndTheCellRunsAgain()
    {
        await using var session = await StartedSessionAsync();

        // import_module hides the name from the scan, so only the failure can report it.
        var code =
            "import importlib\n" +
            $"mod = importlib.import_module('{_wheelhouse.Module}')\n" +
            "print(mod.VALUE)";

        var outputs = await session.ExecuteAsync(code, _context);
        var all = OutputOf(outputs) + Output;

        StringAssert.Contains(all, _wheelhouse.Module + " loaded",
            "A cell that had written nothing should be re-run after the install.");
        Assert.IsFalse(outputs.Any(o => o.IsError),
            "The retry's result replaces the first attempt's failure.");
    }

    [TestMethod]
    public async Task ACellThatAlreadyPrintedIsNotRunAgain()
    {
        await using var session = await StartedSessionAsync();

        // Output already written cannot be retracted, so a retry would repeat this line.
        var code =
            "print('step one')\n" +
            "import importlib\n" +
            $"importlib.import_module('{_wheelhouse.Module}')\n" +
            "print('step two')";

        var outputs = await session.ExecuteAsync(code, _context);
        var reported = OutputOf(outputs);

        StringAssert.Contains(reported, "Run the cell again");
        Assert.IsFalse(reported.Contains("step two"), "The cell must not have been re-run.");
        Assert.AreEqual(1, CountOccurrences(reported, "step one"),
            "A retry would have printed the first attempt's output a second time.");
    }

    [TestMethod]
    public async Task ADynamicImportOfSomethingRealIsNotTouched()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync(
            "import importlib\nprint(importlib.import_module('json').__name__)", _context);

        Assert.AreEqual(0, _requestCount);
        StringAssert.Contains(OutputOf(outputs) + Output, "json");
    }

    [TestMethod]
    public async Task ASubmoduleOfAPackageThatIsPresentIsNotOfferedForInstall()
    {
        await using var session = await StartedSessionAsync();

        // json is here and has no decoder2, so the import names something that does not exist
        // rather than something that is missing. Offering the package it is already running
        // against would install nothing and leave the cell failing the same way.
        var outputs = await session.ExecuteAsync("from json.decoder2 import thing", _context);
        var all = OutputOf(outputs) + Output;

        Assert.AreEqual(0, _requestCount, "There is nothing an install could add.");
        Assert.IsTrue(outputs.Any(o => o.IsError), "The import error is the answer.");
        Assert.IsFalse(all.Contains("Installing"), "Nothing should have been installed.");
        Assert.IsFalse(all.Contains("again"), "The cell should not have been re-run.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
