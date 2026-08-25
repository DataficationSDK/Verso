using Verso.Abstractions;
using Verso.DataFrame.Resources;

namespace Verso.DataFrame;

/// <summary>
/// Package-level metadata for the Verso.DataFrame extension.
/// </summary>
public sealed class DataFrameExtension : IExtension
{
    public string ExtensionId => "verso.dataframe";
    public string Name => "Verso.DataFrame";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Extension_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
