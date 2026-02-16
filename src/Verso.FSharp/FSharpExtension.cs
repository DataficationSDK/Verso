using Verso.Abstractions;

namespace Verso.FSharp;

/// <summary>
/// Metadata descriptor for the Verso.FSharp extension package. Individual components
/// (kernel, formatter, post-processor) are discovered via their own
/// <see cref="VersoExtensionAttribute"/> markers. This class exists for programmatic
/// package-level metadata queries, matching the Verso.Ado pattern.
/// </summary>
public sealed class FSharpExtension : IExtension
{
    public string ExtensionId => "verso.fsharp";
    public string Name => "Verso.FSharp";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "F# Interactive language kernel extension for Verso notebooks.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
