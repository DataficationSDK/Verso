using System.Diagnostics;
using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Exercises completions, hover, and diagnostics against a real Python subprocess. Interpreter
/// discovery is stubbed, as it is for the execution tests, so these measure the assistance rather
/// than the search. The analysis library is provisioned into its ordinary per-user location, which
/// is a cache: the first run on a machine installs it and later runs reuse it. A machine that
/// cannot install it reports inconclusive rather than failing, and the standard-library fallbacks
/// those machines rely on are covered by the host script's own test suite.
/// </summary>
[TestClass]
public sealed class IntelliSenseIntegrationTests
{
    private static string Python => PythonProbe.Require();

    private static PythonHostSession CreateSession(string? executable = null)
    {
        var python = executable ?? Python;
        var validator = new FakeInterpreterValidator()
            .Valid(python, version: AnalysisTools.InterpreterVersion.ToString());
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var options = new PythonKernelOptions { PythonExecutable = python };
        return new PythonHostSession(options, locator: locator);
    }

    private static async Task<PythonHostSession> StartedSessionAsync(
        string? notebookDirectory = null, string? executable = null)
    {
        var session = CreateSession(executable);

        if (notebookDirectory is null)
            await session.StartAsync(CancellationToken.None);
        else
            await session.EnsureBoundAsync(notebookDirectory, CancellationToken.None);

        return session;
    }

    private static Task RequireAnalysisAsync() => AnalysisTools.RequireAsync();

    /// <summary>
    /// What came back, for a failure message. Which component answered is readable from the
    /// shape: the analysis library reports the trailing segment of a name and the
    /// standard-library fallback reports the whole dotted path, so a failure that lists
    /// "items.append" is a fallback and one that lists nothing is a request that went unanswered.
    /// </summary>
    private static string Describe(IReadOnlyList<Completion> completions)
        => completions.Count == 0
            ? "nothing"
            : string.Join(", ", completions.Take(8).Select(c => c.DisplayText));

    // --- completions ---

    [TestMethod]
    public async Task Completions_ReportModuleMembers()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        var completions = await session.GetCompletionsAsync("os.", 3);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"),
            "Expected 'path' among the members of os.");
        Assert.IsTrue(completions.All(c => !string.IsNullOrEmpty(c.Kind)),
            "Every completion needs a kind for the editor to render it.");
    }

    [TestMethod]
    public async Task Completions_FilterByWhatHasBeenTyped()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        var completions = await session.GetCompletionsAsync("os.pa", 5);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"));
    }

    [TestMethod]
    public async Task Completions_SeeNamesFromAnEarlierCell()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        await session.ExecuteAsync("items = [1, 2, 3]", new StubExecutionContext());
        var completions = await session.GetCompletionsAsync("items.", 6);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "append"),
            "Expected list members for a name defined by an earlier cell. Got: " + Describe(completions));
    }

    [TestMethod]
    public async Task Completions_ReflectTheEnvironmentTheCellsRunIn()
    {
        // The regression the out-of-process host exists for: assistance used to come from a
        // separate interpreter, so it never saw what the notebook could actually import.
        await RequireAnalysisAsync();

        var directory = Directory.CreateTempSubdirectory("verso-intel-").FullName;
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "verso_probe_module.py"),
                "def probe_value():\n    return 41\n");

            await using var session = await StartedSessionAsync(directory);

            await session.ExecuteAsync("import verso_probe_module", new StubExecutionContext());

            const string code = "verso_probe_module.";
            var completions = await session.GetCompletionsAsync(code, code.Length);

            Assert.IsTrue(completions.Any(c => c.DisplayText == "probe_value"),
                "Expected a member of a module importable only in the notebook's own directory.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task Completions_SeePackagesInstalledInTheSelectedEnvironment()
    {
        // The same regression from the other direction, and the one that matters most in practice:
        // a package present only in the environment the cells run in still has to complete.
        await RequireAnalysisAsync();

        var root = Directory.CreateTempSubdirectory("verso-env-").FullName;
        try
        {
            var environmentPath = Path.Combine(root, "env");
            if (!await ProcessRunner.RunAsync(
                    Python, new[] { "-m", "venv", environmentPath }, CancellationToken.None))
            {
                Prerequisite.Missing("A virtual environment could not be created.");
            }

            var interpreter = ManagedEnvironment.GetInterpreterPath(environmentPath);
            var sitePackages = QuerySitePackages(interpreter);
            if (sitePackages is null || !Directory.Exists(sitePackages))
                Prerequisite.Missing("The new environment did not report a package directory.");

            // Written where an install would put it, so nothing about this module is knowable
            // from the managing process.
            await File.WriteAllTextAsync(
                Path.Combine(sitePackages!, "verso_installed_probe.py"),
                "def probe_installed():\n    return 41\n");

            await using var session = await StartedSessionAsync(executable: interpreter);

            await session.ExecuteAsync("import verso_installed_probe", new StubExecutionContext());

            const string code = "verso_installed_probe.";
            var completions = await session.GetCompletionsAsync(code, code.Length);

            Assert.IsTrue(completions.Any(c => c.DisplayText == "probe_installed"),
                "Expected a member of a package installed only in the selected environment.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? QuerySitePackages(string interpreter)
    {
        var psi = new ProcessStartInfo(interpreter)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import sysconfig; print(sysconfig.get_paths()['purelib'])");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(15000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    // --- hover ---

    [TestMethod]
    public async Task Hover_DescribesABuiltin()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        var info = await session.GetHoverInfoAsync("len", 1);

        Assert.IsNotNull(info);
        StringAssert.Contains(info!.Content, "len");
        Assert.IsNotNull(info.Range);
        Assert.AreEqual((0, 0, 0, 3), info.Range!.Value);
    }

    [TestMethod]
    public async Task Hover_OnWhitespaceIsNull()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        Assert.IsNull(await session.GetHoverInfoAsync("x = 1   ", 7));
    }

    // --- diagnostics ---

    [TestMethod]
    public async Task Diagnostics_ReportASyntaxErrorWhereItIs()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        var diagnostics = await session.GetDiagnosticsAsync("x = 1\ndef f(:");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(1, diagnostics[0].StartLine);
        Assert.AreEqual("SyntaxError", diagnostics[0].Code);
        Assert.IsFalse(string.IsNullOrEmpty(diagnostics[0].Message));
    }

    [TestMethod]
    public async Task Diagnostics_AreEmptyForValidCode()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        Assert.AreEqual(0, (await session.GetDiagnosticsAsync("x = 1\nprint(x)\n")).Count);
    }

    [TestMethod]
    public async Task Diagnostics_AcceptANameFromAnEarlierCell()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        await session.ExecuteAsync("value = 41", new StubExecutionContext());

        Assert.AreEqual(0, (await session.GetDiagnosticsAsync("result = value + 1")).Count);
    }

    // --- interaction with execution ---

    [TestMethod]
    public async Task Assistance_IsEmptyWhileACellRunsAndResumesAfterwards()
    {
        await RequireAnalysisAsync();
        await using var session = await StartedSessionAsync();

        // Warm the analysis so the timing below measures the guard rather than a first import.
        await session.GetCompletionsAsync("os.", 3);

        var cell = session.ExecuteAsync("import time\ntime.sleep(1.5)", new StubExecutionContext());

        // Long enough for the request to be in flight, short enough to leave most of the sleep.
        await Task.Delay(250);

        var stopwatch = Stopwatch.StartNew();
        var duringCell = await session.GetCompletionsAsync("os.", 3);
        stopwatch.Stop();

        Assert.AreEqual(0, duringCell.Count, "A request must not be answered from a namespace in flux.");
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 750,
            $"The request should return at once rather than wait out the cell; took {stopwatch.ElapsedMilliseconds} ms.");

        await cell;

        var afterCell = await session.GetCompletionsAsync("os.", 3);
        Assert.IsTrue(afterCell.Count > 0, "Assistance has to come back once the cell is done.");
    }

    [TestMethod]
    public async Task Assistance_WithoutARunningProcessIsEmpty()
    {
        // No interpreter is needed for this one: the guards run before anything is sent.
        await using var session = CreateSession();

        Assert.AreEqual(0, (await session.GetCompletionsAsync("os.", 3)).Count);
        Assert.AreEqual(0, (await session.GetDiagnosticsAsync("def f(:")).Count);
        Assert.IsNull(await session.GetHoverInfoAsync("len", 1));
    }
}
