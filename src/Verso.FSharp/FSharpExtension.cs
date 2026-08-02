using Verso.Abstractions;
using Verso.FSharp.Resources;

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
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => Strings.Extension_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
