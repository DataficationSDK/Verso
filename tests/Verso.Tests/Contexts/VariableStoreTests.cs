using Verso.Contexts;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class VariableStoreTests
{
    private VariableStore _store = null!;

    [TestInitialize]
    public void Setup() => _store = new VariableStore();

    [TestMethod]
    public void Set_And_Get_Returns_Value()
    {
        _store.Set("x", 42);
        Assert.AreEqual(42, _store.Get<int>("x"));
    }

    [TestMethod]
    public void Get_Missing_Returns_Default()
    {
        Assert.AreEqual(0, _store.Get<int>("missing"));
        Assert.IsNull(_store.Get<string>("missing"));
    }

    [TestMethod]
    public void Get_TypeMismatch_Returns_Default()
    {
        _store.Set("x", "hello");
        Assert.AreEqual(0, _store.Get<int>("x"));
    }

    [TestMethod]
    public void Set_Overwrites_Existing()
    {
        _store.Set("x", 1);
        _store.Set("x", 2);
        Assert.AreEqual(2, _store.Get<int>("x"));
    }

    [TestMethod]
    public void TryGet_Returns_True_When_Found()
    {
        _store.Set("x", "hello");
        Assert.IsTrue(_store.TryGet<string>("x", out var value));
        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public void TryGet_Returns_False_When_Missing()
    {
        Assert.IsFalse(_store.TryGet<int>("missing", out var value));
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void TryGet_Returns_False_On_TypeMismatch()
    {
        _store.Set("x", "hello");
        Assert.IsFalse(_store.TryGet<int>("x", out var value));
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void GetAll_Returns_All_Variables()
    {
        _store.Set("a", 1);
        _store.Set("b", "two");
        var all = _store.GetAll();
        Assert.AreEqual(2, all.Count);
        Assert.IsTrue(all.Any(v => v.Name == "a" && (int)v.Value! == 1));
        Assert.IsTrue(all.Any(v => v.Name == "b" && (string)v.Value! == "two"));
    }

    [TestMethod]
    public void GetAll_Returns_Correct_Types()
    {
        _store.Set("x", 3.14);
        var all = _store.GetAll();
        Assert.AreEqual(typeof(double), all[0].Type);
    }

    [TestMethod]
    public void Remove_Existing_Returns_True()
    {
        _store.Set("x", 42);
        Assert.IsTrue(_store.Remove("x"));
        Assert.AreEqual(0, _store.Get<int>("x"));
    }

    [TestMethod]
    public void Remove_Missing_Returns_False()
    {
        Assert.IsFalse(_store.Remove("missing"));
    }

    [TestMethod]
    public void Clear_Removes_All()
    {
        _store.Set("a", 1);
        _store.Set("b", 2);
        _store.Clear();
        Assert.AreEqual(0, _store.GetAll().Count);
    }

    [TestMethod]
    public void Set_NullName_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _store.Set(null!, "value"));
    }

    [TestMethod]
    public void Set_NullValue_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _store.Set("x", null!));
    }

    [TestMethod]
    public void Get_NullName_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _store.Get<int>(null!));
    }

    [TestMethod]
    public void Remove_NullName_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _store.Remove(null!));
    }

    [TestMethod]
    public void ConcurrentAccess_Does_Not_Throw()
    {
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _store.Set($"var_{i}", i);
            _store.Get<int>($"var_{i}");
            _store.TryGet<int>($"var_{i}", out _);
            _store.GetAll();
        }));
        Task.WaitAll(tasks.ToArray());

        var all = _store.GetAll();
        Assert.AreEqual(100, all.Count);
    }

    [TestMethod]
    public void CaseInsensitive_Lookup()
    {
        _store.Set("MyVar", 42);
        Assert.AreEqual(42, _store.Get<int>("myvar"));
        Assert.AreEqual(42, _store.Get<int>("MYVAR"));
    }
}
