using Verso.Abstractions;
using Verso.Resources;

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
    /// nothing and are skipped without error. When <paramref name="packageId"/> is supplied,
    /// what registered is attributed to that package so the extension panel can report what
    /// the package added, including that it added nothing.
    /// </summary>
    public static async Task<int> LoadAssembliesAsync(
        ExtensionHost extensionHost, IReadOnlyList<string> assemblyPaths, string? packageId = null)
    {
        var registered = 0;
        var registeredIds = packageId is null ? null : new List<string>();

        foreach (var dll in assemblyPaths)
        {
            try
            {
                var before = extensionHost.GetLoadedExtensions();
                await extensionHost.LoadFromAssemblyAsync(dll);
                var after = extensionHost.GetLoadedExtensions();

                registered += after.Count - before.Count;
                if (registeredIds is not null && after.Count > before.Count)
                {
                    var known = new HashSet<string>(
                        before.Select(e => e.ExtensionId), StringComparer.OrdinalIgnoreCase);
                    registeredIds.AddRange(
                        after.Where(e => !known.Contains(e.ExtensionId)).Select(e => e.ExtensionId));
                }
            }
            catch (ExtensionLoadException ex) when (
                ex.Errors.All(e => e.ErrorCode != "INCOMPATIBLE_VERSION"))
            {
                // Dependency assembly or a duplicate id: not a loadable extension here.
                // An INCOMPATIBLE_VERSION rejection is deliberately left to propagate;
                // swallowing it would report a successful install that loaded nothing.
            }
            catch (BadImageFormatException)
            {
                // Native or non-.NET assembly.
            }
        }

        // Attributed even when the list is empty: "this package registered nothing" is the
        // fact the panel needs to warn on, and it is only knowable here.
        if (packageId is not null && registeredIds is not null)
            extensionHost.AttributeExtensionsToPackage(packageId, registeredIds);

        return registered;
    }

    /// <summary>
    /// Resolves, downloads if needed, and loads the extensions a notebook declares in its
    /// required-extensions list. When <paramref name="promptForConsent"/> is true a single
    /// batched consent request covers everything not already trusted, and anything that still
    /// cannot be loaded is reported through <see cref="ExtensionHost.ReportUnavailableExtensionsAsync"/>
    /// so the host can show a non-fatal notice. When false only already-pinned, already-trusted
    /// packages load and nothing is prompted or reported (used before the host session exists,
    /// where no consent channel is available yet); unpinned references are deferred to the
    /// later consent pass.
    /// </summary>
    /// <remarks>
    /// An unpinned ("latest") reference is resolved to a concrete version against the feed first,
    /// so consent and trust are pinned to that exact version. A one-time approval therefore does
    /// not silently trust a later version: if latest has moved since the last approval, consent
    /// is requested again before the new version loads. Cancellation propagates; other failures
    /// are collected rather than thrown, so a missing extension never blocks the open.
    /// </remarks>
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

        // Resolve the concrete version each reference would load. A pinned reference passes
        // through unchanged; an unpinned NuGet reference is resolved to the latest stable so
        // it can be pinned on approval. The pre-session pass (no consent channel) stays fast
        // and offline: an unpinned reference resolves against the highest approved copy
        // already on disk, so a previously installed extension registers before the layout
        // is first resolved; anything needing a feed or consent defers to the consent pass.
        var plans = new List<ResolvedRef>(refs.Count);
        foreach (var (id, version, source) in refs)
        {
            if (extensionHost.IsExtensionPackageLoaded(id))
                continue;

            var effectiveVersion = version;
            if (effectiveVersion is null && source != ExtensionSource.Local)
            {
                if (!promptForConsent)
                {
                    var installed = marketplace.TryResolveInstalledLatest(id, managedDir);
                    if (installed is null || !trustStore.IsApproved(id, installed.ResolvedVersion))
                        continue; // nothing local and approved — defer to the consent pass
                    effectiveVersion = installed.ResolvedVersion;
                }
                else
                {
                    try
                    {
                        effectiveVersion = await marketplace.ResolveVersionAsync(id, null, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        effectiveVersion = null; // offline / not found — handled by the load fallback
                    }
                }
            }

            plans.Add(new ResolvedRef(id, version, effectiveVersion, source));
        }

        if (plans.Count == 0)
            return;

        // Prompt once for everything not already approved at the version it would load, and
        // pin the approval to that concrete version.
        if (promptForConsent)
        {
            var needConsent = plans
                .Where(p => p.EffectiveVersion is not null && !trustStore.IsApproved(p.Id, p.EffectiveVersion))
                .Select(p => new ExtensionConsentInfo(p.Id, p.EffectiveVersion, Strings.Consent_Source_RequiredExtensions))
                .ToList();

            if (needConsent.Count > 0)
            {
                var approved = await extensionHost.RequestExtensionConsentAsync(needConsent, ct).ConfigureAwait(false);
                if (approved)
                {
                    foreach (var c in needConsent)
                        trustStore.Approve(c.PackageId, c.Version);
                    trustStore.Save();
                }
            }
        }

        var unavailable = new List<UnavailableExtensionInfo>();
        foreach (var plan in plans)
        {
            if (extensionHost.IsExtensionPackageLoaded(plan.Id))
                continue;

            try
            {
                IReadOnlyList<string>? assemblyPaths;

                if (plan.Source != ExtensionSource.Local && plan.EffectiveVersion is null)
                {
                    // Latest could not be resolved (offline or not found). Fall back to the
                    // highest copy already on disk so a previously loaded notebook still opens.
                    var fallback = marketplace.TryResolveInstalledLatest(plan.Id, managedDir);
                    if (fallback is null || !trustStore.IsApproved(plan.Id, fallback.ResolvedVersion))
                    {
                        Report(unavailable, promptForConsent, plan.Id, plan.OriginalVersion,
                            "could not reach the package source and no approved copy is installed");
                        continue;
                    }
                    assemblyPaths = fallback.AssemblyPaths;
                }
                else
                {
                    var loadVersion = plan.Source == ExtensionSource.Local
                        ? plan.OriginalVersion
                        : plan.EffectiveVersion;
                    if (loadVersion is null)
                        continue; // malformed reference — nothing to load

                    if (!trustStore.IsApproved(plan.Id, loadVersion))
                    {
                        Report(unavailable, promptForConsent, plan.Id, loadVersion,
                            "permission to load it was not granted");
                        continue;
                    }

                    // A sideloaded file can't be re-fetched, so it loads from the managed
                    // directory only; a NuGet reference is downloaded if not already present.
                    assemblyPaths = plan.Source == ExtensionSource.Local
                        ? marketplace.TryResolveInstalled(plan.Id, loadVersion, managedDir)?.AssemblyPaths
                        : (await marketplace.EnsureInstalledAsync(plan.Id, loadVersion, managedDir, ct)
                            .ConfigureAwait(false)).AssemblyPaths;

                    if (assemblyPaths is null)
                    {
                        Report(unavailable, promptForConsent, plan.Id, loadVersion,
                            "the extension is not installed on this machine");
                        continue;
                    }
                }

                await LoadAssembliesAsync(extensionHost, assemblyPaths, plan.Id);
                extensionHost.MarkExtensionPackageLoaded(plan.Id);
                extensionHost.ApprovePackage(plan.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Report(unavailable, promptForConsent, plan.Id,
                    plan.EffectiveVersion ?? plan.OriginalVersion, ex.Message);
            }
        }

        if (promptForConsent && unavailable.Count > 0)
            await extensionHost.ReportUnavailableExtensionsAsync(unavailable).ConfigureAwait(false);
    }

    /// <summary>A required-extension reference paired with the concrete version it would load.</summary>
    private readonly record struct ResolvedRef(
        string Id, string? OriginalVersion, string? EffectiveVersion, ExtensionSource Source);

    /// <summary>Records an unavailable extension, but only when failures are being surfaced
    /// (the consent pass); the pre-session pass defers reporting to that later pass.</summary>
    private static void Report(
        List<UnavailableExtensionInfo> sink, bool active, string id, string? version, string reason)
    {
        if (active)
            sink.Add(new UnavailableExtensionInfo(id, version, reason));
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
            var consent = new[] { new ExtensionConsentInfo(id, version,
                string.Format(Strings.Consent_Source_LocalFile, Path.GetFileName(localFilePath))) };
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
            registered = await LoadAssembliesAsync(extensionHost, install.AssemblyPaths, install.PackageId);
            extensionHost.MarkExtensionPackageLoaded(install.PackageId);
        }

        return new LocalInstallOutcome(true, install.PackageId, install.ResolvedVersion, null, registered);
    }
}
