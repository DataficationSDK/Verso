using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Execution;
using Verso.Extensions;

namespace Verso.Cli.Tests.Repl;

/// <summary>
/// Smoke coverage for <see cref="ToolbarActionResolver"/> — the shared resolver behind
/// both <c>verso publish</c> and the REPL's <c>.export</c>.
/// </summary>
[TestClass]
public class ToolbarActionResolverTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _host = new ExtensionHost();
        _host.ConsentHandler = (_, _) => Task.FromResult(true);
        await _host.LoadBuiltInExtensionsAsync();
    }

    [TestCleanup]
    public async Task Teardown()
    {
        await _host.DisposeAsync();
    }

    [TestMethod]
    public void TryResolveAction_UnknownFormat_ReturnsDescriptiveError()
    {
        var ok = ToolbarActionResolver.TryResolveAction(_host, "totally-fake-format", out _, out var error);
        Assert.IsFalse(ok);
        StringAssert.Contains(error, "not registered");
    }

    [TestMethod]
    public void TryResolveTheme_UnknownName_ReturnsDescriptiveError()
    {
        var ok = ToolbarActionResolver.TryResolveTheme(_host, "totally-fake-theme", out _, out var error);
        Assert.IsFalse(ok);
        StringAssert.Contains(error, "not registered");
    }
}
