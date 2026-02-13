using Verso.Abstractions;

namespace Verso.Tests.Helpers;

/// <summary>
/// Bare <see cref="IExtension"/> test double with no capability interface.
/// Tracks lifecycle call counts.
/// </summary>
public sealed class FakeExtension : IExtension
{
    public FakeExtension(
        string extensionId = "com.test.fake",
        string name = "Fake Extension",
        string version = "1.0.0",
        string? author = null,
        string? description = null)
    {
        ExtensionId = extensionId;
        Name = name;
        Version = version;
        Author = author;
        Description = description;
    }

    public string ExtensionId { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }

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
