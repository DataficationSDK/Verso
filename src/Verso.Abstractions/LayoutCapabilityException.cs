namespace Verso.Abstractions;

/// <summary>
/// Thrown when a cell-collection mutation is attempted but the active layout does not support it.
/// </summary>
public sealed class LayoutCapabilityException : InvalidOperationException
{
    public LayoutCapabilityException(LayoutCapabilities required)
        : base($"The active layout does not support the required capability: {required}.")
    {
        RequiredCapability = required;
    }

    public LayoutCapabilityException(LayoutCapabilities required, string message)
        : base(message)
    {
        RequiredCapability = required;
    }

    /// <summary>
    /// The capability that was required but not supported.
    /// </summary>
    public LayoutCapabilities RequiredCapability { get; }
}
