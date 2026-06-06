namespace Verso.Host.Dto;

// --- Extension Management ---

public sealed class ExtensionListResult
{
    public List<ExtensionInfoDto> Extensions { get; set; } = new();

    /// <summary>Packages the notebook requires, for the panel's installed list and uninstall affordance.</summary>
    public List<InstalledExtensionItemDto> Installed { get; set; } = new();
}

public sealed class InstalledExtensionItemDto
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public bool IsLocal { get; set; }
}

public sealed class ExtensionInfoDto
{
    public string ExtensionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
}

public sealed class ExtensionToggleParams
{
    public string ExtensionId { get; set; } = "";
}

// --- Extension Marketplace ---

public sealed class ExtensionSearchParams
{
    public string NotebookId { get; set; } = "";
    public string Query { get; set; } = "";
    public int Skip { get; set; }
    public int Take { get; set; } = 20;
    public bool IncludePrerelease { get; set; }
}

public sealed class ExtensionSearchResult
{
    public List<PackageSearchItemDto> Packages { get; set; } = new();
}

public sealed class PackageSearchItemDto
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public long? DownloadCount { get; set; }
    public string? IconUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public bool IsInstalled { get; set; }
}

public sealed class ExtensionInstallParams
{
    public string NotebookId { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string? Version { get; set; }
}

public sealed class ExtensionInstallResult
{
    public bool Success { get; set; }
    public string? ResolvedVersion { get; set; }
    public string? ErrorMessage { get; set; }
    public int ExtensionsRegistered { get; set; }
}

public sealed class ExtensionInstallLocalParams
{
    public string NotebookId { get; set; } = "";

    /// <summary>Absolute path to the local <c>.dll</c> or <c>.nupkg</c> on the host machine.</summary>
    public string Path { get; set; } = "";
}

public sealed class ExtensionUninstallParams
{
    public string NotebookId { get; set; } = "";
    public string PackageId { get; set; } = "";
}
