using FSharp.Compiler.EditorServices;
using FSharp.Compiler.Text;
using Verso.Abstractions;
using Verso.FSharp.Helpers;

namespace Verso.FSharp.Tests.Helpers;

[TestClass]
public class SemanticTokenMapperTests
{
    private const string File = "/verso/notebook.fsx";

    // A classification of a virtual-document range: 1-based lines, 0-based columns.
    private static SemanticClassificationItem Item(
        int startLine, int startColumn, int endLine, int endColumn, SemanticClassificationType type) =>
        new(Tuple.Create(
            RangeModule.mkRange(File, PositionModule.mkPos(startLine, startColumn), PositionModule.mkPos(endLine, endColumn)),
            type));

    [TestMethod]
    public void MapClassifications_ShiftsByThePrefix_AndDropsThePrefix()
    {
        var items = new[]
        {
            Item(1, 4, 1, 5, SemanticClassificationType.Value),     // in the prefix
            Item(4, 4, 4, 5, SemanticClassificationType.Function),  // cell line 1
        };

        var tokens = SemanticTokenMapper.MapClassifications(items, prefixLineCount: 2, "let a = 1\nlet f x = x");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(new SemanticToken(1, 4, 1, SemanticTokenTypes.Function), tokens[0]);
    }

    [TestMethod]
    public void MapClassifications_DottedName_KeepsTheLastIdentifier()
    {
        var code = "Geometry.area 2.0";
        var items = new[]
        {
            Item(1, 0, 1, 13, SemanticClassificationType.Function),
            Item(1, 0, 1, 8, SemanticClassificationType.Module),
        };

        var tokens = SemanticTokenMapper.MapClassifications(items, 0, code);

        Assert.AreEqual(2, tokens.Count);
        Assert.AreEqual(new SemanticToken(0, 0, 8, SemanticTokenTypes.Namespace), tokens[0]);
        Assert.AreEqual(new SemanticToken(0, 9, 4, SemanticTokenTypes.Function), tokens[1]);
    }

    [TestMethod]
    public void MapClassifications_QuotedIdentifierWithADot_IsTakenWhole()
    {
        var code = "let ``a.b`` = 1";
        var items = new[] { Item(1, 4, 1, 11, SemanticClassificationType.Value) };

        var tokens = SemanticTokenMapper.MapClassifications(items, 0, code);

        Assert.AreEqual(new SemanticToken(0, 4, 7, SemanticTokenTypes.Variable), tokens.Single());
    }

    [TestMethod]
    public void MapClassifications_DropsMultiLineAndUnmappedAndOverlapping()
    {
        var code = "printfn \"%d\" x\nlet y = x";
        var items = new[]
        {
            Item(1, 0, 2, 3, SemanticClassificationType.Value),       // spans lines
            Item(1, 9, 1, 11, SemanticClassificationType.Printf),     // inside a string
            Item(2, 4, 2, 9, SemanticClassificationType.Value),       // "y = x"
            Item(2, 8, 2, 9, SemanticClassificationType.Value),       // overlaps the one before
        };

        var tokens = SemanticTokenMapper.MapClassifications(items, 0, code);

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(new SemanticToken(1, 4, 5, SemanticTokenTypes.Variable), tokens[0]);
    }

    [TestMethod]
    public void MapClassifications_SameSpanTwice_KeepsTheLast()
    {
        var code = "let x = 1";
        var items = new[]
        {
            Item(1, 4, 1, 5, SemanticClassificationType.Value),
            Item(1, 4, 1, 5, SemanticClassificationType.MutableVar),
        };

        var token = SemanticTokenMapper.MapClassifications(items, 0, code).Single();

        Assert.AreEqual(SemanticTokenTypes.Variable, token.Type);
        CollectionAssert.AreEqual(new[] { SemanticTokenModifiers.Mutable }, token.Modifiers!.ToArray());
    }

    [TestMethod]
    public void Map_EveryClassification_HasAStandardTypeOrIsDropped()
    {
        var standard = typeof(SemanticTokenTypes).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        foreach (SemanticClassificationType type in typeof(SemanticClassificationType)
                     .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                     .Select(f => f.GetValue(null)!))
        {
            var mapped = SemanticTokenMapper.Map(type);
            if (mapped is null)
            {
                Assert.IsTrue(type is SemanticClassificationType.Printf or SemanticClassificationType.Plaintext,
                    $"{type} is dropped.");
                continue;
            }
            Assert.IsTrue(standard.Contains(mapped.Value.Type), $"{type} maps to '{mapped.Value.Type}'.");
        }
    }
}
