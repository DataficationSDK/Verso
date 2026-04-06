namespace Verso.Abstractions.Tests.Enums;

[TestClass]
public class CellVisibilityEnumTests
{
    [TestMethod]
    public void CellVisibilityHint_HasThreeValues()
    {
        Assert.AreEqual(3, Enum.GetValues<CellVisibilityHint>().Length);
    }

    [TestMethod]
    [DataRow(CellVisibilityHint.Content)]
    [DataRow(CellVisibilityHint.Infrastructure)]
    [DataRow(CellVisibilityHint.OutputOnly)]
    public void CellVisibilityHint_ValueIsDefined(CellVisibilityHint value)
    {
        Assert.IsTrue(Enum.IsDefined(value));
    }

    [TestMethod]
    public void CellVisibilityState_HasFourValues()
    {
        Assert.AreEqual(4, Enum.GetValues<CellVisibilityState>().Length);
    }

    [TestMethod]
    [DataRow(CellVisibilityState.Visible)]
    [DataRow(CellVisibilityState.Hidden)]
    [DataRow(CellVisibilityState.OutputOnly)]
    [DataRow(CellVisibilityState.Collapsed)]
    public void CellVisibilityState_ValueIsDefined(CellVisibilityState value)
    {
        Assert.IsTrue(Enum.IsDefined(value));
    }

    [TestMethod]
    public void PropertyFieldType_HasSevenValues()
    {
        Assert.AreEqual(7, Enum.GetValues<PropertyFieldType>().Length);
    }

    [TestMethod]
    [DataRow(PropertyFieldType.Text)]
    [DataRow(PropertyFieldType.Number)]
    [DataRow(PropertyFieldType.Toggle)]
    [DataRow(PropertyFieldType.Select)]
    [DataRow(PropertyFieldType.MultiSelect)]
    [DataRow(PropertyFieldType.Color)]
    [DataRow(PropertyFieldType.Tags)]
    public void PropertyFieldType_ValueIsDefined(PropertyFieldType value)
    {
        Assert.IsTrue(Enum.IsDefined(value));
    }
}
