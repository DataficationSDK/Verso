using Verso.Serializers;

namespace Verso.Tests.Serializers;

[TestClass]
public sealed class LanguageDirectiveMapTests
{
    [TestMethod]
    public void Aliases_ContainExactExpectedEntries()
    {
        var expected = new Dictionary<string, (string Type, string? Language)>(StringComparer.OrdinalIgnoreCase)
        {
            ["markdown"] = ("markdown", null),
            ["csharp"] = ("code", "csharp"),
            ["cs"] = ("code", "csharp"),
            ["c#"] = ("code", "csharp"),
            ["fsharp"] = ("code", "fsharp"),
            ["fs"] = ("code", "fsharp"),
            ["f#"] = ("code", "fsharp"),
            ["pwsh"] = ("code", "powershell"),
            ["powershell"] = ("code", "powershell"),
            ["python"] = ("code", "python"),
            ["py"] = ("code", "python"),
            ["javascript"] = ("code", "javascript"),
            ["js"] = ("code", "javascript"),
            ["typescript"] = ("code", "typescript"),
            ["ts"] = ("code", "typescript"),
            ["html"] = ("html", null),
            ["mermaid"] = ("mermaid", null),
            ["sql"] = ("sql", null),
            ["value"] = ("code", "value"),
        };

        Assert.AreEqual(expected.Count, LanguageDirectiveMap.Aliases.Count);
        foreach (var (key, value) in expected)
        {
            Assert.IsTrue(LanguageDirectiveMap.Aliases.TryGetValue(key, out var actual), $"Missing alias '{key}'.");
            Assert.AreEqual(value, actual, $"Alias '{key}' maps to an unexpected pair.");
        }
    }

    [TestMethod]
    public void Aliases_AreCaseInsensitive()
    {
        Assert.IsTrue(LanguageDirectiveMap.Aliases.ContainsKey("CSharp"));
        Assert.IsTrue(LanguageDirectiveMap.Aliases.ContainsKey("PY"));
    }

    [TestMethod]
    public void ToFenceTag_KnownPairs_ProduceCanonicalTags()
    {
        Assert.AreEqual("csharp", LanguageDirectiveMap.ToFenceTag("code", "csharp"));
        Assert.AreEqual("powershell", LanguageDirectiveMap.ToFenceTag("code", "powershell"));
        Assert.AreEqual("mermaid", LanguageDirectiveMap.ToFenceTag("mermaid", null));
        Assert.AreEqual("sql", LanguageDirectiveMap.ToFenceTag("sql", null));
    }

    [TestMethod]
    public void ToFenceTag_UnknownPairs_FallBackToLanguageThenType()
    {
        Assert.AreEqual("ruby", LanguageDirectiveMap.ToFenceTag("code", "ruby"));
        Assert.AreEqual("chart", LanguageDirectiveMap.ToFenceTag("chart", null));
    }
}
