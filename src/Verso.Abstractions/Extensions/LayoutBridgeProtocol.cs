namespace Verso.Abstractions;

/// <summary>
/// Identifies the version of the message protocol spoken across the isolated-layout
/// boundary (the host and an <see cref="LayoutRendererIsolation.Isolated"/> renderer).
/// </summary>
/// <remarks>
/// The protocol is the set of framework message types exchanged between host and
/// renderer (the <c>verso/</c> namespace) plus their payload shapes. The host advertises
/// <see cref="Version"/> to the renderer in the <c>verso/init</c> message
/// (<c>hostProtocolVersion</c>), and a renderer declares the version it was built against
/// through <see cref="LayoutRendererPackage.RendererProtocolVersion"/>. Either side can
/// compare the two to detect a mismatch instead of failing opaquely on an unrecognized
/// message.
/// <para>
/// The version uses a <c>major.minor</c> form. A change to the major component signals an
/// incompatible protocol; a minor bump signals backward-compatible additions (for example
/// a new optional field on an existing message).
/// </para>
/// </remarks>
public static class LayoutBridgeProtocol
{
    /// <summary>
    /// The protocol version this build of the host speaks and the value a renderer should
    /// declare when it targets the current contract.
    /// </summary>
    public const string Version = "1.0";
}
