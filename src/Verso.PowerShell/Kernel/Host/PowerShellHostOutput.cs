namespace Verso.PowerShell.Kernel.Host;

internal sealed record PowerShellHostOutput(
    string MimeType,
    string Content,
    bool IsError = false,
    string? ErrorName = null,
    /// <summary>
    /// When set, this output revises the block of that name rather than adding another. Progress
    /// uses it so a long-running command shows one indicator that advances.
    /// </summary>
    string? BlockId = null);
