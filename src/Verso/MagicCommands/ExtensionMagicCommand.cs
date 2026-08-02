using System.Runtime.InteropServices;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Kernels;
using Verso.Localization;
using Verso.Resources;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!extension PackageId [Version]</c> resolves a NuGet package, requests user consent,
/// loads extensions from its assemblies via <see cref="ExtensionHost"/>, and stores assembly
/// paths in the variable store so the CSharpKernel picks them up as MetadataReferences.
/// <para>
/// <c>#!extension ./path/to/MyExtension.dll</c> loads extensions directly from a local
/// assembly file. Paths are resolved relative to the notebook's directory. No consent dialog
/// is shown for local files.
/// </para>
/// </summary>
[VersoExtension]
public sealed class ExtensionMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.extension";
    string IExtension.Name => Strings.Magic_Extension;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "extension";
    public string Description => Strings.Magic_Extension_Description;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("packageIdOrPath", Strings.Magic_Extension_Param_PackageIdOrPath, typeof(string), IsRequired: true),
        new ParameterDefinition("version", Strings.Magic_Extension_Param_Version, typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = false;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                Strings.Magic_Extension_Usage,
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        var input = arguments.Trim();

        if (IsFilePath(input))
            await ExecuteLocalAsync(input, context).ConfigureAwait(false);
        else
            await ExecuteNuGetAsync(input, context).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    //  Local DLL path flow
    // -----------------------------------------------------------------------

    private async Task ExecuteLocalAsync(string input, IMagicCommandContext context)
    {
        var extensionHost = context.ExtensionHost as ExtensionHost;

        // Normalize backslashes to forward slashes on non-Windows so a notebook
        // authored on Windows still works on macOS/Linux.
        var normalized = NormalizePath(input);
        var resolvedPath = ImportMagicCommand.ResolvePath(normalized, context.NotebookMetadata.FilePath);

        // Idempotent: already loaded this exact path
        if (extensionHost?.IsExtensionPackageLoaded(resolvedPath) == true)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", string.Format(Strings.Magic_Extension_AlreadyLoadedAssembly, Path.GetFileName(resolvedPath))))
                .ConfigureAwait(false);
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                CellText.Error(string.Format(Strings.Magic_Extension_AssemblyNotFound, resolvedPath)),
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        // If the assembly was created after the session started, it was likely generated
        // by a prior cell in this notebook. Require user consent before loading it.
        var sessionStart = context.NotebookMetadata.SessionStartedUtc;
        if (sessionStart > DateTime.MinValue)
        {
            var fileInfo = new FileInfo(resolvedPath);
            var fileTime = fileInfo.CreationTimeUtc > fileInfo.LastWriteTimeUtc
                ? fileInfo.LastWriteTimeUtc
                : fileInfo.CreationTimeUtc;

            if (fileTime > sessionStart && extensionHost is not null)
            {
                var consentInfo = new List<ExtensionConsentInfo>
                {
                    new(Path.GetFileName(resolvedPath), null, Strings.Magic_Extension_ConsentReason_SessionGenerated)
                };

                var approved = await extensionHost.RequestExtensionConsentAsync(
                    consentInfo, context.CancellationToken).ConfigureAwait(false);

                if (!approved)
                {
                    await context.WriteOutputAsync(new CellOutput(
                        "text/plain",
                        string.Format(Strings.Magic_Extension_NotApproved, Path.GetFileName(resolvedPath))))
                        .ConfigureAwait(false);
                    return;
                }
            }
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", string.Format(Strings.Magic_Extension_Loading, Path.GetFileName(resolvedPath))))
            .ConfigureAwait(false);

        try
        {
            // Store assembly path so CSharpKernel picks it up as a MetadataReference
            var existingPaths = new List<string>();
            if (context.Variables.TryGet<List<string>>(NuGetMagicCommand.AssemblyStoreKey, out var existing) && existing is not null)
                existingPaths.AddRange(existing);
            existingPaths.Add(resolvedPath);
            context.Variables.Set(NuGetMagicCommand.AssemblyStoreKey, existingPaths);

            // Load extensions
            var extensionsRegistered = 0;
            if (extensionHost is not null)
            {
                var beforeCount = extensionHost.GetLoadedExtensions().Count;
                await extensionHost.LoadFromAssemblyAsync(resolvedPath).ConfigureAwait(false);
                extensionsRegistered = extensionHost.GetLoadedExtensions().Count - beforeCount;
                extensionHost.MarkExtensionPackageLoaded(resolvedPath);
            }

            if (extensionsRegistered == 0)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain",
                    CellText.Warning(string.Format(
                        Strings.Magic_Extension_NoTypes, Path.GetFileName(resolvedPath)))))
                    .ConfigureAwait(false);
            }
            else
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain",
                    string.Format(
                        Plural.Of(extensionsRegistered, Strings.Magic_Extension_Loaded_One, Strings.Magic_Extension_Loaded_Other),
                        Path.GetFileName(resolvedPath), extensionsRegistered)))
                    .ConfigureAwait(false);
            }
        }
        catch (BadImageFormatException)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                CellText.Error(string.Format(Strings.Magic_Extension_NotAnAssembly, Path.GetFileName(resolvedPath))),
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
        catch (ExtensionLoadException ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                string.Format(Strings.Magic_Extension_LoadFailed, Path.GetFileName(resolvedPath), ex.Message),
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                string.Format(Strings.Magic_Extension_LoadFailedGeneric, Path.GetFileName(resolvedPath), ex.Message),
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
    }

    // -----------------------------------------------------------------------
    //  NuGet package flow (original behavior)
    // -----------------------------------------------------------------------

    private async Task ExecuteNuGetAsync(string input, IMagicCommandContext context)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var packageId = parts[0];
        var version = parts.Length > 1 ? parts[1].Trim() : null;

        // Get the ExtensionHost (cast from IExtensionHostContext)
        var extensionHost = context.ExtensionHost as ExtensionHost;

        // Idempotent: already loaded → early return
        if (extensionHost?.IsExtensionPackageLoaded(packageId) == true)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", string.Format(Strings.Magic_Extension_AlreadyLoadedPackage, packageId)))
                .ConfigureAwait(false);
            return;
        }

        // Consent check
        if (extensionHost is not null && !extensionHost.IsPackageApproved(packageId))
        {
            var consentInfo = new List<ExtensionConsentInfo>
            {
                new(packageId, version)
            };

            var approved = await extensionHost.RequestExtensionConsentAsync(
                consentInfo, context.CancellationToken).ConfigureAwait(false);

            if (!approved)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", string.Format(Strings.Magic_Extension_NotApproved, packageId)))
                    .ConfigureAwait(false);
                return;
            }

            extensionHost.ApprovePackage(packageId);
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain",
            version is not null
                ? string.Format(Strings.Magic_Extension_ResolvingVersion, packageId, version)
                : string.Format(Strings.Magic_Extension_Resolving, packageId)))
            .ConfigureAwait(false);

        try
        {
            // Resolve NuGet package (including any #i sources)
            var resolver = new NuGetPackageResolver();

            if (context.Variables.TryGet<NuGetSourceRegistry>(NuGetSourceRegistry.StoreKey, out var sourceRegistry)
                && sourceRegistry is not null)
            {
                foreach (var source in sourceRegistry.Sources)
                    resolver.AddSource(source);
            }
            var result = await resolver.ResolvePackageAsync(packageId, version, context.CancellationToken)
                .ConfigureAwait(false);

            // Store assembly paths in variable store (same keys as #!nuget)
            var existingPaths = new List<string>();
            if (context.Variables.TryGet<List<string>>(NuGetMagicCommand.AssemblyStoreKey, out var existing) && existing is not null)
                existingPaths.AddRange(existing);
            existingPaths.AddRange(result.AssemblyPaths);
            context.Variables.Set(NuGetMagicCommand.AssemblyStoreKey, existingPaths);

            // Load extensions from each assembly
            var extensionsRegistered = 0;
            if (extensionHost is not null)
            {
                foreach (var assemblyPath in result.AssemblyPaths)
                {
                    try
                    {
                        var beforeCount = extensionHost.GetLoadedExtensions().Count;
                        await extensionHost.LoadFromAssemblyAsync(assemblyPath).ConfigureAwait(false);
                        extensionsRegistered += extensionHost.GetLoadedExtensions().Count - beforeCount;
                    }
                    catch (ExtensionLoadException)
                    {
                        // Non-extension assemblies or validation failures — skip silently
                    }
                    catch (BadImageFormatException)
                    {
                        // Native DLLs or non-.NET assemblies — skip silently
                    }
                }

                extensionHost.MarkExtensionPackageLoaded(packageId);
            }

            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                string.Format(
                    Plural.Of(extensionsRegistered, Strings.Magic_Extension_Installed_One, Strings.Magic_Extension_Installed_Other),
                    result.PackageId, result.ResolvedVersion, extensionsRegistered)))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                string.Format(Strings.Magic_Extension_ResolveFailed, packageId, ex.Message),
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Determines whether <paramref name="input"/> looks like a file path rather than a
    /// NuGet package ID. Package IDs are dotted identifiers (e.g. <c>My.Package</c>) and
    /// never contain path separators or end with <c>.dll</c>.
    /// </summary>
    internal static bool IsFilePath(string input)
    {
        return input.Contains('/')
            || input.Contains('\\')
            || input.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes backslash path separators to forward slashes on non-Windows platforms,
    /// so a notebook authored on Windows still resolves correctly on macOS/Linux where
    /// <c>\</c> is a literal filename character rather than a directory separator.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return path.Replace('\\', '/');
        return path;
    }

    /// <summary>
    /// Scans all cells in a notebook for <c>#!extension</c> directives that reference
    /// NuGet packages (not local file paths) and returns a deduplicated list of
    /// <see cref="ExtensionConsentInfo"/> for consent prompting.
    /// </summary>
    public static IReadOnlyList<ExtensionConsentInfo> ScanForExtensionDirectives(NotebookModel notebook)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ExtensionConsentInfo>();

        foreach (var cell in notebook.Cells)
        {
            if (!string.Equals(cell.Type, "code", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(cell.Source))
                continue;

            var parsed = MagicCommandParser.Parse(cell.Source);
            if (!parsed.IsMagicCommand)
                continue;
            if (!string.Equals(parsed.CommandName, "extension", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(parsed.Arguments))
                continue;

            var parts = parsed.Arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var pkgId = parts[0];
            var ver = parts.Length > 1 ? parts[1].Trim() : null;

            // Local file paths don't need consent — skip them
            if (IsFilePath(pkgId))
                continue;

            if (seen.Add(pkgId))
                results.Add(new ExtensionConsentInfo(pkgId, ver));
        }

        return results;
    }
}
