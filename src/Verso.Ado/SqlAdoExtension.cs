using Verso.Abstractions;

namespace Verso.Ado;

/// <summary>
/// Entry point extension for Verso.Ado — SQL database connectivity for Verso notebooks.
/// Individual components (kernel, magic commands) are discovered via their own
/// <see cref="VersoExtensionAttribute"/> markers.
/// </summary>
[VersoExtension]
public sealed class SqlAdoExtension : IExtension
{
    public string ExtensionId => "verso.ado";
    public string Name => "Verso.Ado";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "SQL database connectivity extension for Verso notebooks.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
