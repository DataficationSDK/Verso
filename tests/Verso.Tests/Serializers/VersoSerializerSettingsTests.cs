using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public class VersoSerializerSettingsTests
{
    private VersoSerializer _serializer = null!;

    [TestInitialize]
    public void Setup()
    {
        _serializer = new VersoSerializer();
    }

    [TestMethod]
    public async Task RoundTrip_WithExtensionSettings()
    {
        var notebook = new NotebookModel
        {
            Title = "Settings Test",
            DefaultKernelId = "csharp"
        };
        notebook.ExtensionSettings["verso.kernel.fsharp"] = new Dictionary<string, object?>
        {
            ["warningLevel"] = 5,
            ["langVersion"] = "8.0"
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsTrue(result.ExtensionSettings.ContainsKey("verso.kernel.fsharp"));
        var settings = result.ExtensionSettings["verso.kernel.fsharp"];
        Assert.AreEqual(2, settings.Count);
        // JSON numbers round-trip through ConvertJsonElement as long or double
        Assert.IsTrue(settings["warningLevel"] is long or double);
        Assert.AreEqual(5.0, Convert.ToDouble(settings["warningLevel"]));
        Assert.AreEqual("8.0", settings["langVersion"]);
    }

    [TestMethod]
    public async Task RoundTrip_WithoutExtensionSettings()
    {
        var notebook = new NotebookModel
        {
            Title = "No Settings",
            DefaultKernelId = "csharp"
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(0, result.ExtensionSettings.Count);
    }

    [TestMethod]
    public async Task RoundTrip_MultipleExtensions()
    {
        var notebook = new NotebookModel { Title = "Multi" };
        notebook.ExtensionSettings["ext.a"] = new Dictionary<string, object?>
        {
            ["flag"] = true
        };
        notebook.ExtensionSettings["ext.b"] = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 42
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(2, result.ExtensionSettings.Count);
        Assert.IsTrue((bool)result.ExtensionSettings["ext.a"]["flag"]!);
        Assert.AreEqual("test", result.ExtensionSettings["ext.b"]["name"]);
        Assert.AreEqual(42.0, Convert.ToDouble(result.ExtensionSettings["ext.b"]["count"]));
    }

    [TestMethod]
    public async Task RoundTrip_NullSettingValue()
    {
        var notebook = new NotebookModel { Title = "Null" };
        notebook.ExtensionSettings["ext.c"] = new Dictionary<string, object?>
        {
            ["optional"] = null,
            ["required"] = "value"
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.IsTrue(result.ExtensionSettings.ContainsKey("ext.c"));
        var settings = result.ExtensionSettings["ext.c"];
        Assert.IsNull(settings["optional"]);
        Assert.AreEqual("value", settings["required"]);
    }

    [TestMethod]
    public async Task BackwardCompatibility_OldFileWithoutExtensionSettings()
    {
        // Simulate a .verso file from before IExtensionSettings existed
        var oldJson = "{ \"verso\": \"1.0\", \"metadata\": { \"title\": \"Old Notebook\", \"defaultKernel\": \"csharp\" }, \"cells\": [] }";

        var result = await _serializer.DeserializeAsync(oldJson);

        Assert.AreEqual("Old Notebook", result.Title);
        Assert.AreEqual(0, result.ExtensionSettings.Count);
    }

    [TestMethod]
    public async Task Serialization_EmptySettings_OmittedFromJson()
    {
        var notebook = new NotebookModel { Title = "Empty" };
        // No extension settings added

        var json = await _serializer.SerializeAsync(notebook);

        // extensionSettings should not appear in JSON when empty
        Assert.IsFalse(json.Contains("extensionSettings"));
    }

    [TestMethod]
    public async Task RoundTrip_SettingsAndLayouts_Coexist()
    {
        var notebook = new NotebookModel { Title = "Both" };
        notebook.Layouts["verso.layout.dashboard"] = new Dictionary<string, object>
        {
            ["columns"] = 12
        };
        notebook.ExtensionSettings["verso.kernel.fsharp"] = new Dictionary<string, object?>
        {
            ["warningLevel"] = 3
        };

        var json = await _serializer.SerializeAsync(notebook);
        var result = await _serializer.DeserializeAsync(json);

        Assert.AreEqual(1, result.Layouts.Count);
        Assert.AreEqual(1, result.ExtensionSettings.Count);
        Assert.AreEqual(3.0, Convert.ToDouble(result.ExtensionSettings["verso.kernel.fsharp"]["warningLevel"]));
    }
}
