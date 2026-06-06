using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionTrustStoreTests
{
    private string _path = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"verso-trust-test-{Guid.NewGuid():N}", "trust.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [TestMethod]
    public void MissingFile_LoadsEmpty_NothingApproved()
    {
        var store = ExtensionTrustStore.Load(_path);
        Assert.IsFalse(store.IsApproved("Any.Package", "1.0.0"));
    }

    [TestMethod]
    public void Approve_SpecificVersion_OnlyMatchesSameVersion()
    {
        var store = ExtensionTrustStore.Load(_path);
        store.Approve("Pkg", "1.0.0");

        Assert.IsTrue(store.IsApproved("Pkg", "1.0.0"));
        Assert.IsFalse(store.IsApproved("Pkg", "2.0.0"), "a different version must re-prompt");
    }

    [TestMethod]
    public void Approve_NullVersion_MatchesAnyRequest()
    {
        var store = ExtensionTrustStore.Load(_path);
        store.Approve("Pkg", null);

        Assert.IsTrue(store.IsApproved("Pkg", null));
        Assert.IsTrue(store.IsApproved("Pkg", "3.1.4"), "latest approval covers any concrete version");
    }

    [TestMethod]
    public void Approve_IsCaseInsensitiveOnPackageId()
    {
        var store = ExtensionTrustStore.Load(_path);
        store.Approve("My.Pkg", "1.0.0");
        Assert.IsTrue(store.IsApproved("my.pkg", "1.0.0"));
    }

    [TestMethod]
    public void Revoke_RemovesApproval()
    {
        var store = ExtensionTrustStore.Load(_path);
        store.Approve("Pkg", "1.0.0");
        store.Revoke("Pkg");
        Assert.IsFalse(store.IsApproved("Pkg", "1.0.0"));
    }

    [TestMethod]
    public void SaveThenLoad_PersistsApprovals()
    {
        var store = ExtensionTrustStore.Load(_path);
        store.Approve("Pkg", "1.2.3");
        store.Save();

        var reloaded = ExtensionTrustStore.Load(_path);
        Assert.IsTrue(reloaded.IsApproved("Pkg", "1.2.3"));
        Assert.IsFalse(reloaded.IsApproved("Pkg", "9.9.9"));
    }

    [TestMethod]
    public void Load_MalformedFile_ReturnsEmptyStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json ");

        var store = ExtensionTrustStore.Load(_path);
        Assert.IsFalse(store.IsApproved("Pkg", "1.0.0"));
    }
}
