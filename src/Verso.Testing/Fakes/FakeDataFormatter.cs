using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// <see cref="IDataFormatter"/> test double for auto-registration testing.
/// </summary>
public sealed class FakeDataFormatter : IDataFormatter
{
    public FakeDataFormatter(
        string extensionId = "com.test.formatter",
        string name = "Fake Formatter",
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

    public IReadOnlyList<Type> SupportedTypes => new[] { typeof(string) };
    public int Priority => 0;

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

    public bool CanFormat(object value, IFormatterContext context) => value is string;

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
        => Task.FromResult(new CellOutput("text/plain", value?.ToString() ?? ""));
}
