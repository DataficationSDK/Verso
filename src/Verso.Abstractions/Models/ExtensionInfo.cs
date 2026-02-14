namespace Verso.Abstractions;

/// <summary>
/// Describes a loaded extension, including its metadata, current status, and capability list.
/// </summary>
/// <param name="ExtensionId">The unique identifier of the extension.</param>
/// <param name="Name">The human-readable display name.</param>
/// <param name="Version">The semantic version string.</param>
/// <param name="Author">The extension author, or <see langword="null"/> if not specified.</param>
/// <param name="Description">A brief description, or <see langword="null"/> if not specified.</param>
/// <param name="Status">Whether the extension is currently enabled or disabled.</param>
/// <param name="Capabilities">The list of capability interface names implemented by the extension.</param>
public sealed record ExtensionInfo(
    string ExtensionId,
    string Name,
    string Version,
    string? Author,
    string? Description,
    ExtensionStatus Status,
    IReadOnlyList<string> Capabilities);
