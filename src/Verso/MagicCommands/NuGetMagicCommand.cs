using Verso.Abstractions;
using Verso.Kernels;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!nuget Package [Version]</c> — resolves a NuGet package and stores assembly paths
/// in the variable store for the kernel to pick up.
/// </summary>
[VersoExtension]
public sealed class NuGetMagicCommand : IMagicCommand
{
    internal const string AssemblyStoreKey = "__verso_nuget_assemblies";
    internal const string ResolvedPackagesStoreKey = "__verso_nuget_resolved_packages";

    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.nuget";
    string IExtension.Name => "NuGet Magic Command";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "nuget";
    public string Description => "Downloads and references a NuGet package for use in subsequent code.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("packageId", "The NuGet package ID.", typeof(string), IsRequired: true),
        new ParameterDefinition("version", "Optional package version.", typeof(string))
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = false;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Usage: #!nuget <PackageId> [Version]", IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
            return;
        }

        var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var packageId = parts[0];
        var version = parts.Length > 1 ? parts[1].Trim() : null;

        await context.WriteOutputAsync(new CellOutput(
            "text/plain",
            version is not null
                ? $"Resolving NuGet package '{packageId}' version '{version}'..."
                : $"Resolving NuGet package '{packageId}'..."))
            .ConfigureAwait(false);

        try
        {
            var resolver = new NuGetPackageResolver();
            var result = await resolver.ResolvePackageAsync(packageId, version, context.CancellationToken)
                .ConfigureAwait(false);

            // Store assembly paths in variable store for the kernel to pick up
            var existingPaths = new List<string>();
            if (context.Variables.TryGet<List<string>>(AssemblyStoreKey, out var existing) && existing is not null)
                existingPaths.AddRange(existing);

            existingPaths.AddRange(result.AssemblyPaths);
            context.Variables.Set(AssemblyStoreKey, existingPaths);

            // Store resolved package info for "Installed Packages" display
            var existingResults = new List<NuGetResolveResult>();
            if (context.Variables.TryGet<List<NuGetResolveResult>>(ResolvedPackagesStoreKey, out var existingRes) && existingRes is not null)
                existingResults.AddRange(existingRes);

            existingResults.Add(result);
            context.Variables.Set(ResolvedPackagesStoreKey, existingResults);

            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Installed '{result.PackageId}', {result.ResolvedVersion}"))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                $"Failed to resolve NuGet package '{packageId}': {ex.Message}",
                IsError: true))
                .ConfigureAwait(false);
            context.SuppressExecution = true;
        }
    }
}
