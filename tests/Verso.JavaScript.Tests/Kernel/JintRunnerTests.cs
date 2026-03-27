using Verso.JavaScript.Kernel;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class JintRunnerTests
{
    private JintRunner _runner = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _runner = new JintRunner(new JavaScriptKernelOptions());
        await _runner.InitializeAsync(CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _runner.DisposeAsync();
    }

    [TestMethod]
    public void IsAlive_AfterInit_ReturnsTrue()
    {
        Assert.IsTrue(_runner.IsAlive);
    }

    [TestMethod]
    public async Task IsAlive_AfterDispose_ReturnsFalse()
    {
        await _runner.DisposeAsync();
        Assert.IsFalse(_runner.IsAlive);
    }

    [TestMethod]
    public async Task Execute_ConsoleLog_CapturesStdout()
    {
        var result = await _runner.ExecuteAsync("console.log('hello')", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.Stdout);
        Assert.IsTrue(result.Stdout.Contains("hello"));
    }

    [TestMethod]
    public async Task Execute_ConsoleError_CapturesStderr()
    {
        var result = await _runner.ExecuteAsync("console.error('oops')", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.Stderr);
        Assert.IsTrue(result.Stderr.Contains("oops"));
    }

    [TestMethod]
    public async Task Execute_ThrowError_SetsErrorFields()
    {
        var result = await _runner.ExecuteAsync("throw new Error('boom')", CancellationToken.None);
        Assert.IsTrue(result.HasError);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage.Contains("boom"));
    }

    [TestMethod]
    public async Task Execute_LastExpression_ReturnsJson()
    {
        var result = await _runner.ExecuteAsync("1 + 2", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.LastExpressionJson);
        Assert.IsTrue(result.LastExpressionJson.Contains("3"));
    }

    [TestMethod]
    public async Task Execute_VarDeclaration_NoLastExpression()
    {
        var result = await _runner.ExecuteAsync("var x = 5;", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNull(result.LastExpressionJson);
    }

    [TestMethod]
    public async Task Execute_UserGlobals_TracksNewVariables()
    {
        var result = await _runner.ExecuteAsync("var myVar = 42", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.UserGlobals);
        Assert.IsTrue(result.UserGlobals.Contains("myVar"),
            $"Expected 'myVar' in user globals, got: [{string.Join(", ", result.UserGlobals)}]");
    }

    [TestMethod]
    public async Task Execute_VersoPrefix_ExcludedFromGlobals()
    {
        var result = await _runner.ExecuteAsync("var _versoCached = 1", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.UserGlobals);
        Assert.IsFalse(result.UserGlobals.Any(g => g.StartsWith("_verso")),
            "Variables starting with _verso should be excluded from user globals");
    }

    [TestMethod]
    public async Task Execute_ConstRedeclaration_Succeeds()
    {
        var r1 = await _runner.ExecuteAsync("const x = 1", CancellationToken.None);
        Assert.IsFalse(r1.HasError);

        var r2 = await _runner.ExecuteAsync("const x = 2", CancellationToken.None);
        Assert.IsFalse(r2.HasError, "Const re-declaration across cells should succeed");
    }

    [TestMethod]
    public async Task Execute_LetRedeclaration_Succeeds()
    {
        var r1 = await _runner.ExecuteAsync("let y = 1", CancellationToken.None);
        Assert.IsFalse(r1.HasError);

        var r2 = await _runner.ExecuteAsync("let y = 2", CancellationToken.None);
        Assert.IsFalse(r2.HasError, "Let re-declaration across cells should succeed");
    }

    [TestMethod]
    public async Task Execute_PromotedConst_AccessibleAcrossCells()
    {
        await _runner.ExecuteAsync("const greeting = 'hello'", CancellationToken.None);
        var result = await _runner.ExecuteAsync("greeting", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.LastExpressionJson);
        Assert.IsTrue(result.LastExpressionJson.Contains("hello"));
    }

    [TestMethod]
    public async Task GetVariables_ReturnsJsonValues()
    {
        await _runner.ExecuteAsync("var x = 42", CancellationToken.None);
        var vars = await _runner.GetVariablesAsync(new[] { "x" }, CancellationToken.None);
        Assert.IsTrue(vars.ContainsKey("x"));
        Assert.AreEqual("42", vars["x"]);
    }

    [TestMethod]
    public async Task GetVariables_UndefinedVar_ReturnsNull()
    {
        var vars = await _runner.GetVariablesAsync(new[] { "notDefined" }, CancellationToken.None);
        Assert.IsTrue(vars.ContainsKey("notDefined"));
        Assert.IsNull(vars["notDefined"]);
    }

    [TestMethod]
    public async Task SetVariables_InjectsIntoScope()
    {
        await _runner.SetVariablesAsync(
            new Dictionary<string, string> { ["injected"] = "42" },
            CancellationToken.None);

        var result = await _runner.ExecuteAsync("injected", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.LastExpressionJson);
        Assert.IsTrue(result.LastExpressionJson.Contains("42"));
    }

    [TestMethod]
    public void ExecuteBeforeInit_ThrowsInvalidOperationException()
    {
        var runner = new JintRunner(new JavaScriptKernelOptions());
        Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await runner.ExecuteAsync("1 + 1", CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task Execute_MultipleConsoleLog_SeparatedByNewlines()
    {
        var result = await _runner.ExecuteAsync(
            "console.log('first')\nconsole.log('second')", CancellationToken.None);
        Assert.IsNotNull(result.Stdout);
        Assert.IsTrue(result.Stdout.Contains("first"));
        Assert.IsTrue(result.Stdout.Contains("second"));
    }

    [TestMethod]
    public async Task Execute_ConsoleInfo_GoesToStdout()
    {
        var result = await _runner.ExecuteAsync("console.info('info message')", CancellationToken.None);
        Assert.IsNotNull(result.Stdout);
        Assert.IsTrue(result.Stdout.Contains("info message"));
    }

    [TestMethod]
    public async Task Execute_ConsoleWarn_GoesToStderr()
    {
        var result = await _runner.ExecuteAsync("console.warn('warning')", CancellationToken.None);
        Assert.IsNotNull(result.Stderr);
        Assert.IsTrue(result.Stderr.Contains("warning"));
    }

    [TestMethod]
    public async Task Execute_FunctionDeclaration_PromotsToGlobal()
    {
        await _runner.ExecuteAsync("function add(a, b) { return a + b; }", CancellationToken.None);
        var result = await _runner.ExecuteAsync("add(3, 4)", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.LastExpressionJson);
        Assert.IsTrue(result.LastExpressionJson.Contains("7"));
    }

    [TestMethod]
    public async Task Execute_ClassDeclaration_PromotesToGlobal()
    {
        await _runner.ExecuteAsync("class Point { constructor(x, y) { this.x = x; this.y = y; } }",
            CancellationToken.None);
        var result = await _runner.ExecuteAsync("new Point(1, 2).x", CancellationToken.None);
        Assert.IsFalse(result.HasError);
        Assert.IsNotNull(result.LastExpressionJson);
        Assert.IsTrue(result.LastExpressionJson.Contains("1"));
    }
}
