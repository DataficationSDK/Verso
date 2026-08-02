using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Utilities;

namespace Verso.Cli.Tests.Utilities;

/// <summary>
/// Measuring text in terminal columns rather than characters, which is what keeps a table lined
/// up once its headings are Japanese or Chinese.
/// </summary>
[TestClass]
public class DisplayWidthTests
{
    [TestMethod]
    public void Measure_CountsLatinTextOneColumnPerCharacter()
    {
        Assert.AreEqual(0, DisplayWidth.Measure(""));
        Assert.AreEqual(0, DisplayWidth.Measure(null));
        Assert.AreEqual(7, DisplayWidth.Measure("Summary"));
    }

    [TestMethod]
    public void Measure_CountsEastAsianCharactersTwoColumnsEach()
    {
        // The two headings that exposed this: "Summary" and "Language" pad correctly by
        // character count, their Japanese counterparts do not.
        Assert.AreEqual(4, DisplayWidth.Measure("概要"));
        Assert.AreEqual(4, DisplayWidth.Measure("言語"));
        Assert.AreEqual(6, DisplayWidth.Measure("表示名"));
        Assert.AreEqual(8, DisplayWidth.Measure("简体中文"));
    }

    [TestMethod]
    public void Measure_HandlesMixedText()
    {
        // What a cell rule actually holds: a Japanese label, a number, and a Latin kernel id.
        // Two wide characters and thirteen narrow ones, which string.Length would call 15.
        Assert.AreEqual(17, DisplayWidth.Measure("セル 18 (csharp) "));
    }

    [TestMethod]
    public void Measure_CountsASurrogatePairOnce()
    {
        // U+20BB7 is one character written as two UTF-16 units, so Length says 2 and the
        // terminal draws 2 columns. Getting there by the wrong route would say 4.
        Assert.AreEqual(2, DisplayWidth.Measure("\U00020BB7"));
    }

    [TestMethod]
    public void PadRight_FillsToTheColumnCountRatherThanTheCharacterCount()
    {
        Assert.AreEqual("概要      ", DisplayWidth.PadRight("概要", 10));
        Assert.AreEqual("Summary   ", DisplayWidth.PadRight("Summary", 10));
    }

    [TestMethod]
    public void PadRight_LeavesTextThatIsAlreadyWideEnoughAlone()
    {
        Assert.AreEqual("概要", DisplayWidth.PadRight("概要", 4));
        Assert.AreEqual("概要", DisplayWidth.PadRight("概要", 2));
    }

    [TestMethod]
    public void PaddedColumnsLineUpAcrossLanguages()
    {
        // The property the call sites depend on: two rows padded to the same width occupy the
        // same number of columns, whatever alphabet they are in.
        var japanese = DisplayWidth.PadRight("言語", 12);
        var latin = DisplayWidth.PadRight("csharp", 12);

        Assert.AreEqual(DisplayWidth.Measure(japanese), DisplayWidth.Measure(latin));
    }
}
