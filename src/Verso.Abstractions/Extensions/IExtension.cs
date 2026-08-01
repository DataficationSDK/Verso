namespace Verso.Abstractions;

/// <summary>
/// Base interface for all Verso extensions. Provides identity, metadata, and lifecycle hooks
/// that the extension host calls when loading and unloading an extension.
/// </summary>
public interface IExtension
{
    /// <summary>
    /// Unique identifier for this extension, typically in reverse-domain format (e.g. "com.example.myext").
    /// </summary>
    string ExtensionId { get; }

    /// <summary>
    /// Human-readable display name shown in the UI and extension listings.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Semantic version string for the extension (e.g. "1.2.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Optional author or publisher name.
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// Optional short description of what the extension provides.
    /// </summary>
    /// <remarks>
    /// One class is one contribution, so for a <see cref="IToolbarAction"/> or an
    /// <see cref="INotebookPanel"/> this doubles as the description of that button or
    /// panel: hosts show it beneath the name in the control's tooltip. Since those
    /// controls are often an icon with no label, a sentence saying what the thing does
    /// is worth more here than a restatement of its name.
    /// </remarks>
    string? Description { get; }

    /// <summary>
    /// Called by the host when the extension is loaded. Use this to register services,
    /// subscribe to events, and perform any asynchronous initialization.
    /// </summary>
    /// <param name="context">Host context providing access to Verso services and registration APIs.</param>
    /// <returns>A task that completes when the extension has finished loading.</returns>
    Task OnLoadedAsync(IExtensionHostContext context);

    /// <summary>
    /// Called by the host when the extension is being unloaded. Use this to release
    /// resources, unsubscribe from events, and perform cleanup.
    /// </summary>
    /// <returns>A task that completes when the extension has finished unloading.</returns>
    Task OnUnloadedAsync();
}
