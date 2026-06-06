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

        foreach (var (id, version) in refs)
        {
            if (extensionHost.IsExtensionPackageLoaded(id))
                continue;
            if (!trustStore.IsApproved(id, version))
                continue; // not trusted (consent denied or unavailable) — skip

            try
            {
                var install = await marketplace.EnsureInstalledAsync(id, version, managedDir, ct);
                await LoadAssembliesAsync(extensionHost, install.AssemblyPaths);
                extensionHost.MarkExtensionPackageLoaded(id);
                extensionHost.ApprovePackage(id);
            }
            catch
            {
                // A required extension that fails to resolve or load must not block the open.
            }
        }
    }
}
