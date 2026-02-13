namespace Verso.Extensions;

/// <summary>
/// Exception thrown when one or more extensions fail validation or loading.
/// </summary>
public sealed class ExtensionLoadException : Exception
{
    public ExtensionLoadException(IReadOnlyList<ExtensionValidationError> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public ExtensionLoadException(IReadOnlyList<ExtensionValidationError> errors, Exception innerException)
        : base(FormatMessage(errors), innerException)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>
    /// The validation errors that caused the load to fail.
    /// </summary>
    public IReadOnlyList<ExtensionValidationError> Errors { get; }

    private static string FormatMessage(IReadOnlyList<ExtensionValidationError> errors)
    {
        if (errors is null || errors.Count == 0)
            return "Extension loading failed.";

        if (errors.Count == 1)
            return $"Extension loading failed: {errors[0].Message}";

        return $"Extension loading failed with {errors.Count} errors: " +
               string.Join("; ", errors.Select(e => e.Message));
    }
}
