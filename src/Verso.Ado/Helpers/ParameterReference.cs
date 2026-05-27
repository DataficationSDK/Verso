namespace Verso.Ado.Helpers;

/// <summary>
/// A reference to a parameter token (e.g. <c>@name</c>) found in SQL text by
/// <see cref="SqlParameterScanner"/>. Offsets are character positions in the
/// scanned string.
/// </summary>
/// <param name="Name">Parameter name without the leading <c>@</c>.</param>
/// <param name="Offset">Character offset of the leading <c>@</c> in the scanned string.</param>
/// <param name="Length">Total length of the token including the leading <c>@</c>.</param>
internal sealed record ParameterReference(string Name, int Offset, int Length);
