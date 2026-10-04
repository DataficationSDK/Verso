using Motus.Testing.MSTest;

namespace Verso.E2E.Tests;

/// <summary>
/// Starts the one browser every test in the assembly shares, and closes it at the end.
/// </summary>
[TestClass]
public static class BrowserLifetime
{
    [AssemblyInitialize]
    public static Task LaunchAsync(TestContext _) => MotusTestBase.LaunchBrowserAsync();

    [AssemblyCleanup]
    public static Task CloseAsync() => MotusTestBase.CloseBrowserAsync();
}
