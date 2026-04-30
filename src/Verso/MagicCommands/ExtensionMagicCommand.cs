using System.Runtime.InteropServices;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Kernels;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!extension PackageId [Version]</c> — resolves a NuGet package, requests user consent,
/// loads extensions from its assemblies via <see cref="ExtensionHost"/>, and stores assembly
/// paths in the variable store so the CSharpKernel picks them up as MetadataReferences.
/// <para>
/// <c>#!extension ./path/to/MyExtension.dll</c> — loads extensions directly from a local
/// assembly file. Paths are resolved relative to the notebook's directory. No consent dialog
/// is shown for local files.
/// </para>
/// </summary>
[VersoExtension]
public sealed class ExtensionMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.extension";
    string IExtension.Name => "Extension Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "extension";
    public string Description => "Installs a NuGet package or loads a local assembly containing Verso extensions.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("packageIdOrPath", "A NuGet package ID or path to a local .dll file.", typeof(string), IsRequired: true),
        new ParameterDefinition("version", "Optional package version (NuGet only).", typeof(string))
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
                "Usage: #!extension <PackageId> [Version]  or  #!extension <path/to/assembly.dll>",
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
                "text/plain", $"Extension assembly '{Path.GetFileName(resolvedPath)}' is already loaded."))
                .ConfigureAwait(false);
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Error: Extension assembly not found: {resolvedPath}",
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
                    new(Path.GetFileName(resolvedPath), null, "session-generated local assembly")
                };

                var approved = await extensionHost.RequestExtensionConsentAsync(
                    consentInfo, context.CancellationToken).ConfigureAwait(false);

                if (!approved)
                {
                    await context.WriteOutputAsync(new CellOutput(
                        "text/plain",
                        $"Extension '{Path.GetFileName(resolvedPath)}' was not approved. Skipping."))
                        .ConfigureAwait(false);
                    return;
                }
            }
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"Loading extension from '{Path.GetFileName(resolvedPath)}'..."))
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
                    $"Warning: '{Path.GetFileName(resolvedPath)}' loaded but contains no [VersoExtension] types. " +
                    "The assembly is still available as a reference."))
                    .ConfigureAwait(false);
            }
            else
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain",
                    $"Loaded '{Path.GetFileName(resolvedPath)}' ({extensionsRegistered} extension{(extensionsRegistered == 1 ? "" : "s")} registered)"))
                    .ConfigureAwait(false);
            }
        }
        catch (BadImageFormatException)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Error: '{Path.GetFileName(resolvedPath)}' is not a valid .NET assembly.",
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
        catch (ExtensionLoadException ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Error loading extensions from '{Path.GetFileName(resolvedPath)}': {ex.Message}",
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
                $"Error loading '{Path.GetFileName(resolvedPath)}': {ex.Message}",
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
                "text/plain", $"Extension package '{packageId}' is already loaded."))
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
                    "text/plain", $"Extension '{packageId}' was not approved. Skipping."))
                    .ConfigureAwait(false);
                return;
            }

            extensionHost.ApprovePackage(packageId);
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain",
            version is not null
                ? $"Resolving extension package '{packageId}' version '{version}'..."
                : $"Resolving extension package '{packageId}'..."))
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
                $"Installed '{result.PackageId}' {result.ResolvedVersion} ({extensionsRegistered} extension{(extensionsRegistered == 1 ? "" : "s")} registered)"))
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
                $"Failed to resolve extension package '{packageId}': {ex.Message}",
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
