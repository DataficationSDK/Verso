using System.Text.Json;
using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Shared.Services;

/// <summary>
/// The host surface an isolated (iframe) layout renderer needs beyond the common
/// <see cref="INotebookService"/> contract. Implemented in-process by the Blazor
/// Server host and remotely (over the JSON-RPC bridge) by the Blazor WASM host, so
/// the single <c>CustomLayoutFrame</c> component drives both.
/// </summary>
/// <remarks>
/// All members here back the iframe handshake and message pump:
/// allocate a frame instance, fetch the renderer package, run the mount/unmount
/// lifecycle callbacks, push extension messages into the frame, and forward log
/// and theme state. The common notebook surface (cells, execution, layout
/// interaction, cell interaction) is inherited from <see cref="INotebookService"/>.
/// </remarks>
public interface IIsolatedLayoutHost : INotebookService
{
    /// <summary>
    /// Allocates an opaque frame-instance token for an isolated layout. Throws when the
    /// referenced layout is not registered or does not declare isolated rendering.
    /// </summary>
    Task<string> AllocateLayoutFrameInstanceAsync(string extensionId, string layoutId);

    /// <summary>
    /// Returns the renderer package (entry point, file bytes, optional CSP) for an
    /// isolated layout, or <c>null</c> when the layout is inline or ships no package.
    /// </summary>
    Task<LayoutRendererPackageDto?> GetLayoutRendererPackageAsync(string extensionId, string layoutId);

    /// <summary>
    /// Runs the layout's mount lifecycle callback for a frame instance and returns the
    /// init seed the renderer receives under the <c>extension</c> field of <c>verso/init</c>,
    /// or <c>null</c> when the layout has no lifecycle handler.
    /// </summary>
    Task<IDictionary<string, JsonElement>?> LayoutRendererMountedAsync(
        string extensionId, string layoutId, string frameInstanceId);

    /// <summary>Runs the layout's unmount lifecycle callback for a frame instance.</summary>
    Task LayoutRendererUnmountedAsync(string extensionId, string layoutId, string frameInstanceId);

    /// <summary>Forwards a log message emitted by an isolated renderer to the host log.</summary>
    Task LogExtensionAsync(
        string extensionId, string layoutId, string frameInstanceId, string level, string message);

    /// <summary>
    /// The current resolved theme bundle pushed to isolated renderers, or <c>null</c> when
    /// no theme data is available.
    /// </summary>
    LayoutThemeBundle? CurrentTheme { get; }

    /// <summary>Raised when a cell's outputs are updated, carrying the raw outputs payload.</summary>
    event Action<CellOutputUpdatedEventArgs>? OnCellOutputUpdated;

    /// <summary>
    /// Raised when an extension pushes a message to an isolated frame via
    /// <c>ILayoutFrameChannel.PostMessageAsync</c>. Subscribers filter by
    /// <see cref="LayoutFrameMessageEventArgs.FrameInstanceId"/>; the
    /// <see cref="LayoutFrameMessageEventArgs.Type"/> already carries the <c>ext/</c> prefix.
    /// </summary>
    event Action<LayoutFrameMessageEventArgs>? OnLayoutFrameMessage;
}
