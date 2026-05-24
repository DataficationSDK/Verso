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
    string? ContentSecurityPolicy);
