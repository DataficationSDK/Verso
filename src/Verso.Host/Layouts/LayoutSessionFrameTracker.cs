using System.Collections.Concurrent;
using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Host.Layouts;

/// <summary>
/// Per-<see cref="NotebookSession"/> allocator and dispatcher for renderer frame
/// instances. Generates opaque <c>frameInstanceId</c> tokens, routes mount and
/// unmount callbacks to the matching <see cref="ILayoutLifecycleHandler"/>, and
/// invokes unmount for any frames still active at session teardown so extension
/// state always gets a chance to clean up.
/// </summary>
internal sealed class LayoutSessionFrameTracker : IAsyncDisposable
{
    private readonly string _notebookId;
    private readonly ExtensionHost _extensionHost;
    private readonly Func<IVersoContext> _versoContextFactory;
    private readonly ConcurrentDictionary<string, ActiveFrame> _active = new(StringComparer.Ordinal);
    private int _seq;
    private bool _disposed;

    public LayoutSessionFrameTracker(
        string notebookId,
        ExtensionHost extensionHost,
        Func<IVersoContext> versoContextFactory)
    {
        _notebookId = notebookId;
        _extensionHost = extensionHost;
        _versoContextFactory = versoContextFactory;
    }

    /// <summary>
    /// Allocates a frame instance id in the form <c>{notebookId}/{layoutId}/{seq}</c>.
    /// The token is opaque to extensions and frames; callers must treat it as a string.
    /// </summary>
    public string AllocateFrameInstanceId(string layoutId)
    {
        var seq = Interlocked.Increment(ref _seq);
        return $"{_notebookId}/{layoutId}/{seq}";
    }

    /// <summary>
    /// Dispatches <see cref="ILayoutLifecycleHandler.OnRendererMountedAsync"/> for
    /// the given identity pair and records the frame as active so a matching
    /// unmount can fire later (including on session dispose). When no handler is
    /// registered for the pair, the frame is still tracked but no callback fires
    /// and <c>null</c> is returned for the init payload.
    /// </summary>
    public async Task<IDictionary<string, object>?> InvokeMountedAsync(
        string extensionId,
        string layoutId,
        string frameInstanceId,
        LayoutRendererIsolation isolation,
        LayoutFrameChannel channel,
        CancellationToken cancellationToken = default)
    {
        _extensionHost.TryGetLayoutLifecycleHandler(extensionId, layoutId, out var handler);

        _active[frameInstanceId] = new ActiveFrame(extensionId, layoutId, isolation, channel, handler);

        if (handler is null)
            return null;

        var context = new LayoutRendererMountContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            Isolation = isolation,
            Verso = _versoContextFactory(),
            Frame = channel,
            CancellationToken = cancellationToken
        };

        return await handler.OnRendererMountedAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches <see cref="ILayoutLifecycleHandler.OnRendererUnmountedAsync"/>
    /// for the given frame and removes it from the active set. The supplied
    /// channel (when present) is marked dead before the handler runs so handler
    /// code observes <see cref="ILayoutFrameChannel.IsAlive"/> as <c>false</c>.
    /// Safe to call when no handler is registered (no-op beyond the channel
    /// teardown and tracking removal).
    /// </summary>
    public Task InvokeUnmountedAsync(
        string extensionId,
        string layoutId,
        string frameInstanceId,
        LayoutRendererIsolation isolation,
        CancellationToken cancellationToken = default)
    {
        _active.TryRemove(frameInstanceId, out var entry);

        var channel = entry?.Channel;
        channel?.MarkDead();

        _extensionHost.TryGetLayoutLifecycleHandler(extensionId, layoutId, out var handler);
        if (handler is null)
            return Task.CompletedTask;

        var context = new LayoutRendererUnmountContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            Isolation = isolation,
            Verso = _versoContextFactory(),
            CancellationToken = cancellationToken
        };

        return handler.OnRendererUnmountedAsync(context);
    }

    /// <summary>
    /// Unmounts every active frame in undefined order. Called during session
    /// teardown so kernel restart, notebook close, and host shutdown all give
    /// lifecycle handlers a chance to release per-frame state.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (frameInstanceId, entry) in _active.ToArray())
        {
            try
            {
                await InvokeUnmountedAsync(
                    entry.ExtensionId,
                    entry.LayoutId,
                    frameInstanceId,
                    entry.Isolation).ConfigureAwait(false);
            }
            catch
            {
                // A lifecycle handler throwing on unmount must not block teardown
                // for the remaining frames or the session itself.
            }
        }

        _active.Clear();
    }

    private sealed record ActiveFrame(
        string ExtensionId,
        string LayoutId,
        LayoutRendererIsolation Isolation,
        LayoutFrameChannel Channel,
        ILayoutLifecycleHandler? Handler);
}
