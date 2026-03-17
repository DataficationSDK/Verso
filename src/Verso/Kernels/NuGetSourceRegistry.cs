namespace Verso.Kernels;

/// <summary>
/// Session-scoped registry of NuGet package sources added via <c>#i</c> directives.
/// Stored in the <see cref="Verso.Abstractions.IVariableStore"/> so both the C# kernel
/// and the <c>#!nuget</c> magic command share the same source list within a session.
/// </summary>
internal sealed class NuGetSourceRegistry
{
    internal const string StoreKey = "__verso_nuget_sources";

    private readonly List<string> _sources = new();

    public IReadOnlyList<string> Sources => _sources;

    public void AddSource(string source)
    {
        if (!_sources.Contains(source, StringComparer.OrdinalIgnoreCase))
            _sources.Add(source);
    }
}
