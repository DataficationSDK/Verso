namespace Verso.PowerShell.Kernel.Host;

internal sealed record PowerShellHostInputRequest(
    string Prompt,
    bool IsPassword = false);
