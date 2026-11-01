using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class SemanticTokensTests
{
    private FSharpKernel _kernel = null!;
    private StubExecutionContext _context = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    // The token whose text in code is name, the first time it occurs on the given line.
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
    public async Task SemanticTokens_ModuleTypeFunctionValueAndUnionCase_AreDistinct()
    {
        var code = string.Join('\n',
            "module Geometry =",
            "    let area r = System.Math.PI * r * r",
            "type Shape = Circle of float",
            "let shape = Circle (Geometry.area 2.0)");

        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.AreEqual(SemanticTokenTypes.Namespace, TokenFor(tokens, code, 3, "Geometry").Type);
        Assert.AreEqual(SemanticTokenTypes.Function, TokenFor(tokens, code, 3, "area").Type);
        Assert.AreEqual(SemanticTokenTypes.EnumMember, TokenFor(tokens, code, 3, "Circle").Type);
        Assert.AreEqual(SemanticTokenTypes.Variable, TokenFor(tokens, code, 3, "shape").Type);
        Assert.AreEqual(SemanticTokenTypes.Type, TokenFor(tokens, code, 2, "Shape").Type);
        Assert.AreEqual(SemanticTokenTypes.Struct, TokenFor(tokens, code, 2, "float").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_AfterExecution_ArePositionedInTheCell()
    {
        // The kernel checks a cell after every cell executed before it; a token left at the
        // position it has in that document would land several lines below the cell.
        await _kernel.ExecuteAsync("type Shape = Circle of float\nlet unit = 1.0", _context);

        var code = "let shape = Circle unit";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.IsTrue(tokens.All(t => t.Line == 0), "Every token is on the cell's only line.");
        Assert.AreEqual(SemanticTokenTypes.EnumMember, TokenFor(tokens, code, 0, "Circle").Type);
        Assert.AreEqual(SemanticTokenTypes.Variable, TokenFor(tokens, code, 0, "unit").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_DottedName_ClassifiesEachPart()
    {
        var code = "let xs = List.map string [1; 2]";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.AreEqual(SemanticTokenTypes.Namespace, TokenFor(tokens, code, 0, "List").Type);
        Assert.AreEqual(SemanticTokenTypes.Function, TokenFor(tokens, code, 0, "map").Type);
    }

    [TestMethod]
    public async Task SemanticTokens_MutableValue_CarriesModifier()
    {
        var code = "let mutable count = 0\ncount <- count + 1";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        var token = TokenFor(tokens, code, 1, "count");
        Assert.AreEqual(SemanticTokenTypes.Variable, token.Type);
        CollectionAssert.Contains(token.Modifiers!.ToList(), SemanticTokenModifiers.Mutable);
    }

    [TestMethod]
    public async Task SemanticTokens_AreOrderedAndDoNotOverlap()
    {
        var code = "open System.Text\nlet sb = StringBuilder()\nlet n = sb.Append(\"a\").Length\nprintfn \"%d\" n";
        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.IsTrue(tokens.Count > 0);
        for (var i = 1; i < tokens.Count; i++)
        {
            var (a, b) = (tokens[i - 1], tokens[i]);
            Assert.IsTrue(a.Line < b.Line || (a.Line == b.Line && a.StartColumn + a.Length <= b.StartColumn),
                $"Token {i} at {b.Line}:{b.StartColumn} overlaps or precedes {a.Line}:{a.StartColumn}+{a.Length}.");
        }
    }

    [TestMethod]
    public async Task SemanticTokens_EmptyCode_ReturnsEmpty()
    {
        var tokens = await _kernel.GetSemanticTokensAsync("   ");

        Assert.IsNotNull(tokens);
        Assert.AreEqual(0, tokens.Count);
    }

    [TestMethod]
    public async Task SemanticTokens_BeforeInitialize_ReturnsNull()
    {
        await using var kernel = new FSharpKernel();

        Assert.IsNull(await kernel.GetSemanticTokensAsync("let x = 1"));
    }
}
