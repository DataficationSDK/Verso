using System.Reflection;

namespace Verso.Abstractions.Tests.Attributes;

[VersoExtension]
public class FakeExtension { }

[TestClass]
public class VersoExtensionAttributeTests
{
    [TestMethod]
    public void CanBeDiscoveredViaReflection()
    {
        var attr = typeof(FakeExtension).GetCustomAttribute<VersoExtensionAttribute>();
        Assert.IsNotNull(attr);
    }

    [TestMethod]
    public void TargetsClassOnly()
    {
        var usage = typeof(VersoExtensionAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.IsNotNull(usage);
        Assert.AreEqual(AttributeTargets.Class, usage!.ValidOn);
    }

    [TestMethod]
    public void InheritedIsFalse()
    {
        var usage = typeof(VersoExtensionAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.IsNotNull(usage);
        Assert.IsFalse(usage!.Inherited);
    }

    [TestMethod]
    public void AllowMultipleIsFalse()
    {
        var usage = typeof(VersoExtensionAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.IsNotNull(usage);
        Assert.IsFalse(usage!.AllowMultiple);
    }

    [TestMethod]
    public void AttributeIsSealed()
    {
        Assert.IsTrue(typeof(VersoExtensionAttribute).IsSealed);
    }
}
