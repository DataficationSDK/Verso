using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Extensions;
using Verso.Extensions.Marketplace;

namespace Verso.Blazor.Services;

/// <summary>
/// Extension marketplace surface for <see cref="ServerNotebookService"/>: NuGet search,
/// per-notebook install/uninstall, and loading a notebook's declared required extensions
/// at open time.
/// </summary>
public sealed partial class ServerNotebookService
{
    private readonly NuGetMarketplaceService _marketplace = new();
    private readonly ExtensionTrustStore _trustStore = ExtensionTrustStore.Load();

    /// <inheritdoc />
    public bool IsMarketplaceSupported => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PackageSearchResultDto>> SearchExtensionsAsync(
        string query, int skip, int take, bool includePrerelease, CancellationToken ct)
    {
        var installed = new HashSet<string>(
            _scaffold?.Notebook.RequiredExtensions.Select(ExtensionPackageRef.ParseId)
                ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var items = await _marketplace.SearchAsync(query, skip, take, includePrerelease, ct);

        return items.Select(i => new PackageSearchResultDto(
            i.Id,
            i.Version,
            i.Description,
            i.Authors,
            i.DownloadCount,
            i.IconUrl,
            i.ProjectUrl,
            installed.Contains(i.Id))).ToList();
    }

    /// <inheritdoc />
    public async Task<PackageInstallResultDto> InstallExtensionAsync(
        string packageId, string? version, CancellationToken ct)
    {
        if (_scaffold is null || _extensionHost is null)
            return new PackageInstallResultDto(false, null, "No notebook is open.", 0);

        try
        {
            if (!_trustStore.IsApproved(packageId, version))
            {
                var consent = new List<ExtensionConsentInfo> { new(packageId, version, "marketplace") };
                var approved = await _extensionHost.RequestExtensionConsentAsync(consent, ct);
                if (!approved)
                    return new PackageInstallResultDto(false, null, "Installation was not approved.", 0);

                _trustStore.Approve(packageId, version);
                _trustStore.Save();
            }
            _extensionHost.ApprovePackage(packageId);

            var managedDir = ExtensionDirectoryResolver.GetDefaultManagedDir();
            var install = await _marketplace.EnsureInstalledAsync(packageId, version, managedDir, ct);

            var registered = 0;
            if (!_extensionHost.IsExtensionPackageLoaded(packageId))
            {
                registered = await MarketplaceLoader.LoadAssembliesAsync(_extensionHost, install.AssemblyPaths);
                _extensionHost.MarkExtensionPackageLoaded(packageId);
            }

            RecordRequiredExtension(packageId, install.ResolvedVersion);

            _scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
            OnExtensionStatusChanged?.Invoke();
            OnNotebookChanged?.Invoke();

            return new PackageInstallResultDto(true, install.ResolvedVersion, null, registered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PackageInstallResultDto(false, null, ex.Message, 0);
        }
    }

    /// <inheritdoc />
    public Task UninstallExtensionAsync(string packageId)
    {
        if (_scaffold is null)
            return Task.CompletedTask;

        _scaffold.Notebook.RequiredExtensions.RemoveAll(r =>
            string.Equals(ExtensionPackageRef.ParseId(r), packageId, StringComparison.OrdinalIgnoreCase));

        // Revoke trust so reinstalling prompts again, and so a stale approval doesn't auto-load
        // it on the next open now that it is no longer required.
        _trustStore.Revoke(packageId);
        _trustStore.Save();

        _scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
        OnExtensionStatusChanged?.Invoke();
        OnNotebookChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves, downloads if needed, and loads the extensions a notebook declares in its
    /// required-extensions list. A single batched consent prompt covers everything not yet
    /// trusted. Invoked early in the open flow so required layout engines are available
    /// before the active layout is chosen.
    /// </summary>
    private Task LoadRequiredExtensionsAsync(NotebookModel notebook)
    {
        if (_extensionHost is null)
            return Task.CompletedTask;

        return MarketplaceLoader.LoadRequiredAsync(
            _extensionHost,
            notebook,
            _trustStore,
            _marketplace,
            ExtensionDirectoryResolver.GetDefaultManagedDir(),
            promptForConsent: true);
    }

    /// <summary>Records or updates a required-extension entry, pinning the resolved version.</summary>
    private void RecordRequiredExtension(string packageId, string resolvedVersion)
    {
        if (_scaffold is null)
            return;

        var refString = ExtensionPackageRef.Format(packageId, resolvedVersion);
        var list = _scaffold.Notebook.RequiredExtensions;
        var index = list.FindIndex(r =>
            string.Equals(ExtensionPackageRef.ParseId(r), packageId, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            list[index] = refString;
        else
            list.Add(refString);
    }
}
