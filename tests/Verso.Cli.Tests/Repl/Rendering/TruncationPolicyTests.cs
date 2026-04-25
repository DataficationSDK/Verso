using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Repl.Settings;

namespace Verso.Cli.Tests.Repl.Rendering;

[TestClass]
public class TruncationPolicyTests
{
    [TestMethod]
    public void ClipLines_UnderCap_ReturnsInputUnchanged()
    {
        var policy = new TruncationPolicy(MaxRows: 20, MaxLines: 200, MaxWidth: 80);
        Assert.AreEqual("a\nb\nc", policy.ClipLines("a\nb\nc"));
    }

    [TestMethod]
    public void ClipLines_OverCap_AppendsFooter()
    {
        var policy = new TruncationPolicy(MaxRows: 20, MaxLines: 3, MaxWidth: 80);
        var result = policy.ClipLines("a\nb\nc\nd\ne");
        StringAssert.StartsWith(result, "a\nb\nc");
        StringAssert.Contains(result, "… 2 more lines");
    }

    [TestMethod]
    public void ClipLines_EmptyInput_ReturnsEmpty()
    {
        var policy = new TruncationPolicy(MaxRows: 20, MaxLines: 10, MaxWidth: 80);
        Assert.AreEqual("", policy.ClipLines(""));
    }

    [TestMethod]
    public void FromSettings_MirrorsPreviewRowsAndLines()
    {
        var settings = new ReplSettings();
        settings.Preview.Rows = 7;
        settings.Preview.Lines = 13;

        var policy = TruncationPolicy.FromSettings(settings);
        Assert.AreEqual(7, policy.MaxRows);
        Assert.AreEqual(13, policy.MaxLines);
    }
}
