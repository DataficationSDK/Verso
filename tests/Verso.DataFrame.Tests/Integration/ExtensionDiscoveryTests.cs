using Verso.Extensions;

namespace Verso.DataFrame.Tests.Integration;

[TestClass]
public sealed class ExtensionDiscoveryTests
{
    [TestMethod]
    public async Task BuiltInDiscovery_FindsDataFrameFormatter()
    {
        await using var host = new ExtensionHost();

        await host.LoadBuiltInExtensionsAsync();

        Assert.IsTrue(
            host.GetFormatters().Any(
                formatter => formatter.ExtensionId == "verso.dataframe.formatter"));
    }
}
