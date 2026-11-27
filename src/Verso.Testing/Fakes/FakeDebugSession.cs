using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Scripted <see cref="IDebugSession"/> test double. It holds breakpoints, records every stop a
/// kernel reports, and answers each one from a queue of actions, so a test of an
/// <see cref="IDebuggableKernel"/> reads as a list of steps and a list of expected stops.
/// </summary>
public sealed class FakeDebugSession : IDebugSession
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, int[]> _breakpoints = new();
    private readonly Queue<DebugResumeAction> _answers = new();

    /// <summary>
    /// Builds a session that answers stops with the given actions, in order. Once they run out,
    /// every further stop is answered with <see cref="DebugResumeAction.Continue"/>.
    /// </summary>
    public FakeDebugSession(params DebugResumeAction[] answers)
    {
        foreach (var answer in answers)
            _answers.Enqueue(answer);
    }

    /// <inheritdoc />
    public event Action? BreakpointsChanged;

    /// <summary>
    /// Every stop reported so far, oldest first.
    /// </summary>
    public List<DebugStop> Stops { get; } = new();

    /// <summary>
    /// Called on each stop, after it is recorded and before it is answered. A test uses this to
    /// change breakpoints or cancel the cell while the cell is stopped.
    /// </summary>
    public Action<DebugStop>? OnStop { get; set; }

    /// <summary>
    /// Replaces the breakpoints of a cell and raises <see cref="BreakpointsChanged"/>.
    /// </summary>
    public FakeDebugSession SetBreakpoints(Guid cellId, params int[] lines)
    {
        lock (_lock) _breakpoints[cellId] = lines;
        BreakpointsChanged?.Invoke();
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<int> GetBreakpoints(Guid cellId)
    {
        lock (_lock)
            return _breakpoints.TryGetValue(cellId, out var lines) ? lines : Array.Empty<int>();
    }

    /// <inheritdoc />
    public Task<DebugResumeAction> StopAsync(DebugStop stop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stop);

        DebugResumeAction answer;
        lock (_lock)
        {
            Stops.Add(stop);
            answer = _answers.Count > 0 ? _answers.Dequeue() : DebugResumeAction.Continue;
        }

        OnStop?.Invoke(stop);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(answer);
    }
}
