using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelSemanticTokensTests
{
    private CSharpKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new CSharpKernel();
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    private static SemanticToken TokenFor(IReadOnlyList<SemanticToken> tokens, string code, int line, string name)
    {
        var column = code.Split('\n')[line].IndexOf(name, StringComparison.Ordinal);
        Assert.IsTrue(column >= 0, $"'{name}' is not on line {line}.");
        var token = tokens.FirstOrDefault(t => t.Line == line && t.StartColumn == column);
        Assert.IsNotNull(token, $"No token for '{name}' at {line}:{column}. Tokens: " +
            string.Join(", ", tokens.Select(t => $"{t.Line}:{t.StartColumn}+{t.Length} {t.Type}")));
        Assert.AreEqual(name.Length, token.Length, $"Length of the token for '{name}'.");
        return token;
    }

    [TestMethod]
    public async Task SemanticTokens_ClassifiesNamespaceTypeMethodLocalAndEnumMember()
    {
        var code = string.Join('\n',
            "var sb = new System.Text.StringBuilder();",
            "sb.Append(DayOfWeek.Monday);",
            "int Twice(int value) => value * 2;");

        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.AreEqual(SemanticTokenTypes.Variable, TokenFor(tokens, code, 0, "sb").Type);
        Assert.AreEqual(SemanticTokenTypes.Namespace, TokenFor(tokens, code, 0, "System").Type);
        Assert.AreEqual(SemanticTokenTypes.Class, TokenFor(tokens, code, 0, "StringBuilder").Type);
        Assert.AreEqual(SemanticTokenTypes.Method, TokenFor(tokens, code, 1, "Append").Type);
        Assert.AreEqual(SemanticTokenTypes.Enum, TokenFor(tokens, code, 1, "DayOfWeek").Type);
        Assert.AreEqual(SemanticTokenTypes.EnumMember, TokenFor(tokens, code, 1, "Monday").Type);
        Assert.AreEqual(SemanticTokenTypes.Parameter, TokenFor(tokens, code, 2, "value").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_StaticMember_CarriesModifier()
    {
        var code = "var now = DateTime.Now;";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        var token = TokenFor(tokens, code, 0, "Now");
        Assert.AreEqual(SemanticTokenTypes.Property, token.Type);
        CollectionAssert.Contains(token.Modifiers!.ToList(), SemanticTokenModifiers.Static);
    }

    [TestMethod]
    public async Task SemanticTokens_NameFromAnExecutedCell_IsClassified()
    {
        var context = new Verso.Contexts.ExecutionContext(
            Guid.NewGuid(), 1, new VariableStore(), CancellationToken.None,
            new StubThemeContext(), LayoutCapabilities.None,
            new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>()),
            new NotebookMetadataContext(new NotebookModel()),
            new Verso.Stubs.StubNotebookOperations(),
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask);
        await _kernel.ExecuteAsync("record Point(double X, double Y);", context);

        var code = "var p = new Point(1, 2);\nvar x = p.X;";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.AreEqual(SemanticTokenTypes.Class, TokenFor(tokens, code, 0, "Point").Type);
        Assert.AreEqual(SemanticTokenTypes.Property, TokenFor(tokens, code, 1, "X").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_NuGetDirective_DoesNotShiftLaterTokens()
    {
        var code = "#r \"nuget: Humanizer, 2.14.1\"\nvar n = 1;";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.AreEqual(SemanticTokenTypes.Variable, TokenFor(tokens, code, 1, "n").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_KeywordsAndLiterals_AreLeftToTheEditor()
    {
        var tokens = await _kernel.GetSemanticTokensAsync("var s = \"text\" + 1;");

        Assert.IsNotNull(tokens);
        Assert.AreEqual(1, tokens.Count, "only the local 's'");
        Assert.AreEqual(SemanticTokenTypes.Variable, tokens[0].Type);
    }
}
