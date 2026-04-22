using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Signals;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class SignalHandlerTests
{
    [TestMethod]
    public void OuterCancellation_FlowsThroughToken()
    {
        using var outer = new CancellationTokenSource();
        using var handler = new SignalHandler(outer.Token);

        Assert.IsFalse(handler.Token.IsCancellationRequested);
        outer.Cancel();
        Assert.IsTrue(handler.Token.IsCancellationRequested,
            "Outer cancellation (e.g. the command-line host tearing down) must propagate.");
        Assert.IsNull(handler.ExitRequested,
            "Only signal-driven cancellation sets ExitRequested; external CTS cancellation does not imply a specific exit code.");
    }

    [TestMethod]
    public void Dispose_UnregistersHandler_NoThrow()
    {
        using var outer = new CancellationTokenSource();
        var handler = new SignalHandler(outer.Token);
        handler.Dispose();
        // A second dispose must not throw.
        handler.Dispose();
    }
}
