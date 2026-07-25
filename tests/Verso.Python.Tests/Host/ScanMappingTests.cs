using System.Text.Json.Nodes;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers turning an import-scan reply into the model the install path consumes. A reply that is
/// missing or malformed has to yield nothing, because that skips the install and lets the cell
/// run: a scan is an optimization over failing at the import, not a precondition for executing.
/// </summary>
[TestClass]
public sealed class ScanMappingTests
{
    private static JsonObject Reply(string json) => (JsonObject)JsonNode.Parse(json)!;

    [TestMethod]
    public void Map_ReadsAMissingModule()
    {
        var scan = HostPackages.MapScan(Reply(
            "{\"missing\":[{\"module\":\"cv2\",\"optional\":false}],\"unsatisfied\":[]}"));

        Assert.AreEqual(1, scan.Missing.Count);
        Assert.AreEqual("cv2", scan.Missing[0].Module);
        Assert.IsFalse(scan.Missing[0].Optional);
    }

    [TestMethod]
    public void Map_ReadsTheGuardedFlag()
    {
        var scan = HostPackages.MapScan(Reply(
            "{\"missing\":[{\"module\":\"ujson\",\"optional\":true}]}"));

        Assert.IsTrue(scan.Missing[0].Optional);
    }

    [TestMethod]
    public void Map_TreatsAnAbsentFlagAsRequired()
    {
        var scan = HostPackages.MapScan(Reply("{\"missing\":[{\"module\":\"cv2\"}]}"));

        Assert.IsFalse(scan.Missing[0].Optional);
    }

    [TestMethod]
    public void Map_ReadsUnsatisfiedRequirements()
    {
        var scan = HostPackages.MapScan(Reply(
            "{\"missing\":[],\"unsatisfied\":[\"pandas>=2\",\"httpx\"]}"));

        CollectionAssert.AreEqual(new[] { "pandas>=2", "httpx" }, scan.Unsatisfied.ToArray());
    }

    [TestMethod]
    public void Map_TrimsRequirementStrings()
    {
        var scan = HostPackages.MapScan(Reply("{\"unsatisfied\":[\"  httpx  \"]}"));

        Assert.AreEqual("httpx", scan.Unsatisfied[0]);
    }

    [TestMethod]
    public void Map_SkipsAnEntryWithNoModuleName()
    {
        var scan = HostPackages.MapScan(Reply(
            "{\"missing\":[{\"optional\":true},{\"module\":\"\",\"optional\":false},{\"module\":\"cv2\"}]}"));

        Assert.AreEqual(1, scan.Missing.Count);
        Assert.AreEqual("cv2", scan.Missing[0].Module);
    }

    [TestMethod]
    public void Map_SkipsEntriesOfTheWrongShape()
    {
        var scan = HostPackages.MapScan(Reply("{\"missing\":[\"cv2\",7],\"unsatisfied\":[{\"a\":1},\"\"]}"));

        Assert.IsTrue(scan.IsEmpty);
    }

    [TestMethod]
    public void Map_ReportsNothingForAnEmptyReply()
    {
        Assert.IsTrue(HostPackages.MapScan(Reply("{}")).IsEmpty);
        Assert.IsTrue(HostPackages.MapScan(Reply("{\"missing\":[],\"unsatisfied\":[]}")).IsEmpty);
    }

    [TestMethod]
    public void Map_ReportsNothingWhenThereIsNoReply()
    {
        Assert.IsTrue(HostPackages.MapScan(null).IsEmpty);
    }

    [TestMethod]
    public void Map_ReportsNothingWhenTheFieldsAreNotArrays()
    {
        var scan = HostPackages.MapScan(Reply("{\"missing\":\"cv2\",\"unsatisfied\":3}"));

        Assert.IsTrue(scan.IsEmpty);
    }

    [TestMethod]
    public void ScanReply_CompletesItsRequest()
    {
        // Without this the read pump routes the reply to the event stream and the caller waits
        // out its whole deadline for an answer that already arrived.
        Assert.IsTrue(HostProtocol.IsReply(HostProtocol.ScanReply));
    }
}
