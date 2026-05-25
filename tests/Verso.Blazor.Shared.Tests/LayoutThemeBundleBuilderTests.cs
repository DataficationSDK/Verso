namespace Verso.Blazor.Shared.Tests;

[TestClass]
public class LayoutThemeBundleBuilderTests
{
    private static readonly string[] RequiredTokenKeys =
    {
        "bg.default",
        "bg.elevated",
        "fg.default",
        "fg.muted",
        "border.default",
        "accent",
        "font.family.mono",
        "font.family.sans",
        "font.size.base",
    };

    private static ThemeData MakeThemeData(
        string bgDefault = "#101010",
        string bgElevated = "#202020",
        string fgDefault = "#FAFAFA",
        string fgMuted = "#999999",
        string borderDefault = "#333333",
        string accent = "#FF8800",
        string fontFamilyMono = "TestMono, monospace",
        string fontFamilySans = "TestSans, sans-serif",
        double fontSizeBase = 14.5)
    {
        var colors = new ThemeColorTokens
        {
            BgDefault = bgDefault,
            BgElevated = bgElevated,
            FgDefault = fgDefault,
            FgMuted = fgMuted,
            BorderDefault = borderDefault,
            Accent = accent,
        };
        var typography = new ThemeTypography
        {
            FontFamilyMono = fontFamilyMono,
            FontFamilySans = fontFamilySans,
            FontSizeBase = fontSizeBase,
        };
        return new ThemeData(colors, typography, new ThemeSpacing());
    }

    [TestMethod]
    public void Build_NullThemeData_ReturnsNull()
    {
        Assert.IsNull(LayoutThemeBundleBuilder.Build(ThemeKind.Dark, null));
    }

    [TestMethod]
    public void Build_PopulatesEveryDocumentedToken()
    {
        var bundle = LayoutThemeBundleBuilder.Build(ThemeKind.Light, MakeThemeData());
        Assert.IsNotNull(bundle);

        foreach (var key in RequiredTokenKeys)
        {
            Assert.IsTrue(
                bundle!.Tokens.ContainsKey(key),
                $"Bundle is missing required token '{key}'.");
            Assert.IsFalse(
                string.IsNullOrEmpty(bundle.Tokens[key]),
                $"Token '{key}' must have a non-empty value.");
        }
    }

    [TestMethod]
    public void Build_TokensReflectThemeDataValues()
    {
        var data = MakeThemeData(
            bgDefault: "#111213",
            fgDefault: "#EEEFF0",
            accent: "#33CCFF",
            fontFamilyMono: "Fira Code, monospace");

        var bundle = LayoutThemeBundleBuilder.Build(ThemeKind.Dark, data);
        Assert.IsNotNull(bundle);

        Assert.AreEqual("#111213", bundle!.Tokens["bg.default"]);
        Assert.AreEqual("#EEEFF0", bundle.Tokens["fg.default"]);
        Assert.AreEqual("#33CCFF", bundle.Tokens["accent"]);
        Assert.AreEqual("Fira Code, monospace", bundle.Tokens["font.family.mono"]);
    }

    [TestMethod]
    public void Build_FontSizeBase_HasPxSuffix()
    {
        var bundle = LayoutThemeBundleBuilder.Build(
            ThemeKind.Light,
            MakeThemeData(fontSizeBase: 14.0));
        Assert.IsNotNull(bundle);

        var value = bundle!.Tokens["font.size.base"];
        StringAssert.EndsWith(value, "px");

        var numeric = value.Substring(0, value.Length - 2);
        Assert.IsTrue(
            double.TryParse(
                numeric,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed),
            $"Token 'font.size.base' numeric portion '{numeric}' should parse as a double.");
        Assert.AreEqual(14.0, parsed, 0.0001);
    }

    [TestMethod]
    public void Build_FontSizeBase_UsesInvariantCulture()
    {
        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            var bundle = LayoutThemeBundleBuilder.Build(
                ThemeKind.Light,
                MakeThemeData(fontSizeBase: 14.5));
            Assert.IsNotNull(bundle);
            Assert.AreEqual("14.5px", bundle!.Tokens["font.size.base"]);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Build_KindLight_MapsToLowercaseLight()
    {
        var bundle = LayoutThemeBundleBuilder.Build(ThemeKind.Light, MakeThemeData());
        Assert.IsNotNull(bundle);
        Assert.AreEqual("light", bundle!.Kind);
    }

    [TestMethod]
    public void Build_KindDark_MapsToLowercaseDark()
    {
        var bundle = LayoutThemeBundleBuilder.Build(ThemeKind.Dark, MakeThemeData());
        Assert.IsNotNull(bundle);
        Assert.AreEqual("dark", bundle!.Kind);
    }

    [TestMethod]
    public void Build_KindHighContrast_MapsToKebabCase()
    {
        var bundle = LayoutThemeBundleBuilder.Build(ThemeKind.HighContrast, MakeThemeData());
        Assert.IsNotNull(bundle);
        Assert.AreEqual("high-contrast", bundle!.Kind);
    }

    [TestMethod]
    public void Build_KindNull_DefaultsToLight()
    {
        var bundle = LayoutThemeBundleBuilder.Build(null, MakeThemeData());
        Assert.IsNotNull(bundle);
        Assert.AreEqual("light", bundle!.Kind);
    }

    [TestMethod]
    public void KindToWire_ExposesSameMapping()
    {
        Assert.AreEqual("light", LayoutThemeBundleBuilder.KindToWire(ThemeKind.Light));
        Assert.AreEqual("dark", LayoutThemeBundleBuilder.KindToWire(ThemeKind.Dark));
        Assert.AreEqual("high-contrast", LayoutThemeBundleBuilder.KindToWire(ThemeKind.HighContrast));
        Assert.AreEqual("light", LayoutThemeBundleBuilder.KindToWire(null));
    }
}
