using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ObjectFormatterTests
{
    private readonly ObjectFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    // --- Test types ---

    private class SimpleClass
    {
        public string Name { get; set; } = "hello";
        public int Value { get; set; } = 42;
    }

    private class ClassWithField
    {
        public string Name { get; set; } = "test";
        public string Tag = "public-field";
    }

    private class ClassWithPrivateOnly
    {
        private string Secret { get; set; } = "hidden";
        private int _count = 5;
    }

    private class ClassWithMixedVisibility
    {
        public string Visible { get; set; } = "yes";
        private string Hidden { get; set; } = "no";
        public int Count = 10;
        private int _secret = 99;
    }

    private record TestRecord(string Name, int Value);

    // --- Identity ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.object", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is5()
        => Assert.AreEqual(5, _formatter.Priority);

    [TestMethod]
    public void IsFallback_IsTrue()
        => Assert.IsTrue(_formatter.IsFallback);

    // --- CanFormat ---

    [TestMethod]
    public void CanFormat_ClassWithPublicProperties_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new SimpleClass(), _context));

    [TestMethod]
    public void CanFormat_ClassWithPublicField_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new ClassWithField(), _context));

    [TestMethod]
    public void CanFormat_ClassWithMixedVisibility_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new ClassWithMixedVisibility(), _context));

    [TestMethod]
    public void CanFormat_Record_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new TestRecord("a", 1), _context));

    [TestMethod]
    public void CanFormat_ClassWithPrivateOnly_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new ClassWithPrivateOnly(), _context));

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public void CanFormat_Int_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(42, _context));

    [TestMethod]
    public void CanFormat_Bool_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(true, _context));

    // --- FormatAsync ---

    [TestMethod]
    public async Task FormatAsync_ClassWithProperties_RendersHtmlTable()
    {
        var obj = new SimpleClass { Name = "world", Value = 99 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-obj-tree"));
        Assert.IsTrue(result.Content.Contains("<details"));
        Assert.IsTrue(result.Content.Contains("<summary"));
        Assert.IsTrue(result.Content.Contains("Name"));
        Assert.IsTrue(result.Content.Contains("world"));
        Assert.IsTrue(result.Content.Contains("Value"));
        Assert.IsTrue(result.Content.Contains("99"));
    }

    [TestMethod]
    public async Task FormatAsync_ClassWithField_IncludesPublicField()
    {
        var obj = new ClassWithField { Name = "test", Tag = "my-tag" };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Tag"));
        Assert.IsTrue(result.Content.Contains("my-tag"));
    }

    [TestMethod]
    public async Task FormatAsync_MixedVisibility_ShowsOnlyPublicMembers()
    {
        var obj = new ClassWithMixedVisibility();
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Visible"));
        Assert.IsTrue(result.Content.Contains("Count"));
        Assert.IsFalse(result.Content.Contains("Hidden"));
        Assert.IsFalse(result.Content.Contains("_secret"));
    }

    [TestMethod]
    public async Task FormatAsync_HtmlEncodesValues()
    {
        var obj = new SimpleClass { Name = "<script>alert('xss')</script>", Value = 1 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsFalse(result.Content.Contains("<script>"));
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public async Task FormatAsync_NullPropertyValue_RendersEmpty()
    {
        var obj = new SimpleClass { Name = null!, Value = 0 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("verso-obj-null"));
    }

    [TestMethod]
    public async Task FormatAsync_Record_RendersProperties()
    {
        var obj = new TestRecord("Alice", 30);
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Alice"));
        Assert.IsTrue(result.Content.Contains("30"));
    }

    // --- VERSO-007: framework type explosion ---

    /// <summary>
    /// A class that exposes a System.Type property, mimicking the
    /// DataFrameColumn.DataType scenario from VERSO-007. Without the
    /// framework-type opacity fix, the Type property expands into ~40
    /// properties including Assembly, which exposes DefinedTypes
    /// containing potentially thousands of types.
    /// </summary>
    private class TypeHolder
    {
        public string Name { get; set; } = "test";
        public Type DataType { get; set; } = typeof(int);
    }

    [TestMethod]
    public async Task FormatAsync_FrameworkTypeAtDepth2_DoesNotExpandReflectionInternals()
    {
        var obj = new TypeHolder();
        var result = await _formatter.FormatAsync(obj, _context);

        // Depth 0: TypeHolder expands (user type)
        // Depth 1: DataType (System.Type) is still expandable, so member names
        //          like "Assembly" appear as labels
        // Depth 2+: System.Type's member VALUES (Assembly, etc.) are opaque,
        //          rendered as ToString() rather than expanded further
        //
        // The explosion path is Type to Assembly to DefinedTypes, thousands of
        // types with 40 properties each. DefinedTypes only appears if Assembly's
        // value is expanded at depth 2, which the fix prevents.
        Assert.IsFalse(result.Content.Contains("DefinedTypes"),
            "Reflection internals like DefinedTypes should not appear, the Assembly value should be opaque at depth 2 (VERSO-007)");

        // The user's own properties should still be present
        Assert.IsTrue(result.Content.Contains("Name"), "User property Name should be present");
        Assert.IsTrue(result.Content.Contains("DataType"), "User property DataType should be present");
    }

    [TestMethod]
    public async Task FormatAsync_FrameworkTypeAtDepth1_StillExpandable()
    {
        // At depth 1, framework types should still be expandable so the
        // user can see their immediate properties. The opacity kicks in
        // at depth 2+ to prevent the combinatorial explosion.
        var obj = new TypeHolder();
        var result = await _formatter.FormatAsync(obj, _context);

        // DataType is at depth 1, so it should render as an expandable node
        // rather than a flat ToString().
        Assert.IsTrue(result.Content.Contains("DataType"),
            "DataType property should be present at depth 1");

        // "AssemblyQualifiedName" is a member label that can only appear inside
        // an expanded System.Type node, so it proves the node expanded. A looser
        // check such as Contains("Type") would pass even when the node is
        // opaque, because "Type" is a substring of "DataType" and "TypeHolder".
        Assert.IsTrue(result.Content.Contains("AssemblyQualifiedName"),
            "System.Type at depth 1 should expand its own members, not be rendered opaquely");
    }

    /// <summary>
    /// A class with a nested collection, to verify that collections still
    /// expand at all depths even though they live in System.* namespaces.
    /// Without the collection-first ordering, List<string> would be hidden
    /// by the framework-type opacity check at depth 2+.
    /// </summary>
    private class CollectionHolder
    {
        public string Name { get; set; } = "test";
        public List<string> Tags { get; set; } = new() { "apple", "banana", "cherry" };
    }

    [TestMethod]
    public async Task FormatAsync_NestedCollectionAtDepth1_StillShowsItems()
    {
        var obj = new CollectionHolder();
        var result = await _formatter.FormatAsync(obj, _context);

        // The collection items should appear, not just the type name
        Assert.IsTrue(result.Content.Contains("apple"), "Collection items should be visible");
        Assert.IsTrue(result.Content.Contains("banana"), "Collection items should be visible");
        Assert.IsTrue(result.Content.Contains("cherry"), "Collection items should be visible");
    }

    [TestMethod]
    public async Task FormatAsync_NestedCollectionAtDepth2_StillShowsItems()
    {
        // Collection nested two levels deep still needs to show items.
        // The framework-type opacity must not hide collections.
        var nested = new
        {
            Outer = "value",
            Inner = new CollectionHolder()
        };
        var result = await _formatter.FormatAsync(nested, _context);

        Assert.IsTrue(result.Content.Contains("apple"),
            "Collection items should be visible even at depth 2+");
        Assert.IsTrue(result.Content.Contains("banana"),
            "Collection items should be visible even at depth 2+");
    }

    /// <summary>
    /// Two properties holding the same shared Type instance (e.g. typeof(int)
    /// is always the same object in memory). Both should render their ToString()
    /// value, not show [circular reference] on the second one.
    /// </summary>
    private class SharedTypeHolder
    {
        public Type First { get; set; } = typeof(int);
        public Type Second { get; set; } = typeof(int);
    }

    [TestMethod]
    public async Task FormatAsync_SharedFrameworkTypeInstance_NoCircularReference()
    {
        var obj = new SharedTypeHolder();
        var result = await _formatter.FormatAsync(obj, _context);

        // Both properties hold typeof(int), the same object in memory.
        // The second occurrence must NOT show [circular reference].
        Assert.IsFalse(result.Content.Contains("[circular reference]"),
            "Shared framework type instances should not trigger circular reference detection");

        // Both should show System.Int32 somewhere in the output
        Assert.IsTrue(result.Content.Contains("System.Int32"),
            "Both Type properties should render their ToString() value");
    }

    /// <summary>
    /// A dictionary nested deep enough to be affected by the framework-graph
    /// rule. Dictionary entries are KeyValuePair, which lives in System.*, so
    /// any rule based on element type would wrongly hide user dictionaries.
    /// </summary>
    private class DictionaryHolder
    {
        public string Label { get; set; } = "config";
        public Dictionary<string, object> Settings { get; set; } = new()
        {
            ["timeout"] = 30,
            ["retries"] = 5
        };
    }

    [TestMethod]
    public async Task FormatAsync_UserDictionaryAtDepth2_StillShowsEntries()
    {
        var nested = new
        {
            Outer = "value",
            Inner = new DictionaryHolder()
        };
        var result = await _formatter.FormatAsync(nested, _context);

        Assert.IsTrue(result.Content.Contains("timeout"),
            "User dictionary keys should be visible at depth 2+");
        Assert.IsTrue(result.Content.Contains("retries"),
            "User dictionary keys should be visible at depth 2+");
        Assert.IsTrue(result.Content.Contains("30"),
            "User dictionary values should be visible at depth 2+");
    }

    [TestMethod]
    public async Task FormatAsync_TypeMember_ProducesBoundedOutput()
    {
        // Asserting the absence of a single member name is not enough to know
        // the output budget is respected: reflection metadata is also reachable
        // through collection properties such as Type.CustomAttributes, which
        // kept the graph fanning out while a name-based assertion still passed.
        // Size is the property that actually matters here.
        var obj = new TypeHolder();
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Length < 100_000,
            $"An object holding one System.Type should render far below the {ObjectTreeRenderer.MaxOutputSize} byte cap, was {result.Content.Length}");
    }

    [TestMethod]
    public async Task FormatAsync_BareTypeValue_ProducesBoundedOutput()
    {
        // A Type rendered as the root still expands at depth 0 and 1 by design,
        // so its own members and their collections are the widest case.
        var result = await _formatter.FormatAsync(typeof(int), _context);

        Assert.IsTrue(result.Content.Length < 100_000,
            $"A bare System.Type should render far below the {ObjectTreeRenderer.MaxOutputSize} byte cap, was {result.Content.Length}");
    }
}
