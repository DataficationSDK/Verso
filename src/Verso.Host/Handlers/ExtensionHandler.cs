using System.Text.Json;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Extensions.Marketplace;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class ExtensionHandler
{
    // Process-scoped marketplace service and trust store, shared across notebook sessions
    // (trust is per-user, not per-notebook).
    private static readonly NuGetMarketplaceService Marketplace = new();
    private static readonly ExtensionTrustStore TrustStore = ExtensionTrustStore.Load();

    /// <summary>Exposes the shared trust store for the open flow's required-extension loading.</summary>
    internal static ExtensionTrustStore SharedTrustStore => TrustStore;

    /// <summary>Exposes the shared marketplace service for the open flow.</summary>
    internal static NuGetMarketplaceService SharedMarketplace => Marketplace;

    public static async Task<ExtensionSearchResult> HandleSearchAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExtensionSearchParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for extension/search");

        var items = await Marketplace.SearchAsync(p.Query, p.Skip, p.Take, p.IncludePrerelease, CancellationToken.None);

        var installed = new HashSet<string>(
            ns.Scaffold.Notebook.RequiredExtensions.Select(ExtensionPackageRef.ParseId),
            StringComparer.OrdinalIgnoreCase);

        return new ExtensionSearchResult
        {
            Packages = items.Select(i => new PackageSearchItemDto
            {
                Id = i.Id,
                Version = i.Version,
                Description = i.Description,
                Authors = i.Authors,
                DownloadCount = i.DownloadCount,
                IconUrl = i.IconUrl,
                ProjectUrl = i.ProjectUrl,
                IsInstalled = installed.Contains(i.Id)
            }).ToList()
        };
    }

    public static async Task<ExtensionVersionsResult> HandleVersionsAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExtensionVersionsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for extension/versions");

        var versions = await Marketplace.GetAvailableVersionsAsync(
            p.PackageId, p.IncludePrerelease, CancellationToken.None);

        return new ExtensionVersionsResult { Versions = versions.ToList() };
    }

    public static async Task<ExtensionInstallResult> HandleInstallAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExtensionInstallParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for extension/install");

        try
        {
            if (!TrustStore.IsApproved(p.PackageId, p.Version))
            {
                var consent = new[] { new ExtensionConsentInfo(p.PackageId, p.Version, "marketplace") };
                var approved = await ns.ExtensionHost.RequestExtensionConsentAsync(consent, CancellationToken.None);
                if (!approved)
                    return new ExtensionInstallResult { Success = false, ErrorMessage = "Installation was not approved." };

                TrustStore.Approve(p.PackageId, p.Version);
                TrustStore.Save();
            }
            ns.ExtensionHost.ApprovePackage(p.PackageId);

            var managedDir = ExtensionDirectoryResolver.GetDefaultManagedDir();
            var install = await Marketplace.EnsureInstalledAsync(p.PackageId, p.Version, managedDir, CancellationToken.None);

            var registered = 0;
            if (!ns.ExtensionHost.IsExtensionPackageLoaded(p.PackageId))
            {
                registered = await MarketplaceLoader.LoadAssembliesAsync(
                    ns.ExtensionHost, install.AssemblyPaths, p.PackageId);
                ns.ExtensionHost.MarkExtensionPackageLoaded(p.PackageId);
            }

            RecordRequiredExtension(ns, p.PackageId, install.ResolvedVersion);
            ns.Scaffold.Notebook.Modified = DateTimeOffset.UtcNow;

            // Loading fires extension/changed via OnExtensionLoaded; send one explicitly too so the
            // client refreshes the installed state even when nothing new registered.
            ns.SendNotification(MethodNames.ExtensionChanged, null);

            return new ExtensionInstallResult
            {
                Success = true,
                PackageId = p.PackageId,
                ResolvedVersion = install.ResolvedVersion,
                ExtensionsRegistered = registered
            };
        }
        catch (Exception ex)
        {
            return new ExtensionInstallResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public static async Task<ExtensionInstallResult> HandleInstallLocalAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExtensionInstallLocalParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for extension/installLocal");

        if (string.IsNullOrWhiteSpace(p.Path))
            return new ExtensionInstallResult { Success = false, ErrorMessage = "No file path was provided." };

        try
        {
            var managedDir = ExtensionDirectoryResolver.GetDefaultManagedDir();
            var outcome = await MarketplaceLoader.InstallLocalFileAsync(
                ns.ExtensionHost, Marketplace, TrustStore, p.Path, managedDir, CancellationToken.None);

            if (!outcome.Success)
                return new ExtensionInstallResult { Success = false, ResolvedVersion = outcome.ResolvedVersion, ErrorMessage = outcome.ErrorMessage };

            RecordRequiredExtension(ns, outcome.PackageId!, outcome.ResolvedVersion!, ExtensionSource.Local);
            ns.Scaffold.Notebook.Modified = DateTimeOffset.UtcNow;

            ns.SendNotification(MethodNames.ExtensionChanged, null);

            return new ExtensionInstallResult
            {
                Success = true,
                PackageId = outcome.PackageId,
                ResolvedVersion = outcome.ResolvedVersion,
                ExtensionsRegistered = outcome.ExtensionsRegistered
            };
        }
        catch (Exception ex)
        {
            return new ExtensionInstallResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public static object HandleUninstall(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExtensionUninstallParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for extension/uninstall");

        ns.Scaffold.Notebook.RequiredExtensions.RemoveAll(r =>
            string.Equals(ExtensionPackageRef.ParseId(r), p.PackageId, StringComparison.OrdinalIgnoreCase));

        TrustStore.Revoke(p.PackageId);
        TrustStore.Save();

        ns.Scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
        ns.SendNotification(MethodNames.ExtensionChanged, null);
        return new { success = true };
    }

    private static void RecordRequiredExtension(
        NotebookSession ns, string packageId, string resolvedVersion, ExtensionSource source = ExtensionSource.NuGet)
    {
        var refString = ExtensionPackageRef.Format(packageId, resolvedVersion, source);
        var list = ns.Scaffold.Notebook.RequiredExtensions;
        var index = list.FindIndex(r =>
            string.Equals(ExtensionPackageRef.ParseId(r), packageId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            list[index] = refString;
        else
            list.Add(refString);
    }

    public static ExtensionListResult HandleList(NotebookSession ns)
    {
        var infos = ns.ExtensionHost.GetExtensionInfos();
        return new ExtensionListResult
        {
            Extensions = infos.Select(i => new ExtensionInfoDto
            {
                ExtensionId = i.ExtensionId,
                Name = i.Name,
                Version = i.Version,
                Author = i.Author,
                Description = i.Description,
                Status = i.Status.ToString(),
                Capabilities = i.Capabilities.ToList()
            }).ToList(),
            Installed = ns.Scaffold.Notebook.RequiredExtensions
                .Select(r =>
                {
                    var (id, version, source) = ExtensionPackageRef.Parse(r);
                    return new InstalledExtensionItemDto
                    {
                        Id = id,
                        Version = version,
                        IsLocal = source == ExtensionSource.Local,
                        Capabilities = ns.ExtensionHost.GetPackageCapabilities(id)?.ToList()
                    };
                })
                .Where(e => !string.IsNullOrEmpty(e.Id))
                .ToList(),
            Sources = Marketplace.SourceNames.ToList()
        };
    }

    public static async Task<ExtensionListResult> HandleEnableAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        await ns.ExtensionHost.EnableExtensionAsync(extensionId);
        return HandleList(ns);
    }

    public static async Task<ExtensionListResult> HandleDisableAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        await ns.ExtensionHost.DisableExtensionAsync(extensionId);
        return HandleList(ns);
    }
}
