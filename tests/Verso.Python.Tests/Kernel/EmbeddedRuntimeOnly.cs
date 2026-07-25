using Verso.Python.Tests.Host;

namespace Verso.Python.Tests.Kernel;

/// <summary>
/// Which Python runtime the suite is exercising. The out-of-process host is the default and the
/// embedded runtime is selected by opting out, so a test that describes what only one of them does
/// asks here and reports inconclusive on the other rather than failing for a capability that is
/// deliberately absent.
/// </summary>
internal static class EmbeddedRuntimeOnly
{
    public static bool HostProcessEnabled
        => Environment.GetEnvironmentVariable("VERSO_PYTHON_HOST") is not ("0" or "false" or "FALSE" or "False");
}

/// <summary>
/// Guards tests for behaviour the Python process provides and the embedded runtime does not.
/// Editor assistance is the case: it is answered inside the interpreter the cells run in, which
/// the embedded runtime has no equivalent of.
/// </summary>
internal static class HostRuntimeOnly
{
    /// <summary>
    /// Ends the test as inconclusive when the embedded runtime is selected, and when the host is
    /// selected but no interpreter can be found to run it.
    /// </summary>
    public static void Require(string capability)
    {
        if (!EmbeddedRuntimeOnly.HostProcessEnabled)
            Assert.Inconclusive($"{capability} is provided by the Python process, not the embedded runtime.");

        PythonProbe.Require();
    }
}
