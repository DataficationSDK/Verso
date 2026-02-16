namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class SettingDefinitionTests
{
    [TestMethod]
    public void Constructor_SetsRequiredProperties()
    {
        var def = new SettingDefinition(
            "warningLevel",
            "Warning Level",
            "Sets the compiler warning level.",
            SettingType.Integer);

        Assert.AreEqual("warningLevel", def.Name);
        Assert.AreEqual("Warning Level", def.DisplayName);
        Assert.AreEqual("Sets the compiler warning level.", def.Description);
        Assert.AreEqual(SettingType.Integer, def.SettingType);
    }

    [TestMethod]
    public void Constructor_DefaultValues()
    {
        var def = new SettingDefinition(
            "name", "Name", "Desc", SettingType.String);

        Assert.IsNull(def.DefaultValue);
        Assert.IsNull(def.Category);
        Assert.IsNull(def.Constraints);
        Assert.AreEqual(0, def.Order);
    }

    [TestMethod]
    public void Constructor_WithAllOptionalProperties()
    {
        var constraints = new SettingConstraints(MinValue: 0, MaxValue: 5);
        var def = new SettingDefinition(
            "warningLevel",
            "Warning Level",
            "Sets the compiler warning level.",
            SettingType.Integer,
            DefaultValue: 4,
            Category: "Compiler",
            Constraints: constraints,
            Order: 10);

        Assert.AreEqual(4, def.DefaultValue);
        Assert.AreEqual("Compiler", def.Category);
        Assert.AreSame(constraints, def.Constraints);
        Assert.AreEqual(10, def.Order);
    }

    [TestMethod]
    public void SettingConstraints_DefaultsToAllNull()
    {
        var constraints = new SettingConstraints();

        Assert.IsNull(constraints.MinValue);
        Assert.IsNull(constraints.MaxValue);
        Assert.IsNull(constraints.Pattern);
        Assert.IsNull(constraints.Choices);
        Assert.IsNull(constraints.MaxLength);
        Assert.IsNull(constraints.MaxItems);
    }

    [TestMethod]
    public void SettingConstraints_WithChoices()
    {
        var choices = new List<string> { "strict", "normal", "relaxed" };
        var constraints = new SettingConstraints(Choices: choices);

        Assert.IsNotNull(constraints.Choices);
        Assert.AreEqual(3, constraints.Choices.Count);
        Assert.AreEqual("strict", constraints.Choices[0]);
    }

    [TestMethod]
    public void SettingConstraints_WithNumericRange()
    {
        var constraints = new SettingConstraints(MinValue: 0, MaxValue: 100);

        Assert.AreEqual(0, constraints.MinValue);
        Assert.AreEqual(100, constraints.MaxValue);
    }

    [TestMethod]
    public void SettingType_AllValuesAreDefined()
    {
        var values = Enum.GetValues<SettingType>();
        Assert.AreEqual(6, values.Length);
        CollectionAssert.Contains(values, SettingType.String);
        CollectionAssert.Contains(values, SettingType.Integer);
        CollectionAssert.Contains(values, SettingType.Double);
        CollectionAssert.Contains(values, SettingType.Boolean);
        CollectionAssert.Contains(values, SettingType.StringChoice);
        CollectionAssert.Contains(values, SettingType.StringList);
    }

    [TestMethod]
    public void SettingDefinition_RecordEquality()
    {
        var a = new SettingDefinition("x", "X", "desc", SettingType.Boolean, true);
        var b = new SettingDefinition("x", "X", "desc", SettingType.Boolean, true);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SettingDefinition_WithModification()
    {
        var original = new SettingDefinition("x", "X", "desc", SettingType.Integer, 5);
        var modified = original with { DefaultValue = 10 };

        Assert.AreEqual(5, original.DefaultValue);
        Assert.AreEqual(10, modified.DefaultValue);
        Assert.AreEqual(original.Name, modified.Name);
    }
}
