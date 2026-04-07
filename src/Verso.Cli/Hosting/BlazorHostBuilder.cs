using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verso.Blazor.Services;
using Verso.Blazor.Shared.Services;

namespace Verso.Cli.Hosting;

/// <summary>
/// Configuration for the <c>verso serve</c> command.
/// </summary>
public sealed record ServeOptions
{
    public int Port { get; init; } = 5050;
    public bool NoHttps { get; init; }
    public bool Verbose { get; init; }
    public string? ExtensionsDirectory { get; init; }
}

/// <summary>
/// Builds a Kestrel-hosted Blazor Server application matching the Verso.Blazor standalone host.
/// </summary>
public static class BlazorHostBuilder
{
    public static WebApplication Build(ServeOptions options)
    {
        // The static web assets manifest is named after the ApplicationName.
        // Since the CLI entry assembly is Verso.Cli but the manifest ships as
        // Verso.Blazor.staticwebassets.runtime.json, we set ApplicationName
        // to "Verso.Blazor" so the middleware discovers the correct manifest
        // and serves wwwroot content from Verso.Blazor and Verso.Blazor.Shared.
        var blazorAssemblyDir = Path.GetDirectoryName(
            typeof(Verso.Blazor.Components.App).Assembly.Location)!;

        // In Development mode ASP.NET Core uses the manifest to locate wwwroot
        // content. The manifest contains absolute paths from the build machine,
        // so it only works from the original build output. When installed as a
        // global tool (or any scenario where the manifest paths are stale) we
        // fall back to Production mode and serve the bundled wwwroot directly.
        var useDevMode = HasValidStaticWebAssetsManifest(blazorAssemblyDir);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Verso.Blazor",
            ContentRootPath = blazorAssemblyDir,
            EnvironmentName = useDevMode ? "Development" : "Production",
        });

        // Suppress ASP.NET Core info/warn noise unless --verbose is set.
        // Errors still surface so startup failures are visible.
        builder.Logging.SetMinimumLevel(
            options.Verbose ? LogLevel.Information : LogLevel.Error);

        // Configure Kestrel URLs
        var urls = new List<string> { $"http://localhost:{options.Port}" };
        if (!options.NoHttps)
            urls.Add($"https://localhost:{options.Port + 1}");
        builder.WebHost.UseUrls(urls.ToArray());

        // Razor + Blazor Server with extended circuit retention
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(circuitOptions =>
            {
                circuitOptions.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(1);
            });

        // Notebook service (same registration as Verso.Blazor/Program.cs)
        builder.Services.AddScoped<INotebookService, ServerNotebookService>();

        var app = builder.Build();

        // Middleware pipeline (matches Verso.Blazor/Program.cs)
        //
        // Serve the bundled wwwroot that ships alongside the assembly. This
        // covers blazor.web.js (extracted at build time) and, in Production
        // mode (global tool install), all other static assets. In Development
        // mode the manifest handles most assets, but blazor.web.js is not in
        // the manifest so we still need this provider.
        var wwwrootPath = Path.Combine(blazorAssemblyDir, "wwwroot");
        if (Directory.Exists(wwwrootPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
            });
        }

        if (!options.NoHttps)
            app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<Verso.Blazor.Components.App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    /// <summary>
    /// Returns true when the static web assets manifest exists and its ContentRoots
    /// point to directories that exist on disk (i.e. running from the original build
    /// output). Returns false when installed as a global tool where the manifest
    /// contains absolute paths from the CI build machine.
    /// </summary>
    private static bool HasValidStaticWebAssetsManifest(string assemblyDir)
    {
        var manifestPath = Path.Combine(assemblyDir, "Verso.Blazor.staticwebassets.runtime.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath));
            var contentRoots = json?["ContentRoots"]?.AsArray();
            if (contentRoots is null || contentRoots.Count == 0)
                return false;

            var firstRoot = contentRoots[0]?.GetValue<string>();
            return firstRoot is not null && Directory.Exists(firstRoot);
        }
        catch
        {
            return false;
        }
    }
}
