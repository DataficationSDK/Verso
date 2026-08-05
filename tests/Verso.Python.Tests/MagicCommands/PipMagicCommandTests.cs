using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.MagicCommands;
using Verso.Python.Tests.Host;
using Verso.Python.Tests.PackageManagement;
using Verso.Stubs;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.MagicCommands;

/// <summary>
/// Covers what <c>#!pip</c> does against a real interpreter, including the case that matters most
/// for a cell that gets run repeatedly: a line naming packages the environment already has.
/// Nothing here reaches a package index, so the tests are the same offline.
/// </summary>
[TestClass]
public sealed class PipMagicCommandTests
{
    private static string Python => PythonProbe.Require();

    private string _root = "";
    private string _interpreter = "";
    private Wheelhouse _wheelhouse = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("verso-pipmagic-").FullName;
        _wheelhouse = Wheelhouse.Create();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _wheelhouse.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A fresh environment, so a test never changes the developer's own.</summary>
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

    private async Task<(PythonKernel Kernel, StubMagicCommandContext Context)> KernelAsync()
    {
        var interpreter = await EnvironmentAsync();
        var kernel = new PythonKernel(new PythonKernelOptions
        {
            PythonExecutable = interpreter,
            // The cells here install explicitly, so nothing should be reaching for a package on
            // their behalf and confusing what the outputs are reporting.
            AutoInstall = AutoInstallPolicy.Off,
        });

        var context = new StubMagicCommandContext
        {
            ExtensionHost = new StubExtensionHostContext(() => new ILanguageKernel[] { kernel }),
        };

        return (kernel, context);
    }

    /// <summary>Installer arguments that keep the install local, written as a user would type them.</summary>
    private string Offline => $"--no-index --find-links \"{_wheelhouse.Directory}\" --no-deps";

    private static string TextOf(StubMagicCommandContext context)
        => string.Join("\n", context.WrittenOutputs.Select(o => o.Content));

    [TestMethod]
    public async Task APackageThatIsAlreadyThereIsNotInstalledAgain()
    {
        var (kernel, context) = await KernelAsync();
        await using var _ = kernel;

        var command = new PipMagicCommand();
        await command.ExecuteAsync($"{_wheelhouse.Distribution} {Offline}", context);
        Assert.IsFalse(context.SuppressExecution, TextOf(context));

        // The same line a second time, as a cell that is run again would.
        var second = new StubMagicCommandContext
        {
            ExtensionHost = context.ExtensionHost,
        };
        await command.ExecuteAsync(_wheelhouse.Distribution, second);

        Assert.AreEqual(0, second.WrittenOutputs.Count,
            "An install that would add nothing has nothing to say: " + TextOf(second));
        Assert.IsFalse(second.SuppressExecution, "The cell still runs.");
    }

    [TestMethod]
    public async Task AnOptionAlwaysReachesTheInstaller()
    {
        var (kernel, context) = await KernelAsync();
        await using var _ = kernel;

        var command = new PipMagicCommand();
        await command.ExecuteAsync($"{_wheelhouse.Distribution} {Offline}", context);

        var second = new StubMagicCommandContext { ExtensionHost = context.ExtensionHost };

        // An option can mean upgrade or reinstall, so the line is passed through whatever is
        // installed already.
        await command.ExecuteAsync($"{_wheelhouse.Distribution} --upgrade {Offline}", second);

        StringAssert.Contains(TextOf(second), "Installing " + _wheelhouse.Distribution);
    }

    [TestMethod]
    public async Task AVersionSpecifierAlwaysReachesTheInstaller()
    {
        var (kernel, context) = await KernelAsync();
        await using var _ = kernel;

        var command = new PipMagicCommand();
        await command.ExecuteAsync($"{_wheelhouse.Distribution} {Offline}", context);

        var second = new StubMagicCommandContext { ExtensionHost = context.ExtensionHost };

        // Whether 1.0.0 satisfies this is the installer's question to answer, not one that can
        // be settled by the distribution being present under that name.
        await command.ExecuteAsync($"{_wheelhouse.Distribution}>=2.0 {Offline}", second);

        StringAssert.Contains(TextOf(second), "Installing " + _wheelhouse.Distribution);
    }

    [TestMethod]
    public async Task APackageThatIsNotThereIsInstalled()
    {
        var (kernel, context) = await KernelAsync();
        await using var _ = kernel;

        var command = new PipMagicCommand();
        await command.ExecuteAsync($"{_wheelhouse.Distribution} {Offline}", context);

        var text = TextOf(context);
        StringAssert.Contains(text, "Installing " + _wheelhouse.Distribution);
        Assert.IsFalse(context.SuppressExecution, text);
    }
}
