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
            .Select(NpmManager.PackageName)
            .Where(name => name.Length > 0)
            .ToList();

        if (packageNames.Count > 0 && packageNames.All(NpmManager.IsPackageInstalled))
        {
            context.Variables.Set(NpmManager.NodePathStoreKey, NpmManager.NodeModulesPath);
            await context.WriteOutputAsync(new CellOutput("text/plain", AlreadyInstalled(packageNames)));
            return;
        }

        // Said before the install starts rather than after it finishes. Resolving a package with
        // a large dependency tree takes long enough that a cell showing nothing looks stuck.
        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"Installing {string.Join(", ", packageNames)}..."));

        var detail = (jsKernel as Kernel.JavaScriptKernel)?.ShowInstallOutput ?? false;

        var success = await NpmManager.InstallAsync(
            packages, packageNames, context, detail, context.CancellationToken);

        if (success)
            context.Variables.Set(NpmManager.NodePathStoreKey, NpmManager.NodeModulesPath);
        else
            context.SuppressExecution = true;
    }

    /// <summary>
    /// What to say when nothing needed installing. The version is read from the package itself,
    /// so the cell says which one is in use rather than only that something is.
    /// </summary>
    private static string AlreadyInstalled(IReadOnlyList<string> packageNames)
    {
        var described = packageNames
            .Select(name => NpmManager.GetInstalledPackageVersion(name) is { } version
                ? $"{name} {version}"
                : name)
            .ToList();

        return described.Count == 1
            ? $"{described[0]} is already installed."
            : $"{string.Join(", ", described)} are already installed.";
    }
}
