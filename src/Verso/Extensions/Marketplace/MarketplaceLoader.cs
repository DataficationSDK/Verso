using Verso.Abstractions;

namespace Verso.Extensions.Marketplace;

/// <summary>
/// Shared logic for loading marketplace-installed extensions into an <see cref="ExtensionHost"/>.
/// Used by both the in-process Blazor Server host and the out-of-process JSON-RPC host so the
/// trust, resolve, and load behavior stays identical across them.
/// </summary>
public static class MarketplaceLoader
{
    /// <summary>
    /// Loads every assembly produced by an install into the host, returning the number of
    /// extensions that registered. Dependency assemblies and native libraries register
    /// nothing and are skipped without error.
    /// </summary>
    public static async Task<int> LoadAssembliesAsync(
        ExtensionHost extensionHost, IReadOnlyList<string> assemblyPaths)
    {
        var registered = 0;
        foreach (var dll in assemblyPaths)
        {
            try
            {
                var before = extensionHost.GetLoadedExtensions().Count;
                await extensionHost.LoadFromAssemblyAsync(dll);
                registered += extensionHost.GetLoadedExtensions().Count - before;
            }
            catch (ExtensionLoadException)
            {
                // Dependency assembly or a duplicate id — not a loadable extension here.
            }
            catch (BadImageFormatException)
            {
                // Native or non-.NET assembly.
            }
        }
        return registered;
    }

    /// <summary>
    /// Resolves, downloads if needed, and loads the extensions a notebook declares in its
    /// required-extensions list. When <paramref name="promptForConsent"/> is true a single
    /// batched consent request covers everything not already trusted; when false only
    /// already-trusted packages load (used where no consent channel is available yet, such
    /// as before the host session exists). A package whose consent is missing is skipped.
    /// </summary>
    public static async Task LoadRequiredAsync(
        ExtensionHost extensionHost,
        NotebookModel notebook,
        ExtensionTrustStore trustStore,
        NuGetMarketplaceService marketplace,
        string managedDir,
        bool promptForConsent,
        CancellationToken ct = default)
    {
        if (notebook.RequiredExtensions.Count == 0)
            return;

        var refs = notebook.RequiredExtensions
            .Select(ExtensionPackageRef.Parse)
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToList();

        if (promptForConsent)
        {
            var needConsent = refs
                .Where(p => !trustStore.IsApproved(p.Id, p.Version))
                .Select(p => new ExtensionConsentInfo(p.Id, p.Version, "notebook required extensions"))
                .ToList();

            if (needConsent.Count > 0)
            {
                var approved = await extensionHost.RequestExtensionConsentAsync(needConsent, ct);
                if (approved)
                {
                    foreach (var c in needConsent)
                        trustStore.Approve(c.PackageId, c.Version);
                    trustStore.Save();
                }
            }
        }

        foreach (var (id, version, source) in refs)
        {
            if (extensionHost.IsExtensionPackageLoaded(id))
                continue;
            if (!trustStore.IsApproved(id, version))
                continue; // not trusted (consent denied or unavailable) — skip

            try
            {
                IReadOnlyList<string>? assemblyPaths;
                if (source == ExtensionSource.Local)
                {
                    // A sideloaded file can't be re-fetched; load it from the managed directory
                    // only. If it isn't present (e.g. the notebook was opened on another machine),
                    // skip it — there's nothing to download.
                    assemblyPaths = marketplace.TryResolveInstalled(id, version, managedDir)?.AssemblyPaths;
                    if (assemblyPaths is null)
                        continue;
                }
                else
                {
                    assemblyPaths = (await marketplace.EnsureInstalledAsync(id, version, managedDir, ct)).AssemblyPaths;
                }

                await LoadAssembliesAsync(extensionHost, assemblyPaths);
                extensionHost.MarkExtensionPackageLoaded(id);
                extensionHost.ApprovePackage(id);
            }
            catch
            {
                // A required extension that fails to resolve or load must not block the open.
            }
        }
    }

    /// <summary>
    /// The outcome of installing a sideloaded local extension file: the derived package id and
    /// resolved version on success, or an error message on failure (including a declined consent
    /// prompt).
    /// </summary>
    public sealed record LocalInstallOutcome(
        bool Success,
        string? PackageId,
        string? ResolvedVersion,
        string? ErrorMessage,
        int ExtensionsRegistered);

    /// <summary>
    /// Installs a sideloaded local extension file (a <c>.dll</c> or <c>.nupkg</c>) into the
    /// managed directory and loads it. The package identity is read from the file first so the
    /// consent prompt names the real extension; on approval trust is persisted, the file is
    /// installed, and its assemblies are loaded. The caller is responsible for recording the
    /// result in the notebook's required extensions and firing change events, since notebook
    /// mutation differs between hosts.
    /// </summary>
    public static async Task<LocalInstallOutcome> InstallLocalFileAsync(
        ExtensionHost extensionHost,
        NuGetMarketplaceService marketplace,
        ExtensionTrustStore trustStore,
        string localFilePath,
        string managedDir,
        CancellationToken ct = default)
    {
        var identity = NuGetMarketplaceService.PeekLocalIdentity(localFilePath);
        if (identity is null)
            return new LocalInstallOutcome(false, null, null, "Unsupported file. Choose a .dll or .nupkg.", 0);

        var (id, version) = identity.Value;

        if (!trustStore.IsApproved(id, version))
        {
            var consent = new[] { new ExtensionConsentInfo(id, version, $"local file: {Path.GetFileName(localFilePath)}") };
            var approved = await extensionHost.RequestExtensionConsentAsync(consent, ct);
            if (!approved)
                return new LocalInstallOutcome(false, id, version, "Installation was not approved.", 0);

            trustStore.Approve(id, version);
            trustStore.Save();
        }
        extensionHost.ApprovePackage(id);

        LocalInstallResult install;
        try
        {
            install = await marketplace.InstallFromFileAsync(localFilePath, managedDir, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LocalInstallOutcome(false, id, version, ex.Message, 0);
        }

        var registered = 0;
        if (!extensionHost.IsExtensionPackageLoaded(install.PackageId))
        {
            registered = await LoadAssembliesAsync(extensionHost, install.AssemblyPaths);
            extensionHost.MarkExtensionPackageLoaded(install.PackageId);
        }

        return new LocalInstallOutcome(true, install.PackageId, install.ResolvedVersion, null, registered);
    }
}
