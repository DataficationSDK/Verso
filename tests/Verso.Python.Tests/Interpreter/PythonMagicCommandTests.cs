using System.Runtime.InteropServices;
using Verso.Python.Interpreter;
using Verso.Python.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Interpreter;

[TestClass]
public sealed class PythonMagicCommandTests
{
    private readonly List<string> _tempFiles = new();
    private readonly Dictionary<string, string?> _env = new(StringComparer.Ordinal);

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best effort */ }
        }
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string VenvPython(string root)
        => IsWindows ? Path.Combine(root, "Scripts", "python.exe") : Path.Combine(root, "bin", "python");

    private PythonMagicCommand MakeCommand(FakeInterpreterValidator validator)
    {
        var locator = new InterpreterLocator(
            validator,
            name => _env.TryGetValue(name, out var v) ? v : null,
            () => Array.Empty<string>());
        return new PythonMagicCommand(locator);
    }

    private string NewTempExecutable()
    {
        var path = Path.Combine(Path.GetTempPath(), "verso-python-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "");
        _tempFiles.Add(path);
        return path;
    }

    [TestMethod]
    public async Task Status_ShowsActiveInterpreter()
    {
        _env["VIRTUAL_ENV"] = "/venv";
        var validator = new FakeInterpreterValidator().Valid(VenvPython("/venv"), version: "3.12.4");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsFalse(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsFalse(context.WrittenOutputs[0].IsError);
        StringAssert.Contains(context.WrittenOutputs[0].Content, VenvPython("/venv"));
        StringAssert.Contains(context.WrittenOutputs[0].Content, "3.12.4");
    }

    [TestMethod]
    public async Task Status_ReportsWhenNoInterpreterFound()
    {
        var command = MakeCommand(new FakeInterpreterValidator());
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        StringAssert.Contains(context.WrittenOutputs[0].Content, "No suitable Python interpreter");
    }

    [TestMethod]
    public async Task List_ShowsDiscoveredInterpretersWithSources()
    {
        _env["VERSO_PYTHON"] = "/env/python";
        _env["VIRTUAL_ENV"] = "/venv";
        var validator = new FakeInterpreterValidator()
            .Valid("/env/python", version: "3.13.1")
            .Valid(VenvPython("/venv"), version: "3.11.8");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("--list", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        var content = context.WrittenOutputs[0].Content;
        StringAssert.Contains(content, "/env/python");
        StringAssert.Contains(content, VenvPython("/venv"));
        StringAssert.Contains(content, "VERSO_PYTHON");
        StringAssert.Contains(content, "virtual environment");
    }

    [TestMethod]
    public async Task Select_ByPath_StoresSessionSelection()
    {
        var executable = NewTempExecutable();
        var validator = new FakeInterpreterValidator().Valid(executable, version: "3.12.0");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync(executable, context);

        Assert.IsFalse(context.SuppressExecution);
        Assert.IsFalse(context.WrittenOutputs[0].IsError);
        StringAssert.Contains(context.WrittenOutputs[0].Content, "Selected");

        Assert.IsTrue(context.Variables.TryGet<string>(PythonMagicCommand.InterpreterSelectionStoreKey, out var stored));
        Assert.AreEqual(executable, stored);
    }

    [TestMethod]
    public async Task Select_WithoutTheHostProcess_SaysTheSelectionIsNotInEffect()
    {
        // No Python kernel is registered, so nothing consumes the selection. Reporting a restart
        // would imply the choice takes effect, which it does not until the host process is used.
        var executable = NewTempExecutable();
        var validator = new FakeInterpreterValidator().Valid(executable, version: "3.12.0");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync(executable, context);

        StringAssert.Contains(context.WrittenOutputs[0].Content, "not enabled for this session");
    }

    [TestMethod]
    public async Task Select_ByVersion_MatchesADiscoveredInterpreter()
    {
        _env["VIRTUAL_ENV"] = "/venv";
        var venvPython = VenvPython("/venv");
        var validator = new FakeInterpreterValidator().Valid(venvPython, version: "3.12.4");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("3.12", context);

        Assert.IsFalse(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.Variables.TryGet<string>(PythonMagicCommand.InterpreterSelectionStoreKey, out var stored));
        Assert.AreEqual(venvPython, stored);
    }

    [TestMethod]
    public async Task Select_RejectedInterpreter_SurfacesReason()
    {
        var executable = NewTempExecutable();
        var validator = new FakeInterpreterValidator()
            .Rejected(executable, "Found Python 3.7.1 at " + executable + ", but Python 3.8 or newer is required.");
        var command = MakeCommand(validator);
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync(executable, context);

        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        StringAssert.Contains(context.WrittenOutputs[0].Content, "3.7.1");
        Assert.IsFalse(context.Variables.TryGet<string>(PythonMagicCommand.InterpreterSelectionStoreKey, out _));
    }

    [TestMethod]
    public async Task Select_UnknownName_ReportsNotFound()
    {
        var command = MakeCommand(new FakeInterpreterValidator());
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("definitely-not-python", context);

        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        StringAssert.Contains(context.WrittenOutputs[0].Content, "definitely-not-python");
    }
}
