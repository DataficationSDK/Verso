namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class CellPropertiesPanelTests : BunitTestContext
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

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("No notebook is open"));
    }

    [TestMethod]
    public void NoCellSelected_ShowsSelectMessage()
    {
        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, null));

        Assert.IsTrue(cut.Markup.Contains("Select a cell to view its properties"));
    }

    [TestMethod]
    public void CellSelected_NoSections_ShowsEmptyState()
    {
        _service.PropertySections = new List<PropertySectionResult>();

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("No properties available for this cell"));
    }

    [TestMethod]
    public void WithTextField_RendersTextInput()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("General", new PropertyField("name", "Name", PropertyFieldType.Text, "hello"))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("General"));
        Assert.IsTrue(cut.Markup.Contains("Name"));
        var input = cut.Find("input[type=text]");
        Assert.AreEqual("hello", input.GetAttribute("value"));
    }

    [TestMethod]
    public void WithNumberField_RendersNumberInput()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Config", new PropertyField("timeout", "Timeout", PropertyFieldType.Number, 30))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var input = cut.Find("input[type=number]");
        Assert.AreEqual("30", input.GetAttribute("value"));
    }

    [TestMethod]
    public void WithToggleField_RendersCheckbox()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Options", new PropertyField("enabled", "Enabled", PropertyFieldType.Toggle, true))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var checkbox = cut.Find("input[type=checkbox]");
        Assert.IsNotNull(checkbox);
    }

    [TestMethod]
    public void WithSelectField_RendersSelectWithOptions()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Visibility", new PropertyField(
                "layout", "Layout", PropertyFieldType.Select, "hidden",
                Options: new List<PropertyFieldOption>
                {
                    new("visible", "Visible"),
                    new("hidden", "Hidden"),
                    new("outputOnly", "Output Only")
                }))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var options = cut.FindAll("select option");
        Assert.AreEqual(3, options.Count);
        Assert.IsTrue(cut.Markup.Contains("Visible"));
        Assert.IsTrue(cut.Markup.Contains("Hidden"));
        Assert.IsTrue(cut.Markup.Contains("Output Only"));
    }

    [TestMethod]
    public void WithMultiSelectField_RendersOptionCheckboxes()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Tags", new PropertyField(
                "categories", "Categories", PropertyFieldType.MultiSelect, null,
                Options: new List<PropertyFieldOption>
                {
                    new("a", "Alpha"),
                    new("b", "Beta")
                }))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("Alpha"));
        Assert.IsTrue(cut.Markup.Contains("Beta"));
        Assert.IsTrue(cut.Markup.Contains("verso-properties-multiselect-option"));
    }

    [TestMethod]
    public void WithColorField_RendersColorInput()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Style", new PropertyField("bg", "Background", PropertyFieldType.Color, "#FF0000"))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var input = cut.Find("input[type=color]");
        Assert.AreEqual("#FF0000", input.GetAttribute("value"));
    }

    [TestMethod]
    public void WithTagsField_RendersExistingTags()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Labels", new PropertyField(
                "tags", "Tags", PropertyFieldType.Tags,
                new List<string> { "important", "review" }))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("important"));
        Assert.IsTrue(cut.Markup.Contains("review"));
        Assert.IsTrue(cut.Markup.Contains("verso-properties-tag"));
    }

    [TestMethod]
    public void ReadOnlyField_IsDisabled()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Info", new PropertyField("id", "Cell ID", PropertyFieldType.Text, "abc", IsReadOnly: true))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var input = cut.Find("input[type=text]");
        Assert.IsTrue(input.HasAttribute("disabled"));
    }

    [TestMethod]
    public void SectionHeader_Click_TogglesCollapse()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Collapsible", new PropertyField("x", "X", PropertyFieldType.Text, "val"))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        // Section starts expanded, field is visible
        Assert.IsTrue(cut.Markup.Contains("verso-properties-field-row"));

        // Click header to collapse
        cut.Find(".verso-properties-section-header").Click();
        Assert.IsFalse(cut.Markup.Contains("verso-properties-field-row"));

        // Click again to expand
        cut.Find(".verso-properties-section-header").Click();
        Assert.IsTrue(cut.Markup.Contains("verso-properties-field-row"));
    }

    [TestMethod]
    public void SectionDescription_IsRendered()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            new("ext.test", new PropertySection(
                "Visibility",
                "Configure how this cell appears in different layouts.",
                new List<PropertyField>
                {
                    new("v", "Vis", PropertyFieldType.Text, "")
                }))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("Configure how this cell appears"));
        Assert.IsTrue(cut.Markup.Contains("verso-properties-section-description"));
    }

    [TestMethod]
    public void FieldDescription_IsRendered()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("Test", new PropertyField(
                "f", "Field", PropertyFieldType.Text, "",
                Description: "This is a helpful description."))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        Assert.IsTrue(cut.Markup.Contains("This is a helpful description."));
        Assert.IsTrue(cut.Markup.Contains("verso-properties-field-description"));
    }

    [TestMethod]
    public void FieldChange_CallsNotifyPropertyChanged()
    {
        var cellId = Guid.NewGuid();
        _service.PropertySections = new List<PropertySectionResult>
        {
            new("ext.vis", new PropertySection("Vis", null, new List<PropertyField>
            {
                new("layout", "Layout", PropertyFieldType.Select, "visible",
                    Options: new List<PropertyFieldOption>
                    {
                        new("visible", "Visible"),
                        new("hidden", "Hidden")
                    })
            }))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, cellId));

        cut.Find("select").Change("hidden");

        Assert.AreEqual(1, _service.PropertyChangedCalls.Count);
        var call = _service.PropertyChangedCalls[0];
        Assert.AreEqual(cellId, call.CellId);
        Assert.AreEqual("ext.vis", call.ProviderExtensionId);
        Assert.AreEqual("layout", call.PropertyName);
        Assert.AreEqual("hidden", call.Value);
    }

    [TestMethod]
    public void MultipleSections_RenderedInOrder()
    {
        _service.PropertySections = new List<PropertySectionResult>
        {
            MakeSection("First Section", new PropertyField("a", "A", PropertyFieldType.Text, "")),
            MakeSection("Second Section", new PropertyField("b", "B", PropertyFieldType.Text, ""))
        };

        var cut = RenderComponent<CellPropertiesPanel>(p => p
            .Add(e => e.Service, _service)
            .Add(e => e.SelectedCellId, Guid.NewGuid()));

        var markup = cut.Markup;
        var firstIndex = markup.IndexOf("First Section");
        var secondIndex = markup.IndexOf("Second Section");
        Assert.IsTrue(firstIndex >= 0);
        Assert.IsTrue(secondIndex >= 0);
        Assert.IsTrue(firstIndex < secondIndex, "First Section should appear before Second Section");
    }

    private static PropertySectionResult MakeSection(string title, params PropertyField[] fields)
        => new("ext.test", new PropertySection(title, null, fields));
}
