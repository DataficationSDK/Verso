namespace Verso.Abstractions;

/// <summary>
/// Describes a variable stored in a kernel's variable store.
/// </summary>
/// <param name="Name">The name of the variable.</param>
/// <param name="Value">The current value of the variable, or <see langword="null"/> if it has not been assigned.</param>
/// <param name="Type">The CLR type of the variable.</param>
/// <param name="KernelId">The identifier of the kernel that owns this variable, or <see langword="null"/> if it belongs to the default kernel.</param>
public sealed record VariableDescriptor(
    string Name,
    object? Value,
    Type Type,
    string? KernelId = null);
