namespace Verso.Abstractions.Tests.Enums;

[TestClass]
public class EnumTests
{
    [TestMethod]
    public void ThemeKind_HasThreeValues()
    {
        Assert.AreEqual(3, Enum.GetValues<ThemeKind>().Length);
    }

    [TestMethod]
    public void ToolbarPlacement_HasThreeValues()
    {
        Assert.AreEqual(3, Enum.GetValues<ToolbarPlacement>().Length);
    }

    [TestMethod]
    public void DiagnosticSeverity_HasFourValues()
    {
        Assert.AreEqual(4, Enum.GetValues<DiagnosticSeverity>().Length);
    }

    [TestMethod]
    public void LayoutCapabilities_ValuesArePowersOfTwo()
    {
        var values = Enum.GetValues<LayoutCapabilities>()
            .Where(v => v != LayoutCapabilities.None)
            .Select(v => (int)v)
            .ToList();

        foreach (var val in values)
        {
            Assert.IsTrue((val & (val - 1)) == 0,
                $"LayoutCapabilities value {val} is not a power of 2");
        }
    }

    [TestMethod]
    public void LayoutCapabilities_HasEightValues()
    {
        // None + 7 flags
        Assert.AreEqual(8, Enum.GetValues<LayoutCapabilities>().Length);
    }

    [TestMethod]
    public void LayoutCapabilities_FlagCombination_Works()
    {
        var caps = LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete | LayoutCapabilities.CellExecute;
        Assert.IsTrue(caps.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(caps.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(caps.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsFalse(caps.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsFalse(caps.HasFlag(LayoutCapabilities.MultiSelect));
    }

    [TestMethod]
    public void LayoutCapabilities_None_IsZero()
    {
        Assert.AreEqual(0, (int)LayoutCapabilities.None);
    }
}
