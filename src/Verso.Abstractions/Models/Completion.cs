namespace Verso.Abstractions;

/// <summary>
/// Represents a single code-completion item offered by a kernel's language service.
/// </summary>
/// <param name="DisplayText">The text shown to the user in the completion list.</param>
/// <param name="InsertText">The text inserted into the editor when the completion is accepted.</param>
/// <param name="Kind">The kind of symbol this completion represents (e.g. "Method", "Property", "Keyword").</param>
/// <param name="Description">An optional description or documentation string displayed alongside the completion.</param>
/// <param name="SortText">An optional string used to control the sort order in the completion list. When <see langword="null"/>, <paramref name="DisplayText"/> is used.</param>
public sealed record Completion(
    string DisplayText,
    string InsertText,
    string Kind,
    string? Description = null,
    string? SortText = null);
