using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// Correlates protocol replies with the requests that produced them. Each request is assigned a
/// monotonically increasing id; the matching reply, echoing that id, completes the awaiting task.
/// </summary>
internal sealed class RequestCorrelator
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private int _nextId;

    /// <summary>Allocate the next request id. Ids are unique and strictly increasing.</summary>
    public int NextId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Register a pending request and return a task that completes when a reply with the same id
    /// arrives, the token cancels, or <see cref="FailAll"/> runs.
    /// </summary>
    public Task<JsonObject> RegisterAsync(int id, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
            throw new InvalidOperationException($"A request with id {id} is already pending.");

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(static state =>
            {
                var (self, key) = ((RequestCorrelator, int))state!;
                if (self._pending.TryRemove(key, out var pending))
                    pending.TrySetCanceled();
            }, (this, id));

            // Release the registration once the request settles, whatever the outcome.
            tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Complete the pending request matching <paramref name="reqId"/> with its reply. Returns
    /// <c>false</c> when no request is awaiting that id.
    /// </summary>
    public bool TryComplete(int reqId, JsonObject reply)
        => _pending.TryRemove(reqId, out var tcs) && tcs.TrySetResult(reply);

    /// <summary>
    /// Abandon a pending request that never reached the wire, for example when the send that
    /// should have carried it failed. Returns <c>false</c> when nothing is awaiting that id.
    /// </summary>
    public bool TryAbandon(int id)
        => _pending.TryRemove(id, out var tcs) && tcs.TrySetCanceled();

    /// <summary>Fail every pending request, for example when the connection drops.</summary>
    public void FailAll(Exception exception)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(exception);
        }
    }
}
