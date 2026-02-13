namespace Verso.Abstractions;

/// <summary>
/// Marker attribute used to identify classes as Verso extensions for discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class VersoExtensionAttribute : Attribute
{
}
