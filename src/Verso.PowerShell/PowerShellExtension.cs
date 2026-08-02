using Verso.Abstractions;
using Verso.PowerShell.Resources;

namespace Verso.PowerShell;

public sealed class PowerShellExtension : IExtension
{
    public string ExtensionId => "verso.powershell";
    public string Name => "Verso.PowerShell";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => Strings.Extension_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
