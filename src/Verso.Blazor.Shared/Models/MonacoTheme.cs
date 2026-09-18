using System.Globalization;
using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

/// <summary>One syntax coloring rule, in the code editor's own token names.</summary>
/// <param name="Token">Token name the rule applies to, along with every token beneath it.</param>
/// <param name="Foreground">Text color, or <c>null</c> to leave the color alone.</param>
/// <param name="FontStyle">Space-separated <c>italic</c>, <c>bold</c>, <c>underline</c>, or <c>null</c>.</param>
public sealed record MonacoTokenRule(string Token, string? Foreground, string? FontStyle = null);

/// <summary>
/// Everything <c>versoMonaco.applyTheme</c> needs to make the code editor look like the
/// active theme.
/// </summary>
/// <param name="Base">Built-in editor theme that supplies whatever this one leaves out.</param>
/// <param name="Colors">Editor color id to CSS color.</param>
/// <param name="Rules">Syntax coloring rules.</param>
public sealed record MonacoThemeSpec(
    string Base,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<MonacoTokenRule> Rules);

/// <summary>
/// Builds a <see cref="MonacoThemeSpec"/> from the active <see cref="ThemeData"/>, so the
/// code editor takes its surface from the theme's editor tokens and its syntax colors from
/// <see cref="ITheme.GetSyntaxColors"/> rather than from a stock palette.
/// </summary>
public static class MonacoThemeBuilder
{
    // A theme names the kind of thing being colored; the editor's tokenizers have their own
    // names for the same things, and often more than one. A name matches every token that
    // starts with it, so "keyword" also covers "keyword.cs".
    private static readonly (string SyntaxKey, string[] Tokens)[] TokenMap =
    {
        ("keyword", new[] { "keyword" }),
        ("comment", new[] { "comment" }),
        ("string", new[] { "string" }),
        ("number", new[] { "number" }),
        ("type", new[] { "type" }),
        ("function", new[] { "function", "support.function" }),
        ("variable", new[] { "variable" }),
        ("operator", new[] { "operator", "operators" }),
        ("punctuation", new[] { "delimiter" }),
        ("preprocessor", new[] { "keyword.preprocessor", "namespace.cpp", "directive", "metatag" }),
        ("attribute", new[] { "annotation", "attribute.name" }),
        ("namespace", new[] { "namespace" }),
    };

    /// <summary>
    /// Returns the editor theme for <paramref name="data"/>, or <c>null</c> when no theme is
    /// active and the editor should keep a stock one.
    /// </summary>
    public static MonacoThemeSpec? Build(ThemeKind? kind, ThemeData? data)
    {
        if (data is null) return null;
        var c = data.Colors;

        var colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["editor.background"] = c.EditorBackground,
            ["editor.foreground"] = c.EditorForeground,
            ["editorLineNumber.foreground"] = c.EditorLineNumber,
            ["editorLineNumber.activeForeground"] = c.EditorForeground,
            ["editorCursor.foreground"] = c.EditorCursor,
            ["editor.selectionBackground"] = c.EditorSelection,
            ["editorGutter.background"] = c.EditorGutter,
            ["editorWhitespace.foreground"] = c.EditorWhitespace,
            ["editorWidget.background"] = c.OverlayBackground,
            ["editorWidget.border"] = c.OverlayBorder,
            ["editorSuggestWidget.background"] = c.OverlayBackground,
            ["editorSuggestWidget.border"] = c.OverlayBorder,
            ["editorHoverWidget.background"] = c.OverlayBackground,
            ["editorHoverWidget.border"] = c.OverlayBorder,
            ["editorError.foreground"] = c.StatusError,
            ["editorWarning.foreground"] = c.StatusWarning,
            ["editorInfo.foreground"] = c.StatusInfo,
            ["scrollbarSlider.background"] = c.ScrollbarThumb,
            ["scrollbarSlider.hoverBackground"] = c.ScrollbarThumbHover,
            ["focusBorder"] = c.BorderFocused,
        };

        var rules = new List<MonacoTokenRule>();
        if (data.SyntaxColors is { Count: > 0 } syntax)
        {
            foreach (var (key, tokens) in TokenMap)
            {
                if (!syntax.TryGetValue(key, out var color) || string.IsNullOrWhiteSpace(color))
                    continue;
                foreach (var token in tokens)
                    rules.Add(new MonacoTokenRule(token, color));
            }
        }

        return new MonacoThemeSpec(BaseFor(kind, c.EditorBackground), colors, rules);
    }

    /// <summary>
    /// Picks the stock theme to inherit from. High contrast comes in a dark and a light
    /// form, and <see cref="ThemeKind"/> does not say which, so the editor background does.
    /// </summary>
    internal static string BaseFor(ThemeKind? kind, string? editorBackground) => kind switch
    {
        ThemeKind.Dark => "vs-dark",
        ThemeKind.HighContrast => IsLight(editorBackground) ? "hc-light" : "hc-black",
        _ => "vs",
    };

    private static bool IsLight(string? hex)
    {
        if (hex is null) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3 || h.Length == 4)
            h = string.Concat(h.Select(ch => new string(ch, 2)));
        if (h.Length < 6) return false;
        if (!int.TryParse(h.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(h.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(h.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) > 140;
    }
}
