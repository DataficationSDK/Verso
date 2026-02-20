namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class SettingsPanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };
    }

    [TestMethod]
    public void NotLoaded_ShowsNotOpenMessage()
    {
        _service.IsLoaded = false;

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("No notebook is open"));
    }

    [TestMethod]
    public void NoSettings_ShowsEmptyState()
    {
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>();

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("No enabled extensions with configurable settings"));
    }

    [TestMethod]
    public void WithSettings_RendersGroupHeaders()
    {
        SetupEnabledExtensionWithSettings();

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        // Group headers should be visible (collapsed)
        Assert.IsTrue(cut.Markup.Contains("verso-settings-group-header"));
    }

    [TestMethod]
    public void ExpandedGroup_ShowsSettingLabels()
    {
        SetupEnabledExtensionWithSettings();

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        // Expand the group
        cut.Find(".verso-settings-group-header").Click();

        Assert.IsTrue(cut.Markup.Contains("Connection String"));
        Assert.IsTrue(cut.Markup.Contains("Timeout"));
    }

    [TestMethod]
    public void BooleanSetting_RendersCheckbox()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("enabled", "Enabled", "Enable feature", SettingType.Boolean, true)
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        cut.Find(".verso-settings-group-header").Click();

        var checkboxes = cut.FindAll("input[type=checkbox]");
        Assert.IsTrue(checkboxes.Count > 0);
    }

    [TestMethod]
    public void IntegerSetting_RendersNumberInput()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("count", "Count", "Item count", SettingType.Integer, 10)
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        cut.Find(".verso-settings-group-header").Click();

        var numberInputs = cut.FindAll("input[type=number]");
        Assert.IsTrue(numberInputs.Count > 0);
    }

    [TestMethod]
    public void StringChoiceSetting_RendersSelect()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("mode", "Mode", "Operating mode", SettingType.StringChoice, "auto",
                    Constraints: new SettingConstraints(Choices: new[] { "auto", "manual", "hybrid" }))
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        cut.Find(".verso-settings-group-header").Click();

        var selects = cut.FindAll("select");
        Assert.IsTrue(selects.Count > 0);
    }

    [TestMethod]
    public void StringSetting_RendersTextInput()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("name", "Name", "A name", SettingType.String)
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        cut.Find(".verso-settings-group-header").Click();

        var textInputs = cut.FindAll("input[type=text]");
        Assert.IsTrue(textInputs.Count > 0);
    }

    [TestMethod]
    public void SettingDescription_Displayed()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("port", "Port", "The TCP port number", SettingType.Integer, 8080)
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        cut.Find(".verso-settings-group-header").Click();

        Assert.IsTrue(cut.Markup.Contains("The TCP port number"));
    }

    [TestMethod]
    public void DisabledExtension_SettingsNotShown()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.test", "Test Ext", "1.0", null, null, ExtensionStatus.Disabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.test", new List<SettingDefinition>
            {
                new("port", "Port", "The TCP port number", SettingType.Integer, 8080)
            })
        };

        var cut = RenderComponent<SettingsPanel>(p => p
            .Add(s => s.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("No enabled extensions with configurable settings"));
    }

    private void SetupEnabledExtensionWithSettings()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.sql", "SQL Extension", "1.0.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };
        _service.SettingDefinitions = new List<ExtensionSettingsGroup>
        {
            new("ext.sql", new List<SettingDefinition>
            {
                new("connectionString", "Connection String", "DB connection", SettingType.String),
                new("timeout", "Timeout", "Query timeout in seconds", SettingType.Integer, 30)
            })
        };
    }
}
