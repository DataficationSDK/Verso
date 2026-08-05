namespace Verso.Abstractions;

/// <summary>
/// Identifies the version of the message protocol spoken across an output channel: the framework
/// message types exchanged between a host and the view drawing one output, plus their payload
/// shapes.
/// </summary>
/// <remarks>
/// The view declares the version it was built against when it reports itself ready, and the host
/// compares that against <see cref="Version"/>. A mismatch is reported and the view is mounted
/// anyway, because a view built against a neighbouring version usually works and refusing to draw
/// it would turn a warning into a blank output.
/// <para>
/// The version uses a <c>major.minor</c> form, read the same way <see cref="LayoutBridgeProtocol"/>
/// reads its own: a change to the major component signals an incompatible protocol, and a minor
/// bump signals a backward-compatible addition such as a new optional field.
/// </para>
/// </remarks>
public static class OutputChannelProtocol
{
    /// <summary>
    /// The protocol version this build of the host speaks, and the value a view should declare
    /// when it targets the current contract.
    /// </summary>
    public const string Version = "1.0";

    /// <summary>
    /// The major component of a <c>major.minor</c> version string, or null when there is nothing
    /// to read. Two views agreeing here are close enough to talk to each other.
    /// </summary>
    public static string? MajorOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var trimmed = version.Trim();
        var dot = trimmed.IndexOf('.');
        var major = dot < 0 ? trimmed : trimmed.Substring(0, dot);
        return major.Length == 0 ? null : major;
    }
}
