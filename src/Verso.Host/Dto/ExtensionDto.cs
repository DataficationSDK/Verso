namespace Verso.Host.Dto;

// --- Extension Management ---

public sealed class ExtensionListResult
{
    public List<ExtensionInfoDto> Extensions { get; set; } = new();

    /// <summary>Packages the notebook requires, for the panel's installed list and uninstall affordance.</summary>
    public List<InstalledExtensionItemDto> Installed { get; set; } = new();

    /// <summary>The configured package sources, in search order, so the panel can say where
    /// results came from.</summary>
    public List<string> Sources { get; set; } = new();
}

public sealed class InstalledExtensionItemDto
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
    public bool IsLocal { get; set; }

    /// <summary>
    /// What the package contributed once loaded. Null when the package has not loaded in this
    /// session, empty when it loaded and registered no extension point. The panel says
    /// different things about those two, so the null must survive the round trip.
    /// </summary>
    public List<string>? Capabilities { get; set; }

    /// <summary>
    /// The package's own icon as a <c>data:</c> URI, read from the installed copy on disk. Null
    /// when the package ships no icon, in which case the panel falls back to a lettered tile.
    /// </summary>
    public string? IconDataUri { get; set; }
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

public sealed class ExtensionVersionsParams
{
    public string NotebookId { get; set; } = "";
    public string PackageId { get; set; } = "";
    public bool IncludePrerelease { get; set; }
}

public sealed class ExtensionVersionsResult
{
    public List<string> Versions { get; set; } = new();
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

    /// <summary>
    /// Resolved package id of the installed extension. For local sideloads the client cannot know
    /// this up front, so it is reported back to let the client clear any stale unavailable mark.
    /// </summary>
    public string? PackageId { get; set; }

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
