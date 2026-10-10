using System.Text.Json;
using Verso.Abstractions;
using Verso.JavaScript.Kernel;
using Verso.JavaScript.MagicCommands;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class JavaScriptDiagnosticsTests
{
    private JavaScriptKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task SyntaxError_IsReportedAtItsLineAndColumn()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("const a = 1;\nlet x = ;");

        Assert.AreEqual(1, diagnostics.Count);
        var d = diagnostics[0];
        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual(1, d.StartLine);
        Assert.AreEqual(8, d.StartColumn);
        Assert.AreEqual(1, d.EndLine);
        Assert.AreEqual(9, d.EndColumn);
        Assert.AreEqual("UnexpectedToken", d.Code);
        StringAssert.Contains(d.Message, "Unexpected token");
    }

    [TestMethod]
    public async Task UnexpectedEndOfInput_IsReported()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("foo(\n");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(1, diagnostics[0].StartLine);
        Assert.AreEqual(0, diagnostics[0].StartColumn);
        Assert.AreEqual("UnexpectedEOS", diagnostics[0].Code);
    }

    [DataTestMethod]
    [DataRow("let x = 1;\nconsole.log(x);")]
    [DataRow("const r = await Promise.resolve(1);\nr")]
    [DataRow("if (true) return 1;\nconsole.log('unreached');")]
    [DataRow("const m = await import('node:path');")]
    public async Task ValidCellCode_ReturnsEmpty(string code)
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task StaticImport_IsReported()
    {
        // A cell runs inside a function, where a static import cannot appear.
        var diagnostics = await _kernel.GetDiagnosticsAsync("import fs from 'fs';");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(0, diagnostics[0].StartLine);
        Assert.AreEqual("ImportOutsideModule", diagnostics[0].Code);
    }

    [TestMethod]
    public async Task LeadingMagicLines_AreIgnored()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("#!npm lodash\n#!time\n\nconst a = 1;");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ErrorAfterMagicLines_KeepsItsLineNumber()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("#!npm lodash\r\n#!time\r\n\r\nlet x = ;");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(3, diagnostics[0].StartLine);
        Assert.AreEqual(8, diagnostics[0].StartColumn);
    }

    [TestMethod]
    public async Task WhitespaceOnly_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("  \n\t");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task UninitializedKernel_StillReportsErrors()
    {
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        var diagnostics = await kernel.GetDiagnosticsAsync("let x = ;");
        Assert.AreEqual(1, diagnostics.Count);
    }

    [TestMethod]
    public async Task DisposedKernel_ReturnsEmpty()
    {
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("let x = ;");
        Assert.AreEqual(0, diagnostics.Count);
    }
}

[TestClass]
public class MagicLinesTests
{
    [TestMethod]
    public void NoLeadingDirectives_ReturnsInputUnchanged()
    {
        const string code = "\nconst a = 1;\n#!not-leading";
        Assert.AreSame(code, MagicLines.BlankLeadingDirectives(code));
    }

    [TestMethod]
    public void LeadingDirectives_AreBlankedKeepingLineBreaks()
    {
        var blanked = MagicLines.BlankLeadingDirectives("  #!npm lodash\r\n\n#!time\nlet a = 1;\n#!kept");
        Assert.AreEqual("\r\n\n\nlet a = 1;\n#!kept", blanked);
    }

    [TestMethod]
    public void DirectivesOnly_BlanksEveryLine()
    {
        Assert.AreEqual("\n", MagicLines.BlankLeadingDirectives("#!npm lodash\n#!time"));
    }

    [TestMethod]
    public void BareShebang_EndsTheLeadingRun()
    {
        // "#!" alone names no command, so the pipeline stops there and passes it to the kernel;
        // the blanking must stop at the same line.
        Assert.AreEqual("\n#!\n#!time\nlet a = 1;",
            MagicLines.BlankLeadingDirectives("#!npm lodash\n#!\n#!time\nlet a = 1;"));

        const string bareFirst = "#!  \nlet a = 1;";
        Assert.AreSame(bareFirst, MagicLines.BlankLeadingDirectives(bareFirst));
    }
}

[TestClass]
public class TypeScriptDiagnosticsTests
{
    [TestMethod]
    public void ParseTypeScriptDiagnostics_MapsCategoriesCodesAndPositions()
    {
        using var doc = JsonDocument.Parse(@"
            {
              ""type"": ""diagnosticsResult"",
              ""id"": ""1"",
              ""items"": [
                { ""category"": 1, ""code"": 1109, ""message"": ""Expression expected."", ""startLine"": 2, ""startColumn"": 18, ""endLine"": 2, ""endColumn"": 19 },
                { ""category"": 0, ""code"": 6133, ""message"": ""Unused."", ""startLine"": 0, ""startColumn"": 0, ""endLine"": 0, ""endColumn"": 3 },
                { ""category"": 2, ""code"": 80001, ""message"": ""Suggestion."", ""startLine"": 1, ""startColumn"": 4, ""endLine"": 3, ""endColumn"": 1 },
                { ""category"": 3, ""message"": ""Note."", ""startLine"": 0, ""startColumn"": 1, ""endLine"": 0, ""endColumn"": 2 },
                { ""category"": 1, ""code"": 1, ""message"": ""No position."" }
              ]
            }
            ");

        var diagnostics = NodeProcessRunner.ParseTypeScriptDiagnostics(doc.RootElement);

        Assert.AreEqual(4, diagnostics.Count);
        Assert.AreEqual(new Diagnostic(DiagnosticSeverity.Error, "Expression expected.", 2, 18, 2, 19, "TS1109"), diagnostics[0]);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[1].Severity);
        Assert.AreEqual("TS6133", diagnostics[1].Code);
        Assert.AreEqual(new Diagnostic(DiagnosticSeverity.Info, "Suggestion.", 1, 4, 3, 1, "TS80001"), diagnostics[2]);
        Assert.AreEqual(DiagnosticSeverity.Info, diagnostics[3].Severity);
        Assert.IsNull(diagnostics[3].Code);
    }

    [TestMethod]
    public void ParseTypeScriptDiagnostics_MissingItems_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("{ \"type\": \"diagnosticsResult\", \"id\": \"1\" }");
        Assert.AreEqual(0, NodeProcessRunner.ParseTypeScriptDiagnostics(doc.RootElement).Count);
    }

    [TestMethod]
    public async Task UninitializedKernel_ReturnsEmptyWithoutStartingRunner()
    {
        var runner = new FakeRunner();
        var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner);

        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = ;");

        Assert.AreEqual(0, diagnostics.Count);
        Assert.AreEqual(0, runner.DiagnosticsCalls);
    }

    [TestMethod]
    public async Task InitializedKernel_PassesCodeWithMagicLinesBlanked()
    {
        var runner = new FakeRunner
        {
            Result = new[] { new Diagnostic(DiagnosticSeverity.Error, "Expression expected.", 1, 18, 1, 19, "TS1109") },
        };
        await using var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner);
        await kernel.InitializeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("#!time\nconst x: number = ;");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual("TS1109", diagnostics[0].Code);
        Assert.AreEqual("\nconst x: number = ;", runner.LastCode);
    }

    [TestMethod]
    public async Task ThrowingRunner_ReturnsEmpty()
    {
        var runner = new FakeRunner { Throw = new IOException("bridge gone") };
        await using var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner);
        await kernel.InitializeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = ;");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task SlowRunner_TimesOutToEmpty()
    {
        var runner = new FakeRunner { Hang = true };
        await using var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner)
        {
            DiagnosticsTimeout = TimeSpan.FromMilliseconds(100),
        };
        await kernel.InitializeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = ;");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task DeadRunner_ReturnsEmpty()
    {
        var runner = new FakeRunner();
        await using var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner);
        await kernel.InitializeAsync();
        runner.Alive = false;

        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = ;");
        Assert.AreEqual(0, diagnostics.Count);
        Assert.AreEqual(0, runner.DiagnosticsCalls);
    }

    [TestMethod]
    public async Task DisposedKernel_ReturnsEmpty()
    {
        var runner = new FakeRunner();
        var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () => runner);
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = ;");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task NodeWithInstalledTypeScript_ReportsSyntaxError()
    {
        // Uses only what is already on this machine; nothing is installed.
        var version = NpmManager.GetInstalledPackageVersion("typescript");
        if (JavaScriptEngineManager.NodeExecutablePath is null
            || version is null
            || !TypeScriptKernel.IsSupportedTypeScriptVersion(version))
        {
            Assert.Inconclusive("Needs Node.js and a supported typescript package already installed.");
            return;
        }

        await using var kernel = new TypeScriptKernel();
        await kernel.InitializeAsync();

        var diagnostics = await kernel.GetDiagnosticsAsync("#!time\nconst x: number = ;");

        Assert.AreEqual(1, diagnostics.Count);
        var d = diagnostics[0];
        Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
        Assert.AreEqual("TS1109", d.Code);
        Assert.AreEqual(1, d.StartLine);
        Assert.AreEqual(18, d.StartColumn);
        Assert.AreEqual(1, d.EndLine);
        Assert.IsTrue(d.EndColumn > d.StartColumn);

        var valid = await kernel.GetDiagnosticsAsync("const y: number = 1;");
        Assert.AreEqual(0, valid.Count);
    }

    [TestMethod]
    public async Task CrashedRunner_IsReplacedThroughTheRunnerFactory()
    {
        var runners = new List<FakeRunner>();
        await using var kernel = new TypeScriptKernel(new JavaScriptKernelOptions(), () =>
        {
            var runner = new FakeRunner();
            runners.Add(runner);
            return runner;
        });
        await kernel.InitializeAsync();
        var context = new Verso.Testing.Stubs.StubExecutionContext();
        await kernel.ExecuteAsync("const a: number = 1;", context);

        runners[0].Alive = false;
        await kernel.ExecuteAsync("const b: number = 2;", context);

        Assert.AreEqual(2, runners.Count, "A crash should start exactly one new runner, from the factory.");
        Assert.IsTrue(runners[1].IsAlive);
        Assert.IsTrue(runners[1].TranspileCalls > 0, "The cell should run on the new runner.");
    }

    private sealed class FakeRunner : IJavaScriptRunner
    {
        public bool Alive { get; set; }
        public bool Hang { get; init; }
        public Exception? Throw { get; init; }
        public IReadOnlyList<Diagnostic> Result { get; init; } = Array.Empty<Diagnostic>();
        public int DiagnosticsCalls { get; private set; }
        public string? LastCode { get; private set; }

        public bool IsAlive => Alive;
        public int TranspileCalls { get; private set; }

        public Task<TranspileResult> TranspileAsync(string code, CancellationToken ct)
        {
            TranspileCalls++;
            return Task.FromResult(new TranspileResult("void 0;", null));
        }

        public Task InitializeAsync(CancellationToken ct)
        {
            Alive = true;
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<Diagnostic>> GetTypeScriptDiagnosticsAsync(string code, CancellationToken ct)
        {
            DiagnosticsCalls++;
            LastCode = code;
            if (Throw is not null) throw Throw;
            if (Hang) await Task.Delay(Timeout.Infinite, ct);
            return Result;
        }

        public Task<JavaScriptRunResult> ExecuteAsync(string code, CancellationToken ct) =>
            Task.FromResult(new JavaScriptRunResult(null, null, null, null, false, null, null));

        public Task<IReadOnlyDictionary<string, string?>> GetVariablesAsync(IReadOnlyList<string> names, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());

        public Task SetVariablesAsync(IReadOnlyDictionary<string, string> variables, CancellationToken ct) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Alive = false;
            return ValueTask.CompletedTask;
        }
    }
}
