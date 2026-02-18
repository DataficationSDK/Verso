using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public sealed class SettingsTests
{
    // --- Interface detection ---

    [TestMethod]
    public void FSharpKernel_ImplementsIExtensionSettings()
    {
        Assert.IsTrue(typeof(IExtensionSettings).IsAssignableFrom(typeof(FSharpKernel)));
    }

    [TestMethod]
    public void FSharpKernel_CanCastToIExtensionSettings()
    {
        var kernel = new FSharpKernel();
        Assert.IsInstanceOfType(kernel, typeof(IExtensionSettings));
    }

    // --- SettingDefinitions ---

    [TestMethod]
    public void SettingDefinitions_ExposesExpectedSettings()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        Assert.AreEqual(4, settings.SettingDefinitions.Count);

        var names = settings.SettingDefinitions.Select(d => d.Name).ToList();
        CollectionAssert.Contains(names, "warningLevel");
        CollectionAssert.Contains(names, "langVersion");
        CollectionAssert.Contains(names, "publishPrivateBindings");
        CollectionAssert.Contains(names, "maxCollectionDisplay");
    }

    [TestMethod]
    public void SettingDefinitions_WarningLevel_HasCorrectMetadata()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;
        var def = settings.SettingDefinitions.First(d => d.Name == "warningLevel");

        Assert.AreEqual(SettingType.Integer, def.SettingType);
        Assert.AreEqual(3, def.DefaultValue);
        Assert.AreEqual("Compiler", def.Category);
        Assert.IsNotNull(def.Constraints);
        Assert.AreEqual(0, def.Constraints!.MinValue);
        Assert.AreEqual(5, def.Constraints.MaxValue);
    }

    [TestMethod]
    public void SettingDefinitions_LangVersion_HasChoiceConstraint()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;
        var def = settings.SettingDefinitions.First(d => d.Name == "langVersion");

        Assert.AreEqual(SettingType.StringChoice, def.SettingType);
        Assert.AreEqual("preview", def.DefaultValue);
        Assert.IsNotNull(def.Constraints?.Choices);
        CollectionAssert.Contains(def.Constraints!.Choices!.ToList(), "preview");
        CollectionAssert.Contains(def.Constraints.Choices!.ToList(), "default");
    }

    [TestMethod]
    public void SettingDefinitions_PublishPrivateBindings_IsBooleanType()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;
        var def = settings.SettingDefinitions.First(d => d.Name == "publishPrivateBindings");

        Assert.AreEqual(SettingType.Boolean, def.SettingType);
        Assert.AreEqual(false, def.DefaultValue);
        Assert.AreEqual("Variables", def.Category);
    }

    [TestMethod]
    public void SettingDefinitions_MaxCollectionDisplay_HasRangeConstraint()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;
        var def = settings.SettingDefinitions.First(d => d.Name == "maxCollectionDisplay");

        Assert.AreEqual(SettingType.Integer, def.SettingType);
        Assert.AreEqual(100, def.DefaultValue);
        Assert.AreEqual("Display", def.Category);
        Assert.AreEqual(10, def.Constraints!.MinValue);
        Assert.AreEqual(10000, def.Constraints.MaxValue);
    }

    // --- GetSettingValues ---

    [TestMethod]
    public void GetSettingValues_DefaultOptions_ReturnsEmpty()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        var values = settings.GetSettingValues();

        Assert.AreEqual(0, values.Count, "Default options should produce no overrides");
    }

    // --- ApplySettingsAsync ---

    [TestMethod]
    public async Task ApplySettingsAsync_WarningLevel_UpdatesOption()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["warningLevel"] = 5
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(5, values["warningLevel"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_LangVersion_UpdatesOption()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["langVersion"] = "8.0"
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual("8.0", values["langVersion"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_PublishPrivateBindings_UpdatesOption()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["publishPrivateBindings"] = true
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(true, values["publishPrivateBindings"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_MaxCollectionDisplay_UpdatesOption()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["maxCollectionDisplay"] = 50
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(50, values["maxCollectionDisplay"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_MultipleSettings_AllApplied()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["warningLevel"] = 0,
            ["langVersion"] = "6.0",
            ["publishPrivateBindings"] = true,
            ["maxCollectionDisplay"] = 200
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(4, values.Count);
        Assert.AreEqual(0, values["warningLevel"]);
        Assert.AreEqual("6.0", values["langVersion"]);
        Assert.AreEqual(true, values["publishPrivateBindings"]);
        Assert.AreEqual(200, values["maxCollectionDisplay"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_UnknownSetting_SilentlyIgnored()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["unknownSetting"] = "value",
            ["warningLevel"] = 4
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(1, values.Count);
        Assert.AreEqual(4, values["warningLevel"]);
        Assert.IsFalse(values.ContainsKey("unknownSetting"));
    }

    [TestMethod]
    public async Task ApplySettingsAsync_WarningLevel_ClampedToRange()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["warningLevel"] = 99
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(5, values["warningLevel"], "Should be clamped to max of 5");
    }

    [TestMethod]
    public async Task ApplySettingsAsync_MaxCollectionDisplay_ClampedToMin()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["maxCollectionDisplay"] = 1
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(10, values["maxCollectionDisplay"], "Should be clamped to min of 10");
    }

    [TestMethod]
    public async Task ApplySettingsAsync_NumericTypeCoercion_HandlesLong()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        // JSON deserializers may produce long instead of int
        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["warningLevel"] = 4L
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(4, values["warningLevel"]);
    }

    [TestMethod]
    public async Task ApplySettingsAsync_NumericTypeCoercion_HandlesDouble()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        // JSON deserializers may produce double
        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["maxCollectionDisplay"] = 50.0
        });

        var values = settings.GetSettingValues();
        Assert.AreEqual(50, values["maxCollectionDisplay"]);
    }

    // --- OnSettingChangedAsync ---

    [TestMethod]
    public async Task OnSettingChangedAsync_SingleSetting_UpdatesValue()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.OnSettingChangedAsync("warningLevel", 2);

        var values = settings.GetSettingValues();
        Assert.AreEqual(2, values["warningLevel"]);
    }

    [TestMethod]
    public async Task OnSettingChangedAsync_DoesNotAffectOtherSettings()
    {
        var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["warningLevel"] = 5,
            ["langVersion"] = "8.0"
        });

        await settings.OnSettingChangedAsync("warningLevel", 1);

        var values = settings.GetSettingValues();
        Assert.AreEqual(1, values["warningLevel"]);
        Assert.AreEqual("8.0", values["langVersion"], "Other settings should be preserved");
    }

    // --- Settings used during initialization ---

    [TestMethod]
    public async Task ApplySettings_BeforeInit_AffectsKernelBehavior()
    {
        await using var kernel = new FSharpKernel();
        var settings = (IExtensionSettings)kernel;

        // Apply settings before initialization (mimics SettingsManager lifecycle)
        await settings.ApplySettingsAsync(new Dictionary<string, object?>
        {
            ["publishPrivateBindings"] = true
        });

        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        // Define a private binding (underscore-prefixed)
        await kernel.ExecuteAsync("let _secret = 42", ctx);

        // With publishPrivateBindings=true, _secret should appear in variable store
        Assert.IsTrue(ctx.Variables.TryGet<object>("_secret", out var val),
            "Private binding should be published when publishPrivateBindings is true");
    }

    [TestMethod]
    public async Task DefaultSettings_PrivateBindings_NotPublished()
    {
        await using var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        // Define a private binding
        await kernel.ExecuteAsync("let _hidden = 99", ctx);

        // With default settings (publishPrivateBindings=false), _hidden should not appear
        Assert.IsFalse(ctx.Variables.TryGet<object>("_hidden", out _),
            "Private binding should not be published by default");
    }
}
