namespace Verso.Extensions;

/// <summary>
/// Describes a single validation failure encountered when loading an extension.
/// </summary>
/// <param name="ExtensionId">The extension identifier that failed validation, or null if the ID itself is missing.</param>
/// <param name="ErrorCode">A stable, machine-readable code such as "MISSING_ID" or "DUPLICATE_ID".</param>
/// <param name="Message">A human-readable description of the validation failure.</param>
public sealed record ExtensionValidationError(string? ExtensionId, string ErrorCode, string Message);
