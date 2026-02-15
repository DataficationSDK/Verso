using Verso.Abstractions;

namespace Verso.Ado;

/// <summary>
/// Metadata descriptor for the Verso.Ado extension package. Individual components
/// (cell type, formatter, magic commands, toolbar actions) are discovered via their
/// own <see cref="VersoExtensionAttribute"/> markers. This class is not auto-loaded
/// by the host; it exists for programmatic package-level metadata queries.
/// </summary>
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
