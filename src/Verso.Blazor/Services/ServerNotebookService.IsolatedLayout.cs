using System.Collections.Concurrent;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Services;

/// <summary>
/// In-process implementation of the isolated (iframe) layout surface for Blazor Server.
/// Mirrors the out-of-process host's <c>LayoutHandler</c> but runs directly against the
/// in-process <see cref="Verso.Extensions.ExtensionHost"/>, so no JSON-RPC round-trip or
/// base64 transfer is involved. Drives the same <c>CustomLayoutFrame</c> component the
/// WASM host uses.
/// </summary>
public sealed partial class ServerNotebookService
{
    private readonly ConcurrentDictionary<string, ServerActiveFrame> _activeFrames =
        new(StringComparer.Ordinal);
    private int _frameSeq;

    /// <inheritdoc />
    public event Action<CellOutputUpdatedEventArgs>? OnCellOutputUpdated;

    /// <inheritdoc />
    public event Action<LayoutFrameMessageEventArgs>? OnLayoutFrameMessage;

    /// <inheritdoc />
    public LayoutThemeBundle? CurrentTheme => LayoutThemeBundleBuilder.Build(ActiveThemeKind, ActiveThemeData);

    /// <inheritdoc />
    public Task<string> AllocateLayoutFrameInstanceAsync(string extensionId, string layoutId)
    {
        if (_extensionHost is null)
            throw new InvalidOperationException("No notebook is loaded.");

        if (!_extensionHost.TryGetLayoutEngine(extensionId, layoutId, out var engine))
            throw new InvalidOperationException(
                $"No layout engine is registered for (extensionId='{extensionId}', layoutId='{layoutId}').");

        if (engine.RendererIsolation != LayoutRendererIsolation.Isolated)
            throw new InvalidOperationException(
                $"Layout (extensionId='{extensionId}', layoutId='{layoutId}') does not declare " +
                $"isolated rendering; frame instances are only allocated for isolated renderers.");

        var seq = Interlocked.Increment(ref _frameSeq);
        return Task.FromResult($"{layoutId}/{seq}");
    }

    /// <inheritdoc />
    public async Task<LayoutRendererPackageDto?> GetLayoutRendererPackageAsync(
        string extensionId, string layoutId)
    {
        if (_scaffold is null || _extensionHost is null) return null;

        if (!_extensionHost.TryGetLayoutEngine(extensionId, layoutId, out var engine))
            throw new InvalidOperationException(
                $"No layout engine is registered for (extensionId='{extensionId}', layoutId='{layoutId}').");

        if (engine.RendererIsolation != LayoutRendererIsolation.Isolated)
            return null;

        var ctx = new BlazorLayoutRenderContext(_scaffold);
        var package = await engine.GetRendererPackageAsync(ctx).ConfigureAwait(false);
        if (package is null) return null;

        // The in-process host hands the bytes straight to the component; unlike the WASM
        // path there is no base64 round-trip through the JSON-RPC bridge.
        return new LayoutRendererPackageDto(
            package.EntryPoint,
            package.Files,
            package.ContentSecurityPolicy);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, JsonElement>?> LayoutRendererMountedAsync(
        string extensionId, string layoutId, string frameInstanceId)
    {
        if (_scaffold is null || _extensionHost is null) return null;

        _extensionHost.TryGetLayoutLifecycleHandler(extensionId, layoutId, out var handler);

        var channel = new ServerFrameChannel(frameInstanceId, this);
        _activeFrames[frameInstanceId] = new ServerActiveFrame(extensionId, layoutId, channel, handler);

        if (handler is null) return null;

        var context = new LayoutRendererMountContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            Isolation = LayoutRendererIsolation.Isolated,
            Verso = new BlazorLayoutRenderContext(_scaffold),
            Frame = channel,
            CancellationToken = CancellationToken.None
        };

        var extra = await handler.OnRendererMountedAsync(context).ConfigureAwait(false);
        return ConvertMountSeed(extra);
    }

    /// <inheritdoc />
    public async Task LayoutRendererUnmountedAsync(
        string extensionId, string layoutId, string frameInstanceId)
    {
        if (_extensionHost is null) return;

        _activeFrames.TryRemove(frameInstanceId, out var entry);
        entry?.Channel.MarkDead();

        if (!_extensionHost.TryGetLayoutLifecycleHandler(extensionId, layoutId, out var handler))
            return;

        var context = new LayoutRendererUnmountContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            Isolation = LayoutRendererIsolation.Isolated,
            Verso = _scaffold is not null
                ? new BlazorLayoutRenderContext(_scaffold)
                : null!,
            CancellationToken = CancellationToken.None
        };

        await handler.OnRendererUnmountedAsync(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task LogExtensionAsync(
        string extensionId, string layoutId, string frameInstanceId, string level, string message)
    {
        Console.Error.WriteLine($"[layout:{extensionId}/{layoutId}] [{level}] {message}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Unmounts every active isolated frame. Called during notebook teardown (new/open/close,
    /// kernel restart, dispose) so lifecycle handlers can release per-frame state.
    /// </summary>
    private async Task UnmountAllFramesAsync()
    {
        foreach (var (frameInstanceId, entry) in _activeFrames.ToArray())
        {
            try
            {
                await LayoutRendererUnmountedAsync(
                    entry.ExtensionId, entry.LayoutId, frameInstanceId).ConfigureAwait(false);
            }
            catch
            {
                // A lifecycle handler throwing on unmount must not block teardown for the
                // remaining frames or the notebook itself.
            }
        }

        _activeFrames.Clear();
    }

    /// <summary>
    /// Re-broadcasts a cell-output update as a <see cref="CellOutputUpdatedEventArgs"/> carrying
    /// the cell's serialized outputs, so an isolated renderer can receive <c>verso/cellOutputs</c>.
    /// Skips the work entirely when no subscriber is attached.
    /// </summary>
    private void RaiseCellOutputUpdated(Guid cellId)
    {
        var handler = OnCellOutputUpdated;
        if (handler is null || _scaffold is null) return;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return;

        try
        {
            var outputs = JsonSerializer.SerializeToElement(cell.Outputs);
            handler.Invoke(new CellOutputUpdatedEventArgs(cellId, outputs));
        }
        catch (JsonException)
        {
            // A non-serializable output payload should not break the update pump.
        }
    }

    private static IDictionary<string, JsonElement>? ConvertMountSeed(IDictionary<string, object>? seed)
    {
        if (seed is null || seed.Count == 0) return null;

        var result = new Dictionary<string, JsonElement>(seed.Count, StringComparer.Ordinal);
        foreach (var (key, value) in seed)
            result[key] = JsonSerializer.SerializeToElement(value);
        return result;
    }

    /// <summary>
    /// In-process <see cref="ILayoutFrameChannel"/>. Raises
    /// <see cref="ServerNotebookService.OnLayoutFrameMessage"/> with an <c>ext/</c>-prefixed
    /// type, which <c>CustomLayoutFrame</c> forwards into the matching iframe via JS interop.
    /// </summary>
    private sealed class ServerFrameChannel : ILayoutFrameChannel
    {
        private readonly ServerNotebookService _owner;
        private volatile bool _isAlive = true;

        public ServerFrameChannel(string frameInstanceId, ServerNotebookService owner)
        {
            FrameInstanceId = frameInstanceId;
            _owner = owner;
        }

        public string FrameInstanceId { get; }

        public bool IsAlive => _isAlive;

        public void MarkDead() => _isAlive = false;

        public Task PostMessageAsync(string type, object? payload, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(type))
                throw new ArgumentException("Message type must be a non-empty string.", nameof(type));

            if (type.StartsWith("verso/", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Message type '{type}' is in the reserved 'verso/' namespace. Extensions must use " +
                    $"the 'ext/'-prefixed namespace (the host applies the prefix automatically).",
                    nameof(type));

            if (!_isAlive)
                throw new InvalidOperationException(
                    $"Cannot post message of type '{type}': the frame channel for " +
                    $"'{FrameInstanceId}' has been marked dead.");

            JsonElement? payloadElement = payload is null
                ? null
                : JsonSerializer.SerializeToElement(payload);

            _owner.OnLayoutFrameMessage?.Invoke(
                new LayoutFrameMessageEventArgs(FrameInstanceId, "ext/" + type, payloadElement));
            return Task.CompletedTask;
        }
    }

    private sealed record ServerActiveFrame(
        string ExtensionId,
        string LayoutId,
        ServerFrameChannel Channel,
        ILayoutLifecycleHandler? Handler);
}
