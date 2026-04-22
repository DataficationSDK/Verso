using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Prompt;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class HistoryStoreTests
{
    [TestMethod]
    public void Resolve_DisabledFlag_ReturnsNull()
    {
        Assert.IsNull(HistoryStore.Resolve(@override: null, disabled: true));
    }

    [TestMethod]
    public void Resolve_ExplicitPath_IsHonoured()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"verso-history-{Guid.NewGuid():N}");
        try
        {
            var path = HistoryStore.Resolve(temp, disabled: false);
            Assert.AreEqual(Path.GetFullPath(temp), path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [TestMethod]
    public void Resolve_Default_PointsToPlatformStateDirectory()
    {
        var path = HistoryStore.Resolve(@override: null, disabled: false);
        Assert.IsNotNull(path);
        // The trailing filename is fixed; platform details vary.
        Assert.AreEqual("repl-history", Path.GetFileName(path));
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(path)!));
    }
}
