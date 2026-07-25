using Verso.Abstractions;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.Kernel;

/// <summary>
/// Covers the notebook setting that carries the requirement list. Persistence is what makes it
/// worth having: the value travels in the notebook file, which is meaningful on any machine,
/// unlike the interpreter path that deliberately does not.
/// </summary>
[TestClass]
public sealed class PythonKernelSettingsTests
{
    private static PythonKernel Kernel() => new();

    [TestMethod]
    public void TheKernelDeclaresTheDependencyList()
    {
        var definition = Kernel().SettingDefinitions
            .Single(d => d.Name == "dependencies");

        Assert.AreEqual(SettingType.StringList, definition.SettingType);
        Assert.AreEqual("Packages", definition.Category);
    }

    [TestMethod]
    public async Task AppliedValuesAreReportedBack()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["dependencies"] = new[] { "pandas>=2", "httpx" },
        });

        var values = kernel.GetSettingValues();
        CollectionAssert.AreEqual(
            new[] { "pandas>=2", "httpx" }, (string[])values["dependencies"]!);
    }

    [TestMethod]
    public async Task AListPersistedAsObjectsIsRead()
    {
        // A value read back from a notebook file arrives as a list of boxed strings.
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["dependencies"] = new List<object> { "pandas>=2", "httpx" },
        });

        CollectionAssert.AreEqual(
            new[] { "pandas>=2", "httpx" }, (string[])kernel.GetSettingValues()["dependencies"]!);
    }

    [TestMethod]
    public async Task ACommaSeparatedStringIsRead()
    {
        // A settings surface with no list editor sends one string, which is also the most
        // natural thing for a person to type.
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["dependencies"] = "pandas>=2, httpx",
        });

        CollectionAssert.AreEqual(
            new[] { "pandas>=2", "httpx" }, (string[])kernel.GetSettingValues()["dependencies"]!);
    }

    [TestMethod]
    public async Task AnEmptyListIsNotReportedAsAnOverride()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["dependencies"] = Array.Empty<string>(),
        });

        Assert.IsFalse(kernel.GetSettingValues().ContainsKey("dependencies"),
            "Only a value that differs from the default is persisted.");
    }

    [TestMethod]
    public async Task BlankEntriesAreDropped()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["dependencies"] = new[] { "httpx", "   ", "" },
        });

        CollectionAssert.AreEqual(
            new[] { "httpx" }, (string[])kernel.GetSettingValues()["dependencies"]!);
    }

    [TestMethod]
    public async Task AnUnknownSettingIsIgnored()
    {
        var kernel = Kernel();

        await kernel.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["somethingElse"] = 7,
        });

        Assert.AreEqual(0, kernel.GetSettingValues().Count);
    }

    [TestMethod]
    public async Task AnInteractiveChangeTakesEffect()
    {
        var kernel = Kernel();

        await kernel.OnSettingChangedAsync("dependencies", new[] { "rich" });

        CollectionAssert.AreEqual(
            new[] { "rich" }, (string[])kernel.GetSettingValues()["dependencies"]!);
    }

    [TestMethod]
    public async Task ClearingTheListRemovesTheOverride()
    {
        var kernel = Kernel();
        await kernel.OnSettingChangedAsync("dependencies", new[] { "rich" });

        await kernel.OnSettingChangedAsync("dependencies", null);

        Assert.IsFalse(kernel.GetSettingValues().ContainsKey("dependencies"));
    }

    // --- policy from the environment ---

    [TestMethod]
    public void ThePolicyIsReadFromItsSpelling()
    {
        Assert.IsTrue(PythonKernel.TryParsePolicy("off", out var off));
        Assert.AreEqual(AutoInstallPolicy.Off, off);

        Assert.IsTrue(PythonKernel.TryParsePolicy("Auto", out var auto));
        Assert.AreEqual(AutoInstallPolicy.Auto, auto);

        Assert.IsTrue(PythonKernel.TryParsePolicy(" PROMPT ", out var prompt));
        Assert.AreEqual(AutoInstallPolicy.Prompt, prompt);
    }

    [TestMethod]
    public void AnUnrecognizedPolicyLeavesTheDefault()
    {
        Assert.IsFalse(PythonKernel.TryParsePolicy("maybe", out var policy));
        Assert.AreEqual(AutoInstallPolicy.Prompt, policy);

        Assert.IsFalse(PythonKernel.TryParsePolicy(null, out _));
        Assert.IsFalse(PythonKernel.TryParsePolicy("", out _));
    }
}
