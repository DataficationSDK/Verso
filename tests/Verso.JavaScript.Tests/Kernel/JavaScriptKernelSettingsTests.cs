using Verso.Abstractions;
using Verso.JavaScript.Kernel;

namespace Verso.JavaScript.Tests.Kernel;

/// <summary>
/// Covers the notebook setting deciding how much of an npm install a cell shows. It travels in
/// the notebook file, so a notebook whose author wants the detail keeps it on any machine.
/// </summary>
[TestClass]
public sealed class JavaScriptKernelSettingsTests
{
    private static JavaScriptKernel Kernel() => new();

    [TestMethod]
    public void TheKernelOffersItsSettings()
    {
        Assert.IsInstanceOfType<IExtensionSettings>(Kernel());
    }

    [TestMethod]
    public void InstallOutputIsHiddenUntilSomebodyAsksForIt()
    {
        var definition = Kernel().SettingDefinitions
            .Single(d => d.Name == "hideInstallOutput");

        Assert.AreEqual(SettingType.Boolean, definition.SettingType);
        Assert.AreEqual("Packages", definition.Category);
        Assert.AreEqual(true, definition.DefaultValue);
        Assert.IsFalse(Kernel().ShowInstallOutput);
    }

    [TestMethod]
    public async Task ClearingTheCheckboxShowsTheDetail()
    {
        var kernel = Kernel();

        await kernel.OnSettingChangedAsync("hideInstallOutput", false);

        Assert.IsTrue(kernel.ShowInstallOutput);
        Assert.AreEqual(false, kernel.GetSettingValues()["hideInstallOutput"]);
    }

    [TestMethod]
    public async Task TheDefaultIsNotPersisted()
    {
        var kernel = Kernel();
        await kernel.OnSettingChangedAsync("hideInstallOutput", false);

        await kernel.OnSettingChangedAsync("hideInstallOutput", true);

        Assert.IsFalse(kernel.GetSettingValues().ContainsKey("hideInstallOutput"));
    }

    [TestMethod]
    public async Task ASettingSavedInANotebookIsRead()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["hideInstallOutput"] = false,
        });

        Assert.IsTrue(kernel.ShowInstallOutput);
    }

    [TestMethod]
    public async Task AValueThatIsNeitherTrueNorFalseLeavesTheSettingAlone()
    {
        var kernel = Kernel();

        await kernel.OnSettingChangedAsync("hideInstallOutput", "perhaps");

        Assert.IsFalse(kernel.ShowInstallOutput);
    }

    [TestMethod]
    public async Task AnUnknownSettingIsIgnored()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?> { ["somethingElse"] = 7 });

        Assert.AreEqual(0, kernel.GetSettingValues().Count);
    }
}
