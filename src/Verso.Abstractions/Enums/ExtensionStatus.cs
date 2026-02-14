namespace Verso.Abstractions;

/// <summary>
/// Indicates whether an extension is currently enabled or disabled at runtime.
/// </summary>
public enum ExtensionStatus
{
    /// <summary>
    /// The extension is active and its capabilities are available.
    /// </summary>
    Enabled,

    /// <summary>
    /// The extension is loaded but its capabilities are excluded from queries.
    /// </summary>
    Disabled
}
