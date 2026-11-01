namespace Verso.Blazor.Shared.Tests;

[TestClass]
public class MonacoThemeBuilderTests
{
    private static ThemeData MakeThemeData(
        ThemeColorTokens? colors = null,
        IReadOnlyDictionary<string, string>? syntax = null) =>
        new(colors ?? new ThemeColorTokens(), new ThemeTypography(), new ThemeSpacing(), null, syntax);

    [TestMethod]
    public void Build_NullThemeData_ReturnsNull()
    {
        Assert.IsNull(MonacoThemeBuilder.Build(ThemeKind.Dark, null));
    }

    [TestMethod]
    public void Build_TakesTheEditorSurfaceFromTheThemeTokens()
    {
        var colors = new ThemeColorTokens
        {
            EditorBackground = "#303446",
            EditorForeground = "#C6D0F5",
            EditorSelection = "#626880",
        };

        var spec = MonacoThemeBuilder.Build(ThemeKind.Dark, MakeThemeData(colors))!;

        Assert.AreEqual("vs-dark", spec.Base);
        Assert.AreEqual("#303446", spec.Colors["editor.background"]);
        Assert.AreEqual("#C6D0F5", spec.Colors["editor.foreground"]);
        Assert.AreEqual("#626880", spec.Colors["editor.selectionBackground"]);
    }

    [TestMethod]
    public void Build_MapsSyntaxColorsOntoEditorTokens()
    {
        var syntax = new Dictionary<string, string>
        {
            ["keyword"] = "#CA9EE6",
            ["punctuation"] = "#949CBB",
            ["preprocessor"] = "#E5C890",
        };

        var spec = MonacoThemeBuilder.Build(ThemeKind.Dark, MakeThemeData(syntax: syntax))!;

        Assert.AreEqual("#CA9EE6", spec.Rules.Single(r => r.Token == "keyword").Foreground);
        // The editor's tokenizers call punctuation "delimiter".
        Assert.AreEqual("#949CBB", spec.Rules.Single(r => r.Token == "delimiter").Foreground);
        // A preprocessor line reaches the editor under a keyword token of its own, which
        // has to outrank the plain keyword rule rather than be swallowed by it.
        Assert.AreEqual("#E5C890", spec.Rules.Single(r => r.Token == "keyword.preprocessor").Foreground);
    }

    [TestMethod]
    public void Build_ColorsSemanticTokensByTheirThemeKey()
    {
        var syntax = new Dictionary<string, string>
        {
            ["type"] = "#4EC9B0",
            ["function"] = "#DCDCAA",
            ["variable"] = "#9CDCFE",
            ["enumMember"] = "#4FC1FF",
        };

        var spec = MonacoThemeBuilder.Build(ThemeKind.Dark, MakeThemeData(syntax: syntax))!;

        // The kernels' semantic tokens use the standard names, which a theme does not spell out.
        foreach (var token in new[] { "class", "struct", "enum", "interface", "typeParameter" })
            Assert.AreEqual("#4EC9B0", spec.Rules.Single(r => r.Token == token).Foreground, token);
        Assert.AreEqual("#DCDCAA", spec.Rules.Single(r => r.Token == "method").Foreground);
        foreach (var token in new[] { "parameter", "property", "event" })
            Assert.AreEqual("#9CDCFE", spec.Rules.Single(r => r.Token == token).Foreground, token);
        Assert.AreEqual("#4FC1FF", spec.Rules.Single(r => r.Token == "enumMember").Foreground);
    }

    [TestMethod]
    public void Build_ThemeWithoutEnumMember_ColorsEnumMembersAsTypes()
    {
        var syntax = new Dictionary<string, string>
        {
            ["type"] = "#4EC9B0",
            ["variable"] = "#9CDCFE",
        };

        var spec = MonacoThemeBuilder.Build(ThemeKind.Dark, MakeThemeData(syntax: syntax))!;

        Assert.AreEqual("#4EC9B0", spec.Rules.Single(r => r.Token == "enumMember").Foreground);
    }

    [TestMethod]
    public void Build_WithoutSyntaxColors_LeavesTheRulesToTheBaseTheme()
    {
        var spec = MonacoThemeBuilder.Build(ThemeKind.Light, MakeThemeData())!;

        Assert.AreEqual("vs", spec.Base);
        Assert.AreEqual(0, spec.Rules.Count);
    }

    [TestMethod]
    public void Build_SkipsSyntaxKeysTheEditorHasNoTokenFor()
    {
        var syntax = new Dictionary<string, string> { ["made-up"] = "#123456", ["string"] = " " };

        var spec = MonacoThemeBuilder.Build(ThemeKind.Light, MakeThemeData(syntax: syntax))!;

        Assert.AreEqual(0, spec.Rules.Count);
    }

    [DataTestMethod]
    [DataRow("#000000", "hc-black")]
    [DataRow("#FFFFFF", "hc-light")]
    [DataRow("#FFF", "hc-light")]
    [DataRow("not a color", "hc-black")]
    public void Build_HighContrast_PicksTheBaseFromTheEditorBackground(string background, string expected)
    {
        var colors = new ThemeColorTokens { EditorBackground = background };

        var spec = MonacoThemeBuilder.Build(ThemeKind.HighContrast, MakeThemeData(colors))!;

        Assert.AreEqual(expected, spec.Base);
    }
}
