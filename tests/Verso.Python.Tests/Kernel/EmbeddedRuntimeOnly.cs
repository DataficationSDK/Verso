namespace Verso.Python.Tests.Kernel;

/// <summary>
/// Guards tests that describe behaviour only the embedded runtime provides today. The
/// out-of-process host runs cells and shares variables but does not yet answer IntelliSense
/// requests, so these tests report inconclusive rather than failing for a capability that is
/// deliberately not there yet. The host is the default, so opting out is what turns them on.
/// </summary>
internal static class EmbeddedRuntimeOnly
{
    public static bool HostProcessEnabled
        => Environment.GetEnvironmentVariable("VERSO_PYTHON_HOST") is not ("0" or "false" or "FALSE" or "False");

    public static void Require(string capability)
    {
        if (HostProcessEnabled)
            Assert.Inconclusive($"{capability} is not carried by the out-of-process Python host yet.");
    }
}
