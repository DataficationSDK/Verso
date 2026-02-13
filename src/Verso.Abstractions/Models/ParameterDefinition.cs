namespace Verso.Abstractions;

/// <summary>
/// Defines a parameter accepted by a magic command.
/// </summary>
/// <param name="Name">The parameter name as it appears in command usage (e.g. "timeout").</param>
/// <param name="Description">A human-readable description of the parameter's purpose.</param>
/// <param name="ParameterType">The CLR type expected for the parameter value.</param>
/// <param name="IsRequired">Indicates whether the parameter must be supplied. Defaults to <see langword="false"/>.</param>
/// <param name="DefaultValue">The default value used when the parameter is not supplied. Defaults to <see langword="null"/>.</param>
public sealed record ParameterDefinition(
    string Name,
    string Description,
    Type ParameterType,
    bool IsRequired = false,
    object? DefaultValue = null);
