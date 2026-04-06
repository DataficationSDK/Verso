namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class PropertyTypesTests
{
    [TestMethod]
    public void PropertyFieldOption_Construction()
    {
        var option = new PropertyFieldOption("hidden", "Hidden");
        Assert.AreEqual("hidden", option.Value);
        Assert.AreEqual("Hidden", option.DisplayName);
    }

    [TestMethod]
    public void PropertyFieldOption_Equality()
    {
        var a = new PropertyFieldOption("visible", "Visible");
        var b = new PropertyFieldOption("visible", "Visible");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void PropertyField_Construction_RequiredOnly()
    {
        var field = new PropertyField("name", "Name", PropertyFieldType.Text, "value");
        Assert.AreEqual("name", field.Name);
        Assert.AreEqual("Name", field.DisplayName);
        Assert.AreEqual(PropertyFieldType.Text, field.FieldType);
        Assert.AreEqual("value", field.CurrentValue);
    }

    [TestMethod]
    public void PropertyField_Defaults()
    {
        var field = new PropertyField("key", "Key", PropertyFieldType.Toggle, true);
        Assert.IsNull(field.Description);
        Assert.IsNull(field.Options);
        Assert.IsFalse(field.IsReadOnly);
    }

    [TestMethod]
    public void PropertyField_Construction_AllParameters()
    {
        var options = new List<PropertyFieldOption>
        {
            new("a", "Alpha"),
            new("b", "Beta")
        };

        var field = new PropertyField(
            "layout",
            "Layout Visibility",
            PropertyFieldType.Select,
            "a",
            Description: "Choose visibility state",
            Options: options,
            IsReadOnly: true);

        Assert.AreEqual("layout", field.Name);
        Assert.AreEqual("Layout Visibility", field.DisplayName);
        Assert.AreEqual(PropertyFieldType.Select, field.FieldType);
        Assert.AreEqual("a", field.CurrentValue);
        Assert.AreEqual("Choose visibility state", field.Description);
        Assert.AreEqual(2, field.Options!.Count);
        Assert.IsTrue(field.IsReadOnly);
    }

    [TestMethod]
    public void PropertyField_NullCurrentValue()
    {
        var field = new PropertyField("key", "Key", PropertyFieldType.Text, null);
        Assert.IsNull(field.CurrentValue);
    }

    [TestMethod]
    public void PropertySection_Construction()
    {
        var fields = new List<PropertyField>
        {
            new("f1", "Field 1", PropertyFieldType.Text, "val")
        };

        var section = new PropertySection("Visibility", "Configure cell visibility", fields);
        Assert.AreEqual("Visibility", section.Title);
        Assert.AreEqual("Configure cell visibility", section.Description);
        Assert.AreEqual(1, section.Fields.Count);
    }

    [TestMethod]
    public void PropertySection_WithNullDescription()
    {
        var section = new PropertySection("General", null, new List<PropertyField>());
        Assert.AreEqual("General", section.Title);
        Assert.IsNull(section.Description);
        Assert.AreEqual(0, section.Fields.Count);
    }
}
