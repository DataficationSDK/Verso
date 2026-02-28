using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Test double implementing <see cref="IDataFormatter"/> + <see cref="ICellInteractionHandler"/>
/// for testing bidirectional cell interaction registration and dispatch.
/// </summary>
public sealed class FakeCellInteractionHandler : IDataFormatter, ICellInteractionHandler
{
    public FakeCellInteractionHandler(
        string extensionId = "com.test.interaction",
        string name = "Fake Interaction Handler",
        string version = "1.0.0")
    {
        ExtensionId = extensionId;
        Name = name;
        Version = version;
    }

    public string ExtensionId { get; }
    public string Name { get; }
    public string Version { get; }
    public string? Author => null;
    public string? Description => null;

    // --- IDataFormatter ---

    public IReadOnlyList<Type> SupportedTypes => new[] { typeof(string) };
    public int Priority => 0;

    public bool CanFormat(object value, IFormatterContext context) => value is string;

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
        => Task.FromResult(new CellOutput("text/plain", value?.ToString() ?? ""));

    // --- ICellInteractionHandler ---

    public List<CellInteractionContext> ReceivedInteractions { get; } = new();
    public string? ResponseToReturn { get; set; }

    public Task<string?> OnCellInteractionAsync(CellInteractionContext context)
    {
        ReceivedInteractions.Add(context);
        return Task.FromResult(ResponseToReturn);
    }

    // --- Lifecycle tracking ---

    public int OnLoadedCallCount { get; private set; }
    public int OnUnloadedCallCount { get; private set; }

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        OnLoadedCallCount++;
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync()
    {
        OnUnloadedCallCount++;
        return Task.CompletedTask;
    }
}
