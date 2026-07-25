using Verso.Python.PackageManagement;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers the interpreter-floor check. The bar is deliberately low: it warns about a mismatch it
/// is certain of and stays quiet otherwise, because a wrong warning is worse than none.
/// </summary>
[TestClass]
public sealed class RequiresPythonTests
{
    private static readonly Version Running = new(3, 13, 3);

    [TestMethod]
    public void Satisfied_WhenTheFloorIsBelowTheRunningVersion()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.9", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.13", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied(">3.12", Running));
    }

    [TestMethod]
    public void NotSatisfied_WhenTheFloorIsAboveTheRunningVersion()
    {
        Assert.IsFalse(RequiresPython.IsSatisfied(">=3.14", Running));
        Assert.IsFalse(RequiresPython.IsSatisfied(">3.13.3", Running));
    }

    [TestMethod]
    public void Satisfied_ComparesAThreePartFloorProperly()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.13.3", Running));
        Assert.IsFalse(RequiresPython.IsSatisfied(">=3.13.4", Running));
    }

    [TestMethod]
    public void Satisfied_HonorsACeiling()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied("<4", Running));
        Assert.IsFalse(RequiresPython.IsSatisfied("<3.13", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied("<=3.13.3", Running));
    }

    [TestMethod]
    public void Satisfied_HandlesAFloorAndCeilingTogether()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.9,<4", Running));
        Assert.IsFalse(RequiresPython.IsSatisfied(">=3.9,<3.13", Running));
    }

    [TestMethod]
    public void Satisfied_HandlesTheCompatibleReleaseOperator()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied("~=3.9", Running));
        Assert.IsFalse(RequiresPython.IsSatisfied("~=4.0", Running));
    }

    [TestMethod]
    public void Satisfied_WhenTheSpecifierCannotBeRead()
    {
        // Equality forms carry wildcard and pre-release rules a partial reading would get
        // wrong, so they are not judged at all.
        Assert.IsTrue(RequiresPython.IsSatisfied("==3.9.*", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied("!=3.13", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied("nonsense", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied(">=", Running));
    }

    [TestMethod]
    public void Satisfied_WhenThereIsNoSpecifier()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied(null, Running));
        Assert.IsTrue(RequiresPython.IsSatisfied("", Running));
        Assert.IsTrue(RequiresPython.IsSatisfied("   ", Running));
    }

    [TestMethod]
    public void Satisfied_IgnoresAPreReleaseSuffixOnTheFloor()
    {
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.13.0rc1", Running));
    }

    [TestMethod]
    public void Satisfied_TreatsAMinorFloorAsItsFirstRelease()
    {
        // ">=3.13" must not lose to 3.13.0 on an unset patch field.
        Assert.IsTrue(RequiresPython.IsSatisfied(">=3.13", new Version(3, 13)));
    }
}
