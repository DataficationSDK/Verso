namespace Verso.Abstractions;

/// <summary>
/// Defines the typography settings for a Verso notebook theme,
/// mapping each text role to a <see cref="FontDescriptor"/>.
/// </summary>
public sealed record ThemeTypography
{
    /// <summary>Font used for code editor cells.</summary>
    public FontDescriptor EditorFont { get; init; } = new("Cascadia Code", 14);

    /// <summary>Font used for general user-interface labels and controls.</summary>
    public FontDescriptor UIFont { get; init; } = new("Segoe UI", 13);

    /// <summary>Font used for Markdown prose and rich-text content.</summary>
    public FontDescriptor ProseFont { get; init; } = new("Georgia", 16, LineHeight: 1.6);

    /// <summary>Font for heading level 1 elements.</summary>
    public FontDescriptor H1Font { get; init; } = new("Segoe UI", 32, Weight: 700);

    /// <summary>Font for heading level 2 elements.</summary>
    public FontDescriptor H2Font { get; init; } = new("Segoe UI", 26, Weight: 700);

    /// <summary>Font for heading level 3 elements.</summary>
    public FontDescriptor H3Font { get; init; } = new("Segoe UI", 22, Weight: 600);

    /// <summary>Font for heading level 4 elements.</summary>
    public FontDescriptor H4Font { get; init; } = new("Segoe UI", 18, Weight: 600);

    /// <summary>Font for heading level 5 elements.</summary>
    public FontDescriptor H5Font { get; init; } = new("Segoe UI", 15, Weight: 600);

    /// <summary>Font for heading level 6 elements.</summary>
    public FontDescriptor H6Font { get; init; } = new("Segoe UI", 13, Weight: 600);

    /// <summary>Font used for code execution output and console text.</summary>
    public FontDescriptor CodeOutputFont { get; init; } = new("Cascadia Mono", 13);
}
