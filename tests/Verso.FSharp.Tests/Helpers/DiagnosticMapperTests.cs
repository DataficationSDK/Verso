using FSharp.Compiler.Diagnostics;
using Verso.Abstractions;
using Verso.FSharp.Helpers;

namespace Verso.FSharp.Tests.Helpers;

[TestClass]
public class DiagnosticMapperTests
{
    [TestMethod]
    public void MapSeverity_Error_ReturnsError()
    {
        var result = DiagnosticMapper.MapSeverity(FSharpDiagnosticSeverity.Error);
        Assert.AreEqual(DiagnosticSeverity.Error, result);
    }

    [TestMethod]
    public void MapSeverity_Warning_ReturnsWarning()
    {
        var result = DiagnosticMapper.MapSeverity(FSharpDiagnosticSeverity.Warning);
        Assert.AreEqual(DiagnosticSeverity.Warning, result);
    }

    [TestMethod]
    public void MapSeverity_Info_ReturnsInfo()
    {
        var result = DiagnosticMapper.MapSeverity(FSharpDiagnosticSeverity.Info);
        Assert.AreEqual(DiagnosticSeverity.Info, result);
    }

    [TestMethod]
    public void MapSeverity_Hidden_ReturnsNull()
    {
        var result = DiagnosticMapper.MapSeverity(FSharpDiagnosticSeverity.Hidden);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void FormatCode_SingleDigit_PadsToFourDigits()
    {
        Assert.AreEqual("FS0001", DiagnosticMapper.FormatCode(1));
    }

    [TestMethod]
    public void FormatCode_FourDigit_FormatsCorrectly()
    {
        Assert.AreEqual("FS0039", DiagnosticMapper.FormatCode(39));
    }

    [TestMethod]
    public void FormatCode_LargeNumber_FormatsCorrectly()
    {
        Assert.AreEqual("FS3118", DiagnosticMapper.FormatCode(3118));
    }
}
