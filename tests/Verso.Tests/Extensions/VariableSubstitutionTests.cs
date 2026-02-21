using Verso.Abstractions;
using Verso.Extensions.Utilities;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class VariableSubstitutionTests
{
    private readonly StubExecutionContext _context = new();

    [TestMethod]
    public void Apply_SimpleSubstitution_ReplacesVariable()
    {
        _context.Variables.Set("name", "World");
        var result = VariableSubstitution.Apply("Hello @name!", _context.Variables);
        Assert.AreEqual("Hello World!", result);
    }

    [TestMethod]
    public void Apply_MultipleVariables_ReplacesAll()
    {
        _context.Variables.Set("first", "John");
        _context.Variables.Set("last", "Doe");
        var result = VariableSubstitution.Apply("@first @last", _context.Variables);
        Assert.AreEqual("John Doe", result);
    }

    [TestMethod]
    public void Apply_UnresolvedVariable_LeavesAsIs()
    {
        var result = VariableSubstitution.Apply("Hello @missing", _context.Variables);
        Assert.AreEqual("Hello @missing", result);
    }

    [TestMethod]
    public void Apply_CaseInsensitive_MatchesVariable()
    {
        _context.Variables.Set("Name", "World");
        var result = VariableSubstitution.Apply("Hello @name!", _context.Variables);
        Assert.AreEqual("Hello World!", result);
    }

    [TestMethod]
    public void Apply_DoubleAtEscape_ProducesLiteralAt()
    {
        var result = VariableSubstitution.Apply("email@@example.com", _context.Variables);
        Assert.AreEqual("email@example.com", result);
    }

    [TestMethod]
    public void Apply_EmptySource_ReturnsEmpty()
    {
        var result = VariableSubstitution.Apply("", _context.Variables);
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void Apply_NullSource_ReturnsNull()
    {
        var result = VariableSubstitution.Apply(null!, _context.Variables);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Apply_NoVariables_ReturnsOriginal()
    {
        var result = VariableSubstitution.Apply("No variables here", _context.Variables);
        Assert.AreEqual("No variables here", result);
    }

    [TestMethod]
    public void FindUnresolved_ReturnsUnresolvedPositions()
    {
        _context.Variables.Set("known", "value");
        var results = VariableSubstitution.FindUnresolved("@known and @unknown", _context.Variables);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("unknown", results[0].Name);
    }

    [TestMethod]
    public void FindUnresolved_AllResolved_ReturnsEmpty()
    {
        _context.Variables.Set("a", "1");
        _context.Variables.Set("b", "2");
        var results = VariableSubstitution.FindUnresolved("@a @b", _context.Variables);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void FindUnresolved_SkipsDoubleAtEscape()
    {
        var results = VariableSubstitution.FindUnresolved("@@escape @missing", _context.Variables);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("missing", results[0].Name);
    }

    [TestMethod]
    public void FindUnresolved_EmptySource_ReturnsEmpty()
    {
        var results = VariableSubstitution.FindUnresolved("", _context.Variables);
        Assert.AreEqual(0, results.Count);
    }
}
