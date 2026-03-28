using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Verso.Blazor",
            ContentRootPath = blazorAssemblyDir,
            // Development mode enables static web assets discovery from the manifest
            // and uses the ASP.NET Core dev certificate for HTTPS.
            EnvironmentName = "Development",
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
        if (!options.NoHttps)
            app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<Verso.Blazor.Components.App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
