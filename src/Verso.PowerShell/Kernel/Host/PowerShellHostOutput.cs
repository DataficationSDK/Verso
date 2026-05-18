namespace Verso.PowerShell.Kernel.Host;

internal sealed record PowerShellHostOutput(
    string MimeType,
    string Content,
    bool IsError = false,
    string? ErrorName = null);
