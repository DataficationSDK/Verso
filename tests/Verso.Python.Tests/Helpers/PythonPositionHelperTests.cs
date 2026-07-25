using Verso.Python.Helpers;

namespace Verso.Python.Tests.Helpers;

[TestClass]
public sealed class PythonPositionHelperTests
{
    // ---- OffsetToLineColumn ----

    [TestMethod]
    public void OffsetToLineColumn_Zero_Returns0_0()
    {
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn("hello", 0);
        Assert.AreEqual(0, line);
        Assert.AreEqual(0, col);
    }

    [TestMethod]
    public void OffsetToLineColumn_SingleLineEnd_ReturnsCorrectColumn()
    {
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn("hello", 5);
        Assert.AreEqual(0, line);
        Assert.AreEqual(5, col);
    }

    [TestMethod]
    public void OffsetToLineColumn_MultiLine_IncrementsLineAfterNewline()
    {
        var text = "abc\ndef";
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn(text, 5); // 'd' on line 1
        Assert.AreEqual(1, line);
        Assert.AreEqual(1, col);
    }

    [TestMethod]
    public void OffsetToLineColumn_ClampedToTextLength()
    {
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn("hi", 100);
        Assert.AreEqual(0, line);
        Assert.AreEqual(2, col);
    }

    [TestMethod]
    public void OffsetToLineColumn_EmptyString_Returns0_0()
    {
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn("", 0);
        Assert.AreEqual(0, line);
        Assert.AreEqual(0, col);
    }

    [TestMethod]
    public void OffsetToLineColumn_AtNewlineBoundary_ResetsColumn()
    {
        var text = "abc\ndef";
        var (line, col) = PythonPositionHelpers.OffsetToLineColumn(text, 4); // first char after \n
        Assert.AreEqual(1, line);
        Assert.AreEqual(0, col);
    }

    // ---- ComputeIdentifierRange ----

    [TestMethod]
    public void ComputeIdentifierRange_CursorInMiddle_ReturnsCorrectRange()
    {
        var code = "hello_world";
        var result = PythonPositionHelpers.ComputeIdentifierRange(code, 5, 0);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Value.StartLine);
        Assert.AreEqual(0, result.Value.StartColumn);
        Assert.AreEqual(0, result.Value.EndLine);
        Assert.AreEqual(11, result.Value.EndColumn);
    }

    [TestMethod]
    public void ComputeIdentifierRange_CursorOnWhitespace_ReturnsNull()
    {
        var code = "   ";
        var result = PythonPositionHelpers.ComputeIdentifierRange(code, 1, 0);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ComputeIdentifierRange_EmptyCode_ReturnsNull()
    {
        var result = PythonPositionHelpers.ComputeIdentifierRange("", 0, 0);
        Assert.IsNull(result);
    }

    // Cross-cell context is assembled inside the Python process now, next to the analysis that
    // consumes it, and is covered by the host script's own test suite.
}
