using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Utilities;

namespace Verso.Cli.Tests.Utilities;

[TestClass]
public class ExitCodesTests
{
    [TestMethod]
    public void Success_IsZero() => Assert.AreEqual(0, ExitCodes.Success);

    [TestMethod]
    public void CellFailure_IsOne() => Assert.AreEqual(1, ExitCodes.CellFailure);

    [TestMethod]
    public void Timeout_IsTwo() => Assert.AreEqual(2, ExitCodes.Timeout);

    [TestMethod]
    public void FileNotFound_IsThree() => Assert.AreEqual(3, ExitCodes.FileNotFound);

    [TestMethod]
    public void SerializationError_IsFour() => Assert.AreEqual(4, ExitCodes.SerializationError);

    [TestMethod]
    public void MissingParameters_IsFive() => Assert.AreEqual(5, ExitCodes.MissingParameters);

    [TestMethod]
    public void AllCodesAreUnique()
    {
        var codes = new[]
        {
            ExitCodes.Success,
            ExitCodes.CellFailure,
            ExitCodes.Timeout,
            ExitCodes.FileNotFound,
            ExitCodes.SerializationError,
            ExitCodes.MissingParameters
        };

        Assert.AreEqual(codes.Length, codes.Distinct().Count(), "Exit codes must be unique.");
    }
}
