using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;

namespace Verso.PowerShell.Kernel.Host;

internal sealed class VersoPowerShellHost : PSHost
{
    private readonly VersoPowerShellHostUserInterface _ui;

    public VersoPowerShellHost(
        PowerShellHostOutputCallbackProvider outputCallbackProvider,
        PowerShellHostInputCallbackProvider inputCallbackProvider)
    {
        ArgumentNullException.ThrowIfNull(outputCallbackProvider);
        ArgumentNullException.ThrowIfNull(inputCallbackProvider);

        InstanceId = Guid.NewGuid();
        _ui = new VersoPowerShellHostUserInterface(outputCallbackProvider, inputCallbackProvider);
        PrivateData = PSObject.AsPSObject(new object());
    }

    public override string Name => "Verso PowerShell Host";

    public override Version Version { get; } = new(1, 0, 0);

    public override Guid InstanceId { get; }

    public override PSHostUserInterface UI => _ui;

    public override PSObject PrivateData { get; }

    public override CultureInfo CurrentCulture => CultureInfo.CurrentCulture;

    public override CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;

    public override void EnterNestedPrompt() =>
        throw new PSNotSupportedException("Nested PowerShell prompts are not supported by Verso.");

    public override void ExitNestedPrompt() =>
        throw new PSNotSupportedException("Nested PowerShell prompts are not supported by Verso.");

    public override void NotifyBeginApplication()
    {
    }

    public override void NotifyEndApplication()
    {
    }

    public override void SetShouldExit(int exitCode)
    {
    }
}
