namespace Verso.Abstractions;

/// <summary>
/// Bundle delivered by an <see cref="LayoutRendererIsolation.Isolated"/> layout so the
/// host can instantiate the layout's renderer inside its isolation boundary.
/// </summary>
/// <param name="EntryPoint">
/// Relative path of the entry module within <paramref name="Files"/>. The host loads this
/// first; other files are addressable by their relative path for the entry to reference.
/// </param>
/// <param name="Files">
/// Complete asset bundle keyed by relative path. The host materializes the bundle into the
/// isolation boundary using a host-appropriate mechanism.
/// </param>
/// <param name="ContentSecurityPolicy">
/// Optional Content Security Policy hint that hosts capable of enforcing CSP apply to the
/// boundary. Hosts without a CSP concept may ignore it. The host appends platform-required
/// directives but never relaxes the supplied policy.
/// </param>
public sealed record LayoutRendererPackage(
    string EntryPoint,
    IReadOnlyDictionary<string, byte[]> Files,
    string? ContentSecurityPolicy)
{
    /// <summary>
    /// Optional version of the host-renderer message protocol this bundle was built against
    /// (see <see cref="LayoutBridgeProtocol"/>). <c>null</c> means the renderer does not
    /// declare a version and the host treats it as compatible. When set, the host compares it
    /// to <see cref="LayoutBridgeProtocol.Version"/> and reports a non-fatal warning on a
    /// mismatch; the renderer can perform the reciprocal check using the
    /// <c>hostProtocolVersion</c> field the host sends in <c>verso/init</c>.
    /// </summary>
    /// <remarks>
    /// Declared as an init-only property rather than a positional parameter so that adding it
    /// does not change the primary constructor's signature: renderer extensions compiled
    /// against the three-argument constructor keep binding to it at run time. Set it with an
    /// object initializer: <c>new LayoutRendererPackage(entry, files, csp) { RendererProtocolVersion = "1.0" }</c>.
    /// </remarks>
    public string? RendererProtocolVersion { get; init; }
}
