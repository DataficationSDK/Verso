using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Prompt;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class TerminalCapabilitiesTests
{
    [TestMethod]
    public void SupportsColor_NoColorFlag_ReturnsFalse()
    {
        // The explicit flag always wins — no environment setup required.
        Assert.IsFalse(TerminalCapabilities.SupportsColor(noColorFlag: true));
    }

    [TestMethod]
    public void SupportsPrettyPrompt_UnderRedirectedStdin_ReturnsFalse()
    {
        // The test host redirects stdin, so PrettyPrompt support should always be false
        // here. This keeps the test deterministic across CI and dev machines.
        Assert.IsFalse(TerminalCapabilities.SupportsPrettyPrompt());
    }
}
