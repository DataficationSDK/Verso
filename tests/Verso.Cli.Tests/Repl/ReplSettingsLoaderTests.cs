using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Settings;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class ReplSettingsLoaderTests
{
    [TestMethod]
    public void ApplyJsonOverride_PreviewRowsAndLines_AreApplied()
    {
        var settings = new ReplSettings();
        ReplSettingsLoader.ApplyJsonOverride(settings,
            "{ \"preview\": { \"rows\": 9, \"lines\": 77, \"elapsedThresholdMs\": 500 } }");

        Assert.AreEqual(9, settings.Preview.Rows);
        Assert.AreEqual(77, settings.Preview.Lines);
        Assert.AreEqual(500, settings.Preview.ElapsedThresholdMs);
    }

    [TestMethod]
    public void ApplyJsonOverride_ConfirmOnExit_CanBeDisabled()
    {
        var settings = new ReplSettings();
        Assert.IsTrue(settings.ConfirmOnExit, "Default posture is to confirm unsaved-cell exits.");

        ReplSettingsLoader.ApplyJsonOverride(settings, "{ \"confirmOnExit\": false }");
        Assert.IsFalse(settings.ConfirmOnExit);
    }

    [TestMethod]
    public void ApplyJsonOverride_MalformedJson_LeavesDefaults()
    {
        var settings = new ReplSettings();
        var before = settings.Preview.Rows;
        ReplSettingsLoader.ApplyJsonOverride(settings, "{ this is not valid JSON");
        // The malformed input must not mutate or throw.
        Assert.AreEqual(before, settings.Preview.Rows);
    }

    [TestMethod]
    public void ApplyJsonOverride_CaseInsensitiveKeys()
    {
        var settings = new ReplSettings();
        ReplSettingsLoader.ApplyJsonOverride(settings,
            "{ \"Preview\": { \"Rows\": 5 }, \"ConfirmOnExit\": false }");
        Assert.AreEqual(5, settings.Preview.Rows);
        Assert.IsFalse(settings.ConfirmOnExit);
    }

    [TestMethod]
    public void GetUserConfigPath_EndsWithReplJson()
    {
        var path = ReplSettingsLoader.GetUserConfigPath();
        StringAssert.EndsWith(path, "repl.json");
        StringAssert.Contains(path, "verso");
    }
}
