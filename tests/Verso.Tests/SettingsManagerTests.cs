namespace Verso.Tests;

[TestClass]
public class SettingsManagerTests
{
    private TestExtensionWithSettings _extension = null!;
    private SettingsManager _manager = null!;

    [TestInitialize]
    public void Setup()
    {
        _extension = new TestExtensionWithSettings();
        _manager = new SettingsManager(new List<IExtensionSettings> { _extension });
    }

    [TestMethod]
    public void GetAllDefinitions_ReturnsExtensionDefinitions()
    {
        var defs = _manager.GetAllDefinitions();
        Assert.AreEqual(1, defs.Count);
        Assert.AreEqual("test.settings.ext", defs[0].ExtensionId);
        Assert.AreEqual(2, defs[0].Definitions.Count);
    }

    [TestMethod]
    public async Task RestoreSettingsAsync_AppliesPersistedValues()
    {
        var notebook = new NotebookModel();
        notebook.ExtensionSettings["test.settings.ext"] = new Dictionary<string, object?>
        {
            ["warningLevel"] = 3,
            ["verbose"] = true
        };

        await _manager.RestoreSettingsAsync(notebook);

        Assert.AreEqual(3, _extension.WarningLevel);
        Assert.IsTrue(_extension.Verbose);
    }

    [TestMethod]
    public async Task RestoreSettingsAsync_IgnoresUnknownExtensions()
    {
        var notebook = new NotebookModel();
        notebook.ExtensionSettings["nonexistent.ext"] = new Dictionary<string, object?>
        {
            ["foo"] = "bar"
        };

        // Should not throw
        await _manager.RestoreSettingsAsync(notebook);
    }

    [TestMethod]
    public async Task SaveSettingsAsync_PersistsOverriddenValues()
    {
        _extension.WarningLevel = 3;
        var notebook = new NotebookModel();

        await _manager.SaveSettingsAsync(notebook);

        Assert.IsTrue(notebook.ExtensionSettings.ContainsKey("test.settings.ext"));
        var saved = notebook.ExtensionSettings["test.settings.ext"];
        Assert.IsTrue(saved.ContainsKey("warningLevel"));
        Assert.AreEqual(3, saved["warningLevel"]);
    }

    [TestMethod]
    public async Task SaveSettingsAsync_DoesNotPersistDefaults()
    {
        // Extension has defaults: warningLevel=4, verbose=false
        // Don't change anything — should not persist
        var notebook = new NotebookModel();

        await _manager.SaveSettingsAsync(notebook);

        Assert.IsFalse(notebook.ExtensionSettings.ContainsKey("test.settings.ext"));
    }

    [TestMethod]
    public async Task UpdateSettingAsync_UpdatesExtensionAndRaisesEvent()
    {
        string? changedExtId = null;
        string? changedName = null;
        object? changedValue = null;

        _manager.OnSettingsChanged += (extId, name, value) =>
        {
            changedExtId = extId;
            changedName = name;
            changedValue = value;
        };

        await _manager.UpdateSettingAsync("test.settings.ext", "warningLevel", 2);

        Assert.AreEqual(2, _extension.WarningLevel);
        Assert.AreEqual("test.settings.ext", changedExtId);
        Assert.AreEqual("warningLevel", changedName);
        Assert.AreEqual(2, changedValue);
    }

    [TestMethod]
    public async Task UpdateSettingAsync_ThrowsForUnknownExtension()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _manager.UpdateSettingAsync("nonexistent.ext", "foo", "bar"));
    }

    [TestMethod]
    public async Task ResetSettingAsync_RestoresDefault()
    {
        _extension.WarningLevel = 1;
        await _manager.ResetSettingAsync("test.settings.ext", "warningLevel");

        Assert.AreEqual(4, _extension.WarningLevel); // Default is 4
    }

    [TestMethod]
    public async Task RestoreSettingsAsync_EmptySettings_NoEffect()
    {
        var notebook = new NotebookModel();
        // No extension settings at all
        await _manager.RestoreSettingsAsync(notebook);

        Assert.AreEqual(4, _extension.WarningLevel); // Default
        Assert.IsFalse(_extension.Verbose); // Default
    }

    // --- Test helper ---

    private sealed class TestExtensionWithSettings : IExtension, IExtensionSettings
    {
        public string ExtensionId => "test.settings.ext";
        public string Name => "Test Settings Extension";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;

        public int WarningLevel { get; set; } = 4;
        public bool Verbose { get; set; }

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public IReadOnlyList<SettingDefinition> SettingDefinitions => new[]
        {
            new SettingDefinition("warningLevel", "Warning Level", "Compiler warning level.",
                SettingType.Integer, DefaultValue: 4, Category: "Compiler",
                Constraints: new SettingConstraints(MinValue: 0, MaxValue: 5)),
            new SettingDefinition("verbose", "Verbose", "Enable verbose output.",
                SettingType.Boolean, DefaultValue: false)
        };

        public IReadOnlyDictionary<string, object?> GetSettingValues()
        {
            return new Dictionary<string, object?>
            {
                ["warningLevel"] = WarningLevel,
                ["verbose"] = Verbose
            };
        }

        public Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values)
        {
            if (values.TryGetValue("warningLevel", out var wl) && wl is int w)
                WarningLevel = w;
            // Handle long from JSON deserialization
            if (values.TryGetValue("warningLevel", out var wl2) && wl2 is long wLong)
                WarningLevel = (int)wLong;
            if (values.TryGetValue("verbose", out var v) && v is bool b)
                Verbose = b;
            return Task.CompletedTask;
        }

        public Task OnSettingChangedAsync(string name, object? value)
        {
            switch (name)
            {
                case "warningLevel" when value is int w:
                    WarningLevel = w;
                    break;
                case "warningLevel" when value is long wl:
                    WarningLevel = (int)wl;
                    break;
                case "verbose" when value is bool b:
                    Verbose = b;
                    break;
            }
            return Task.CompletedTask;
        }
    }
}
