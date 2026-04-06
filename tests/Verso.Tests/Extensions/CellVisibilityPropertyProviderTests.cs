using Verso.Extensions;
using Verso.Extensions.Layouts;
using Verso.Testing.Fakes;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class CellVisibilityPropertyProviderTests
{
    private readonly CellVisibilityPropertyProvider _provider = new();
    private readonly StubCellRenderContext _renderContext = new();

    [TestInitialize]
    public async Task Setup()
    {
        // Set up an ExtensionHost with layouts and a renderer so the provider can query them
        var host = new ExtensionHost();
        await host.LoadExtensionAsync(new FakeCellRenderer(extensionId: "com.test.renderer.fake"));
        await host.LoadExtensionAsync(_provider);
        // Layouts are loaded via built-in scan; load them explicitly for testing
        await host.LoadExtensionAsync(new PresentationLayout());
        await host.LoadExtensionAsync(new DashboardLayout());
        await host.LoadExtensionAsync(new NotebookLayout());
    }

    [TestMethod]
    public void AppliesTo_ReturnsTrue_ForAnyCell()
    {
        var cell = new CellModel { Type = "code" };
        Assert.IsTrue(_provider.AppliesTo(cell, _renderContext));
    }

    [TestMethod]
    public void Order_IsZero()
    {
        Assert.AreEqual(0, _provider.Order);
    }

    [TestMethod]
    public async Task GetPropertiesSection_ContainsFieldsForQualifyingLayouts()
    {
        var cell = new CellModel { Type = "fake" };
        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        Assert.AreEqual("Visibility", section.Title);
        // Presentation and Dashboard qualify (more than just Visible); Notebook does not
        Assert.AreEqual(2, section.Fields.Count);

        var fieldNames = section.Fields.Select(f => f.Name).ToList();
        Assert.IsTrue(fieldNames.Contains("visibility:presentation"));
        Assert.IsTrue(fieldNames.Contains("visibility:dashboard"));
    }

    [TestMethod]
    public async Task GetPropertiesSection_FieldsAreSelectType()
    {
        var cell = new CellModel { Type = "fake" };
        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        foreach (var field in section.Fields)
        {
            Assert.AreEqual(PropertyFieldType.Select, field.FieldType);
            Assert.IsNotNull(field.Options);
            Assert.IsTrue(field.Options!.Count > 0);
        }
    }

    [TestMethod]
    public async Task GetPropertiesSection_PresentationField_HasThreeOptions()
    {
        var cell = new CellModel { Type = "fake" };
        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        var presField = section.Fields.First(f => f.Name == "visibility:presentation");
        // Presentation supports: Visible, Hidden, OutputOnly
        Assert.AreEqual(3, presField.Options!.Count);
    }

    [TestMethod]
    public async Task GetPropertiesSection_DashboardField_HasTwoOptions()
    {
        var cell = new CellModel { Type = "fake" };
        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        var dashField = section.Fields.First(f => f.Name == "visibility:dashboard");
        // Dashboard supports: Visible, Hidden
        Assert.AreEqual(2, dashField.Options!.Count);
    }

    [TestMethod]
    public async Task GetPropertiesSection_ReadsCurrentValueFromMetadata()
    {
        var cell = new CellModel { Type = "fake" };
        cell.Metadata["verso:visibility"] = new Dictionary<string, string>
        {
            ["presentation"] = "hidden",
        };

        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        var presField = section.Fields.First(f => f.Name == "visibility:presentation");
        Assert.AreEqual("hidden", presField.CurrentValue);
    }

    [TestMethod]
    public async Task GetPropertiesSection_NoOverride_CurrentValueIsNull()
    {
        var cell = new CellModel { Type = "fake" };

        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        var presField = section.Fields.First(f => f.Name == "visibility:presentation");
        Assert.IsNull(presField.CurrentValue);
    }

    [TestMethod]
    public async Task GetPropertiesSection_DescriptionReflectsDefaultHint()
    {
        var cell = new CellModel { Type = "fake" };
        // FakeCellRenderer has DefaultVisibility = Content (DIM default)
        var section = await _provider.GetPropertiesSectionAsync(cell, _renderContext);

        var presField = section.Fields.First(f => f.Name == "visibility:presentation");
        Assert.AreEqual("Default: Visible", presField.Description);
    }

    [TestMethod]
    public async Task OnPropertyChanged_WritesToMetadata()
    {
        var cell = new CellModel { Type = "fake" };

        await _provider.OnPropertyChangedAsync(cell, "visibility:presentation", "hidden", _renderContext);

        Assert.IsTrue(cell.Metadata.ContainsKey("verso:visibility"));
        var dict = (Dictionary<string, string>)cell.Metadata["verso:visibility"];
        Assert.AreEqual("hidden", dict["presentation"]);
    }

    [TestMethod]
    public async Task OnPropertyChanged_SelectingDefault_RemovesOverride()
    {
        var cell = new CellModel { Type = "fake" };
        // First set an override
        cell.Metadata["verso:visibility"] = new Dictionary<string, string>
        {
            ["presentation"] = "hidden",
        };

        // Now set back to the default (Content -> Visible for presentation)
        await _provider.OnPropertyChangedAsync(cell, "visibility:presentation", "visible", _renderContext);

        // Override should be removed, and if dict is empty, the key should be removed too
        Assert.IsFalse(cell.Metadata.ContainsKey("verso:visibility"));
    }

    [TestMethod]
    public async Task OnPropertyChanged_NullValue_RemovesOverride()
    {
        var cell = new CellModel { Type = "fake" };
        cell.Metadata["verso:visibility"] = new Dictionary<string, string>
        {
            ["presentation"] = "hidden",
        };

        await _provider.OnPropertyChangedAsync(cell, "visibility:presentation", null, _renderContext);

        Assert.IsFalse(cell.Metadata.ContainsKey("verso:visibility"));
    }

    [TestMethod]
    public async Task OnPropertyChanged_PreservesOtherLayoutOverrides()
    {
        var cell = new CellModel { Type = "fake" };
        cell.Metadata["verso:visibility"] = new Dictionary<string, string>
        {
            ["presentation"] = "hidden",
            ["dashboard"] = "hidden",
        };

        // Remove only the presentation override
        await _provider.OnPropertyChangedAsync(cell, "visibility:presentation", "visible", _renderContext);

        Assert.IsTrue(cell.Metadata.ContainsKey("verso:visibility"));
        var dict = (Dictionary<string, string>)cell.Metadata["verso:visibility"];
        Assert.IsFalse(dict.ContainsKey("presentation"));
        Assert.AreEqual("hidden", dict["dashboard"]);
    }

    [TestMethod]
    public async Task ProviderDiscoveredByExtensionHost()
    {
        var host = new ExtensionHost();
        try
        {
            await host.LoadBuiltInExtensionsAsync();

            var providers = host.GetPropertyProviders();
            Assert.IsTrue(providers.Any(p => p.ExtensionId == "verso.propertyprovider.visibility"),
                "CellVisibilityPropertyProvider should be discovered by LoadBuiltInExtensionsAsync");
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
