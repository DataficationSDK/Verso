using Verso.Abstractions;

namespace Verso.JavaScript.MagicCommands;

/// <summary>
/// Magic command for installing npm packages: <c>#!npm lodash axios</c>.
/// Packages are installed to <c>~/.verso/node/node_modules</c> and made available
/// via <c>require()</c> in subsequent JavaScript cells (Node.js mode only).
/// </summary>
[VersoExtension]
public sealed class NpmMagicCommand : IMagicCommand
{
    public string ExtensionId => "verso.magic.npm";
    string IExtension.Name => "Npm Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    string? IExtension.Description => "Installs npm packages for use in JavaScript cells.";

    // IMagicCommand
    public string Name => "npm";
    public string Description => "Installs npm packages for use in subsequent JavaScript cells.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new ParameterDefinition("packages",
            "One or more package names (e.g. lodash axios@1.6).",
            typeof(string), IsRequired: true),
    ];

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        var packages = arguments.Trim();

        // Strip leading "install " if present
        if (packages.StartsWith("install ", StringComparison.OrdinalIgnoreCase))
            packages = packages[8..].Trim();

        if (string.IsNullOrWhiteSpace(packages))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Usage: #!npm <package-names>", IsError: true, ErrorName: "NpmError"));
            context.SuppressExecution = true;
            return;
        }

        // Check if the JavaScript kernel is using Node.js
        var kernels = context.ExtensionHost.GetKernels();
        var jsKernel = kernels.FirstOrDefault(k => k.LanguageId == "javascript");
        if (jsKernel is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                "No JavaScript kernel is loaded. #!npm requires the JavaScript kernel.",
                IsError: true, ErrorName: "NpmError"));
            context.SuppressExecution = true;
            return;
        }

        // Fast-path: check if all packages are already installed
        var packageNames = packages.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('@')[0])
            .ToList();

        if (packageNames.All(NpmManager.IsPackageInstalled))
        {
            context.Variables.Set(NpmManager.NodePathStoreKey, NpmManager.NodeModulesPath);
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Packages already installed: {string.Join(", ", packageNames)}"));
            return;
        }

        // Run npm install
        var success = await NpmManager.InstallAsync(packages, context, context.CancellationToken);

        if (success)
        {
            context.Variables.Set(NpmManager.NodePathStoreKey, NpmManager.NodeModulesPath);
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Installed: {packages}"));
        }
        else
        {
            context.SuppressExecution = true;
        }
    }
}
