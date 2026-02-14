using Verso.Contexts;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class VariableStoreChangeNotificationTests
{
    [TestMethod]
    public void Set_FiresOnVariablesChanged()
    {
        var store = new VariableStore();
        var fired = false;
        store.OnVariablesChanged += () => fired = true;

        store.Set("x", 42);

        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void Remove_WhenFound_FiresOnVariablesChanged()
    {
        var store = new VariableStore();
        store.Set("x", 42);

        var fired = false;
        store.OnVariablesChanged += () => fired = true;

        var removed = store.Remove("x");

        Assert.IsTrue(removed);
        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void Remove_WhenNotFound_DoesNotFire()
    {
        var store = new VariableStore();
        var fired = false;
        store.OnVariablesChanged += () => fired = true;

        var removed = store.Remove("nonexistent");

        Assert.IsFalse(removed);
        Assert.IsFalse(fired);
    }

    [TestMethod]
    public void Clear_FiresOnVariablesChanged()
    {
        var store = new VariableStore();
        store.Set("x", 1);

        var fired = false;
        store.OnVariablesChanged += () => fired = true;

        store.Clear();

        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void Set_FiresMultipleTimes()
    {
        var store = new VariableStore();
        var count = 0;
        store.OnVariablesChanged += () => count++;

        store.Set("a", 1);
        store.Set("b", 2);
        store.Set("a", 3); // overwrite

        Assert.AreEqual(3, count);
    }
}
