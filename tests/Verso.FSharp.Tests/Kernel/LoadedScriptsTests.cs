using Verso.Abstractions;
using Verso.Contexts;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

/// <summary>
/// FSI compiles a <c>#load</c>ed script as a file of its own and does not open it: later cells
/// reach its names through its module or namespace header, or, without one, through a module
/// named for the file. The document the kernel checks a cell in has to know them the same way.
/// </summary>
[TestClass]
public class LoadedScriptsTests
{
    private FSharpKernel _kernel = null!;
    private StubExecutionContext _context = null!;
    private string _scriptDir = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();
        _scriptDir = Path.Combine(Path.GetTempPath(), $"verso-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scriptDir);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
        Directory.Delete(_scriptDir, recursive: true);
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.StartLine}:{d.StartColumn} {d.Code} {d.Message}"));

    private string WriteScript(string fileName, string source)
    {
        var path = Path.Combine(_scriptDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private async Task LoadAsync(string path)
    {
        var outputs = await _kernel.ExecuteAsync($"#load @\"{path}\"", _context);
        Assert.IsFalse(outputs.Any(o => o.IsError), string.Join("; ", outputs.Select(o => o.Content)));
    }

    [TestMethod]
    public async Task LoadedScriptWithAModuleHeader_IsKnownByItsModuleName()
    {
        await LoadAsync(WriteScript("withheader.fsx", "module Lib\n\nlet triple x = x * 3\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = Lib.triple 2");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadedScriptWithoutAHeader_IsKnownByItsFileName()
    {
        await LoadAsync(WriteScript("noheader.fsx", "let quad x = x * 4\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = Noheader.quad 2");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadedScript_IsNotOpenedForLaterCells()
    {
        await LoadAsync(WriteScript("noheader.fsx", "let quad x = x * 4\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = quad 2");

        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"), Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadedScriptWithAnAutoOpenModule_IsOpenedForLaterCells()
    {
        await LoadAsync(WriteScript("auto.fsx", "[<AutoOpen>]\nmodule Auto\n\nlet seven x = x + 7\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = seven 2");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadedScriptWithANamespaceHeader_IsKnownByItsNamespace()
    {
        await LoadAsync(WriteScript("shapes.fsx",
            "namespace Shapes\n\nmodule Area =\n    let square x = x * x\n\ntype Square = { Side: int }\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let a = Shapes.Area.square 3\nlet s : Shapes.Square = { Side = 1 }");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ScriptLoadedByALoadedScript_IsKnownToo()
    {
        WriteScript("inner.fsx", "let tenfold x = x * 10\n");
        await LoadAsync(WriteScript("outer.fsx", "#load \"inner.fsx\"\n\nlet plusOne x = Inner.tenfold x + 1\n"));

        var diagnostics = await _kernel.GetDiagnosticsAsync("let a = Outer.plusOne 1\nlet b = Inner.tenfold 1");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task RelativeLoad_ResolvesAgainstTheNotebookDirectory()
    {
        WriteScript("scripts/scale.fsx", "let double x = x * 2\n");
        _context.NotebookMetadata = new NotebookMetadataContext(new NotebookModel(), Path.Combine(_scriptDir, "notebook.verso"));

        var outputs = await _kernel.ExecuteAsync("#load \"scripts/scale.fsx\"", _context);
        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = Scale.double 2");

        Assert.IsFalse(outputs.Any(o => o.IsError), string.Join("; ", outputs.Select(o => o.Content)));
        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadThatFailedToCompile_IsNotKnownLater()
    {
        var path = WriteScript("broken.fsx", "let value : int = \"text\"\n");
        var outputs = await _kernel.ExecuteAsync($"#load @\"{path}\"", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = Broken.value");

        Assert.IsTrue(outputs.Any(o => o.IsError), "the load FSI rejects");
        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"), Describe(diagnostics));
    }

    [TestMethod]
    public async Task LoadedScript_IsOfferedInCompletionsAndHover()
    {
        await LoadAsync(WriteScript("withheader.fsx", "module Lib\n\nlet triple x = x * 3\n"));

        var completions = await _kernel.GetCompletionsAsync("let n = Lib.tri", 15);
        var code = "let n = Lib.triple 2";
        var hover = await _kernel.GetHoverInfoAsync(code, code.IndexOf("triple", StringComparison.Ordinal) + 1);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "triple"),
            string.Join(", ", completions.Select(c => c.DisplayText).Take(10)));
        Assert.IsNotNull(hover);
        StringAssert.Contains(hover.Content, "triple");
    }

    [TestMethod]
    public async Task CellRunAfterALoad_StillChecksAgainstIt()
    {
        await _kernel.ExecuteAsync("let before = 1", _context);
        await LoadAsync(WriteScript("withheader.fsx", "module Lib\n\nlet triple x = x * 3\n"));
        await _kernel.ExecuteAsync("let after = Lib.triple before", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n : int = after + before");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }
}
