# Theme Authoring Guide

This guide explains how to create custom themes for Verso notebooks. Themes control colors, typography, spacing, and syntax highlighting across the entire UI.

## Introduction

A Verso theme implements the `ITheme` interface and provides a complete visual definition for the notebook UI. The platform ships three built-in themes (`VersoLightTheme`, `VersoDarkTheme`, `VersoHighContrastTheme`) and supports third-party themes loaded via the extension system.

Themes are purely declarative: they return color tokens, font descriptors, and spacing values. The rendering layer (Blazor, VS Code webview) consumes these tokens to style the UI.

## Quick Start

1. Create a new extension project (or use an existing one):

```bash
dotnet new verso-extension -n MyTheme --extensionId com.mycompany.mytheme
```

2. Add a class implementing `ITheme` with the `[VersoExtension]` attribute:

```csharp
using Verso.Abstractions;

[VersoExtension]
public sealed class SolarizedDarkTheme : ITheme
{
    public string ExtensionId => "com.mycompany.solarized-dark";
    public string Name => "Solarized Dark";
    public string Version => "1.0.0";
    public string? Author => "Your Name";
    public string? Description => "Solarized Dark theme for Verso notebooks.";

    public string ThemeId => "solarized-dark";
    public string DisplayName => "Solarized Dark";
    public ThemeKind ThemeKind => ThemeKind.Dark;

    public ThemeColorTokens Colors { get; } = new ThemeColorTokens
    {
        EditorBackground = "#002B36",
        EditorForeground = "#839496",
        // ... set all tokens
    };

    public ThemeTypography Typography { get; } = new();
    public ThemeSpacing Spacing { get; } = new();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string? GetCustomToken(string key) => null;

    public SyntaxColorMap GetSyntaxColors()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#859900");
        // ... set all 12 standard tokens
        return map;
    }
}
```

3. Build and test your theme with the high-contrast theme tests as a pattern.

## Token Reference: ThemeColorTokens

All values are CSS hex color strings (e.g. `#FFFFFF`). The table below groups all 45 tokens by category, showing the light theme defaults.

### Editor Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `EditorBackground` | `#FFFFFF` | Background of code editor regions |
| `EditorForeground` | `#1E1E1E` | Default text color in editors |
| `EditorLineNumber` | `#858585` | Line number label color |
| `EditorCursor` | `#000000` | Blinking text cursor color |
| `EditorSelection` | `#ADD6FF` | Selected text background |
| `EditorGutter` | `#F5F5F5` | Editor gutter background |
| `EditorWhitespace` | `#D3D3D3` | Whitespace indicator color |

### Cell Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `CellBackground` | `#FFFFFF` | Default cell background |
| `CellBorder` | `#E0E0E0` | Inactive cell border |
| `CellActiveBorder` | `#0078D4` | Focused cell border |
| `CellHoverBackground` | `#F8F8F8` | Cell hover background |
| `CellOutputBackground` | `#F5F5F5` | Cell output region background |
| `CellOutputForeground` | `#1E1E1E` | Cell output text color |
| `CellErrorBackground` | `#FDE7E9` | Error output background |
| `CellErrorForeground` | `#A1260D` | Error output text color |
| `CellRunningIndicator` | `#0078D4` | Running cell indicator color |

### Toolbar Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `ToolbarBackground` | `#F3F3F3` | Main toolbar background |
| `ToolbarForeground` | `#1E1E1E` | Toolbar text/icon color |
| `ToolbarButtonHover` | `#E0E0E0` | Button hover background |
| `ToolbarSeparator` | `#D4D4D4` | Separator line color |
| `ToolbarDisabledForeground` | `#A0A0A0` | Disabled button color |

### Sidebar Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `SidebarBackground` | `#F3F3F3` | Sidebar panel background |
| `SidebarForeground` | `#1E1E1E` | Sidebar text color |
| `SidebarItemHover` | `#E0E0E0` | Item hover background |
| `SidebarItemActive` | `#D0D0D0` | Active item background |

### Border Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `BorderDefault` | `#E0E0E0` | General UI border color |
| `BorderFocused` | `#0078D4` | Focused input border color |

### Accent / Highlight Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `AccentPrimary` | `#0078D4` | Primary accent for links and interactive elements |
| `AccentSecondary` | `#005A9E` | Secondary accent for hover/emphasis |
| `HighlightBackground` | `#FFF3CD` | Highlighted content background |
| `HighlightForeground` | `#664D03` | Text over highlighted background |

### Status Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `StatusSuccess` | `#28A745` | Success/positive status |
| `StatusWarning` | `#FFC107` | Warning/caution status |
| `StatusError` | `#DC3545` | Error/failure status |
| `StatusInfo` | `#17A2B8` | Informational status |

### Scrollbar Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `ScrollbarThumb` | `#C1C1C1` | Scrollbar thumb (handle) |
| `ScrollbarTrack` | `#F1F1F1` | Scrollbar track background |
| `ScrollbarThumbHover` | `#A8A8A8` | Thumb color on hover |

### Overlay / Dropdown / Tooltip Tokens

| Token | Default (Light) | Description |
|-------|-----------------|-------------|
| `OverlayBackground` | `#FFFFFF` | Modal overlay background |
| `OverlayBorder` | `#E0E0E0` | Overlay border color |
| `DropdownBackground` | `#FFFFFF` | Dropdown menu background |
| `DropdownHover` | `#F0F0F0` | Dropdown item hover background |
| `TooltipBackground` | `#333333` | Tooltip background |
| `TooltipForeground` | `#FFFFFF` | Tooltip text color |

## Typography Reference

`ThemeTypography` contains 10 `FontDescriptor` properties. Each `FontDescriptor` has:

| Field | Type | Description |
|-------|------|-------------|
| `Family` | `string` | CSS font-family name (e.g. "Cascadia Code") |
| `SizePx` | `double` | Font size in pixels |
| `Weight` | `int` | Font weight 100–900 (default: 400) |
| `LineHeight` | `double` | Line height multiplier (default: 1.4) |

### Typography Properties

| Property | Default Family | Default Size | Default Weight | Usage |
|----------|---------------|-------------|----------------|-------|
| `EditorFont` | Cascadia Code | 14 | 400 | Code editor cells |
| `UIFont` | Segoe UI | 13 | 400 | UI labels and controls |
| `ProseFont` | Georgia | 16 | 400 | Markdown prose |
| `H1Font` | Segoe UI | 32 | 700 | Heading 1 |
| `H2Font` | Segoe UI | 26 | 700 | Heading 2 |
| `H3Font` | Segoe UI | 22 | 600 | Heading 3 |
| `H4Font` | Segoe UI | 18 | 600 | Heading 4 |
| `H5Font` | Segoe UI | 15 | 600 | Heading 5 |
| `H6Font` | Segoe UI | 13 | 600 | Heading 6 |
| `CodeOutputFont` | Cascadia Mono | 13 | 400 | Execution output |

## Spacing Reference

`ThemeSpacing` controls padding, margins, and border radii. All values are in device-independent pixels.

| Property | Default | Description |
|----------|---------|-------------|
| `CellPadding` | 12 | Inner padding within cells |
| `CellGap` | 8 | Vertical gap between cells |
| `ToolbarHeight` | 40 | Main toolbar height |
| `SidebarWidth` | 260 | Sidebar panel width |
| `ContentMarginHorizontal` | 24 | Horizontal content margin |
| `ContentMarginVertical` | 16 | Vertical content margin |
| `CellBorderRadius` | 4 | Cell corner radius |
| `ButtonBorderRadius` | 4 | Button corner radius |
| `OutputPadding` | 8 | Output region padding |
| `ScrollbarWidth` | 10 | Scrollbar track width |

## Syntax Color Mapping

Themes provide syntax highlighting via `GetSyntaxColors()`, which returns a `SyntaxColorMap`. The platform recognizes 12 standard token types:

| Token Type | Description | Light Default | Dark Default |
|------------|-------------|--------------|-------------|
| `keyword` | Language keywords (`if`, `class`, `var`) | `#0000FF` | `#569CD6` |
| `comment` | Code comments | `#008000` | `#6A9955` |
| `string` | String literals | `#A31515` | `#CE9178` |
| `number` | Numeric literals | `#098658` | `#B5CEA8` |
| `type` | Type names and references | `#267F99` | `#4EC9B0` |
| `function` | Function/method names | `#795E26` | `#DCDCAA` |
| `variable` | Variable names | `#001080` | `#9CDCFE` |
| `operator` | Operators (`+`, `==`, `=>`) | `#000000` | `#D4D4D4` |
| `punctuation` | Brackets, semicolons | `#000000` | `#D4D4D4` |
| `preprocessor` | Preprocessor directives | `#808080` | `#C586C0` |
| `attribute` | Attributes and decorators | `#267F99` | `#4EC9B0` |
| `namespace` | Namespace references | `#267F99` | `#4EC9B0` |

Always provide all 12 tokens. Renderers may fall back to `EditorForeground` for missing tokens.

## Overriding Defaults

Since `ThemeColorTokens`, `ThemeTypography`, and `ThemeSpacing` are C# records with `init` properties, you have two approaches:

### Constructor with Object Initializer (Recommended)

Set only the properties that differ from defaults:

```csharp
public ThemeColorTokens Colors { get; } = new ThemeColorTokens
{
    EditorBackground = "#002B36",
    EditorForeground = "#839496",
    // Unset properties keep their light-theme defaults
};
```

### Record `with` Expression

Start from an existing theme and override specific values:

```csharp
private static readonly ThemeColorTokens DarkBase = new VersoDarkTheme().Colors;

public ThemeColorTokens Colors { get; } = DarkBase with
{
    AccentPrimary = "#B58900",
    AccentSecondary = "#268BD2",
};
```

This is useful for creating theme variants that share most tokens with a base theme.

## Custom Theme Tokens

The `GetCustomToken(string key)` method allows themes to define additional tokens beyond the built-in set. Extensions can query these at runtime:

```csharp
// In your theme
public string? GetCustomToken(string key) => key switch
{
    "chart.gridline" => "#E0E0E0",
    "chart.axis" => "#333333",
    _ => null
};

// In a renderer
var gridColor = context.Theme.ActiveTheme.GetCustomToken("chart.gridline") ?? "#CCC";
```

Use a namespaced key format (`"yourextension.tokenname"`) to avoid collisions.

## ThemeKind and Front-End Behavior

The `ThemeKind` enum has three values:

| Value | Description |
|-------|-------------|
| `ThemeKind.Light` | Light color scheme with dark text on light backgrounds |
| `ThemeKind.Dark` | Dark color scheme with light text on dark backgrounds |
| `ThemeKind.HighContrast` | High-contrast scheme for accessibility |

Front-end renderers use `ThemeKind` to:
- Set the appropriate `prefers-color-scheme` media query behavior
- Apply platform-specific OS integration (title bar color, scrollbar style)
- Choose the correct icon variant (light/dark) for UI elements

Always set `ThemeKind` accurately since it affects more than just colors.

## Testing Themes

Use MSTest with the existing theme test patterns. Key things to verify:

### 1. All Color Tokens Are Valid Hex

```csharp
[TestMethod]
public void AllColorTokens_AreValidHex()
{
    var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
    var props = typeof(ThemeColorTokens)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string));

    foreach (var prop in props)
    {
        var value = (string)prop.GetValue(myTheme.Colors)!;
        Assert.IsTrue(hexRegex.IsMatch(value),
            $"Color token {prop.Name} has invalid hex: {value}");
    }
}
```

### 2. Syntax Colors Are Complete

```csharp
[TestMethod]
public void SyntaxColors_HasAtLeast12Tokens()
{
    var map = myTheme.GetSyntaxColors();
    Assert.IsTrue(map.Count >= 12);
}
```

### 3. WCAG Contrast Verification (Accessibility)

For high-contrast or accessibility-focused themes, programmatically verify contrast ratios using the W3C relative luminance formula:

```csharp
private static double RelativeLuminance(string hex)
{
    var r = SrgbToLinear(int.Parse(hex[1..3], NumberStyles.HexNumber) / 255.0);
    var g = SrgbToLinear(int.Parse(hex[3..5], NumberStyles.HexNumber) / 255.0);
    var b = SrgbToLinear(int.Parse(hex[5..7], NumberStyles.HexNumber) / 255.0);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

private static double SrgbToLinear(double c)
    => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

private static double ContrastRatio(string hex1, string hex2)
{
    var l1 = RelativeLuminance(hex1);
    var l2 = RelativeLuminance(hex2);
    var lighter = Math.Max(l1, l2);
    var darker = Math.Min(l1, l2);
    return (lighter + 0.05) / (darker + 0.05);
}
```

See `VersoHighContrastThemeTests` for a complete example.

## Accessibility / WCAG Guidance

When building accessible themes, follow these WCAG 2.1 AA guidelines:

- **Normal text**: foreground/background contrast ≥ 4.5:1
- **Large text** (≥ 18pt or 14pt bold): contrast ≥ 3:1
- **UI components** (borders, icons): contrast ≥ 3:1
- **Syntax colors**: recommend ≥ 5.0:1 against editor background for readability

Key foreground/background pairs to verify:

| Foreground Token | Background Token | Minimum Ratio |
|-----------------|-----------------|---------------|
| `EditorForeground` | `EditorBackground` | 4.5:1 |
| `EditorLineNumber` | `EditorGutter` | 4.5:1 |
| `CellOutputForeground` | `CellOutputBackground` | 4.5:1 |
| `CellErrorForeground` | `CellErrorBackground` | 4.5:1 |
| `ToolbarForeground` | `ToolbarBackground` | 4.5:1 |
| `ToolbarDisabledForeground` | `ToolbarBackground` | 4.5:1 |
| `SidebarForeground` | `SidebarBackground` | 4.5:1 |
| `HighlightForeground` | `HighlightBackground` | 4.5:1 |
| `TooltipForeground` | `TooltipBackground` | 4.5:1 |
| All syntax colors | `EditorBackground` | 5.0:1 |

For high-contrast themes, also consider:
- Zero or near-zero border radius for sharper edges
- Slightly larger font sizes for improved readability
- High-visibility focus indicators (bright border colors)
- Inverted tooltip colors for maximum visibility

## Complete Example: VersoHighContrastTheme

The built-in `VersoHighContrastTheme` demonstrates all best practices for an accessible theme:

```csharp
[VersoExtension]
public sealed class VersoHighContrastTheme : ITheme
{
    public string ThemeId => "verso-highcontrast";
    public ThemeKind ThemeKind => ThemeKind.HighContrast;

    public ThemeColorTokens Colors { get; } = new ThemeColorTokens
    {
        EditorBackground = "#000000",        // Pure black for max contrast
        EditorForeground = "#FFFFFF",        // White text, 21:1 ratio
        AccentPrimary = "#FFD700",           // Gold, 13.9:1 ratio
        AccentSecondary = "#6FC3DF",         // Cyan, 9.0:1 ratio
        TooltipBackground = "#FFD700",       // Inverted: gold bg
        TooltipForeground = "#000000",       // Black text, 13.9:1
        // ... all 45 tokens set
    };

    public ThemeTypography Typography { get; } = new ThemeTypography
    {
        EditorFont = new FontDescriptor("Cascadia Code", 15),  // +1px
        UIFont = new FontDescriptor("Segoe UI", 14),           // +1px
    };

    public ThemeSpacing Spacing { get; } = new ThemeSpacing
    {
        CellBorderRadius = 0,     // Sharp edges
        ButtonBorderRadius = 0,
    };
}
```

See `src/Verso/Extensions/Themes/VersoHighContrastTheme.cs` for the complete implementation and `tests/Verso.Tests/Extensions/VersoHighContrastThemeTests.cs` for the full WCAG verification test suite.

## See Also

- [Extension Interfaces](extension-interfaces.md): full `ITheme` API reference
- [Testing Extensions](testing-extensions.md): test stubs and patterns
- [Best Practices](best-practices.md): theme-aware rendering for extension authors
- [Getting Started](getting-started.md): project scaffolding
