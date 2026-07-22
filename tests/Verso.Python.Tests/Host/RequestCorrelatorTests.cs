using System.Text.Json.Nodes;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

[TestClass]
public sealed class RequestCorrelatorTests
{
    [TestMethod]
    public async Task Register_ThenComplete_ResolvesWithReply()
    {
        var correlator = new RequestCorrelator();
        var id = correlator.NextId();
        var task = correlator.RegisterAsync(id, CancellationToken.None);

        var reply = new JsonObject { ["req_id"] = id, ["ok"] = true };
        Assert.IsTrue(correlator.TryComplete(id, reply));

        var result = await task;
        Assert.AreEqual(true, (bool?)result["ok"]);
    }

    [TestMethod]
    public void TryComplete_UnknownId_ReturnsFalse()
    {
        var correlator = new RequestCorrelator();
        Assert.IsFalse(correlator.TryComplete(999, new JsonObject()));
    }

    [TestMethod]
    public void NextId_IsMonotonicAndUnique()
    {
        var correlator = new RequestCorrelator();
        var seen = new HashSet<int>();
        var previous = 0;
        for (var i = 0; i < 100; i++)
        {
            var id = correlator.NextId();
            Assert.IsTrue(id > previous, "ids should strictly increase");
            Assert.IsTrue(seen.Add(id), "ids should be unique");
            previous = id;
        }
    }

    [TestMethod]
    public async Task FailAll_FaultsPendingRequests()
    {
        var correlator = new RequestCorrelator();
        var id = correlator.NextId();
        var task = correlator.RegisterAsync(id, CancellationToken.None);

        correlator.FailAll(new PythonHostException("connection dropped"));

        await Assert.ThrowsExceptionAsync<PythonHostException>(async () => await task);
        Assert.IsFalse(correlator.TryComplete(id, new JsonObject()), "a failed request should no longer be pending");
    }

    [TestMethod]
    public async Task Cancellation_CancelsPendingRequest()
    {
        var correlator = new RequestCorrelator();
        var id = correlator.NextId();
        using var cts = new CancellationTokenSource();
        var task = correlator.RegisterAsync(id, cts.Token);

        cts.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await task);
        Assert.IsFalse(correlator.TryComplete(id, new JsonObject()), "a cancelled request should no longer be pending");
    }

    [TestMethod]
    public void DoubleComplete_IsSafe()
    {
        var correlator = new RequestCorrelator();
        var id = correlator.NextId();
        _ = correlator.RegisterAsync(id, CancellationToken.None);

        Assert.IsTrue(correlator.TryComplete(id, new JsonObject()));
        Assert.IsFalse(correlator.TryComplete(id, new JsonObject()), "completing a second time is a no-op");
    }
}
