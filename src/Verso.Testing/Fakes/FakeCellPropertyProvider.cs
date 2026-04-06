using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// <see cref="ICellPropertyProvider"/> test double for auto-registration testing.
/// </summary>
public sealed class FakeCellPropertyProvider : ICellPropertyProvider
{
    public FakeCellPropertyProvider(
        string extensionId = "com.test.propertyprovider",
        string name = "Fake Property Provider",
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

    public int Order => 100;

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

    public bool AppliesTo(CellModel cell, ICellRenderContext context) => true;

    public Task<PropertySection> GetPropertiesSectionAsync(CellModel cell, ICellRenderContext context)
    {
        var section = new PropertySection("Fake Section", null, Array.Empty<PropertyField>());
        return Task.FromResult(section);
    }

    public Task OnPropertyChangedAsync(CellModel cell, string propertyName, object? value, ICellRenderContext context)
        => Task.CompletedTask;
}
