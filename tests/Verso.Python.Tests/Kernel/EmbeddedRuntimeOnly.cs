namespace Verso.Python.Tests.Kernel;

/// <summary>
/// Guards tests that describe behaviour only the embedded runtime provides today. The
/// out-of-process host runs cells but does not yet share variables or answer IntelliSense
/// requests, so these tests report inconclusive when a session opts into it rather than failing
/// for a capability that is deliberately not there yet.
/// </summary>
internal static class EmbeddedRuntimeOnly
{
    public static bool HostProcessEnabled
        => Environment.GetEnvironmentVariable("VERSO_PYTHON_HOST") is "1" or "true" or "TRUE" or "True";

    public static void Require(string capability)
    {
        if (HostProcessEnabled)
            Assert.Inconclusive($"{capability} is not carried by the out-of-process Python host yet.");
    }
}
