namespace Verso.PowerShell.Kernel.Host;

internal delegate Task PowerShellHostOutputCallback(PowerShellHostOutput output);

internal delegate PowerShellHostOutputCallback? PowerShellHostOutputCallbackProvider();

internal delegate Task<string?> PowerShellHostInputCallback(PowerShellHostInputRequest request);

internal delegate PowerShellHostInputCallback? PowerShellHostInputCallbackProvider();
