using Verso.Sample.Dice.Models;

namespace Verso.Sample.Dice.Tests;

[TestClass]
public sealed class DiceNotationTests
{
    [TestMethod]
    public void Parse_SimpleDice_ReturnsCorrectValues()
    {
        var notation = DiceNotation.TryParse("2d6");
        Assert.IsNotNull(notation);
        Assert.AreEqual(2, notation.Count);
        Assert.AreEqual(6, notation.Sides);
        Assert.AreEqual(0, notation.Modifier);
    }

    [TestMethod]
    public void Parse_WithPositiveModifier_ReturnsCorrectValues()
    {
        var notation = DiceNotation.TryParse("1d20+5");
        Assert.IsNotNull(notation);
        Assert.AreEqual(1, notation.Count);
        Assert.AreEqual(20, notation.Sides);
        Assert.AreEqual(5, notation.Modifier);
    }

    [TestMethod]
    public void Parse_WithNegativeModifier_ReturnsCorrectValues()
    {
        var notation = DiceNotation.TryParse("3d8-2");
        Assert.IsNotNull(notation);
        Assert.AreEqual(3, notation.Count);
        Assert.AreEqual(8, notation.Sides);
        Assert.AreEqual(-2, notation.Modifier);
    }

    [TestMethod]
    public void Parse_InvalidInput_ReturnsNull()
    {
        Assert.IsNull(DiceNotation.TryParse("abc"));
        Assert.IsNull(DiceNotation.TryParse(""));
        Assert.IsNull(DiceNotation.TryParse(null!));
        Assert.IsNull(DiceNotation.TryParse("d6"));
        Assert.IsNull(DiceNotation.TryParse("2d"));
    }

    [TestMethod]
    public void Parse_ExcessiveCount_ReturnsNull()
    {
        Assert.IsNull(DiceNotation.TryParse("101d6"));
    }

    [TestMethod]
    public void Parse_ExcessiveSides_ReturnsNull()
    {
        Assert.IsNull(DiceNotation.TryParse("1d1001"));
    }

    [TestMethod]
    public void ToString_WithoutModifier_ReturnsNotation()
    {
        var notation = DiceNotation.TryParse("2d6");
        Assert.AreEqual("2d6", notation!.ToString());
    }

    [TestMethod]
    public void ToString_WithPositiveModifier_IncludesPlus()
    {
        var notation = DiceNotation.TryParse("1d20+5");
        Assert.AreEqual("1d20+5", notation!.ToString());
    }

    [TestMethod]
    public void ToString_WithNegativeModifier_IncludesMinus()
    {
        var notation = DiceNotation.TryParse("3d8-2");
        Assert.AreEqual("3d8-2", notation!.ToString());
    }

    [TestMethod]
    public void Parse_CaseInsensitive_Works()
    {
        var notation = DiceNotation.TryParse("2D6");
        Assert.IsNotNull(notation);
        Assert.AreEqual(2, notation.Count);
        Assert.AreEqual(6, notation.Sides);
    }
}
