using Verso.Blazor.Services;
using Verso.Blazor.Shared.Services;
using Verso.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(1);
    })
    .AddHubOptions(options =>
    {
        // Allow large client to server interop payloads. An isolated layout can post a
        // rendered image back to the host for download (e.g. export PNG); the 32 KB default
        // silently tears down the circuit when that payload is exceeded.
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024;
    });

// Seed the host options with the managed extensions directory so marketplace installs have
// a stable, persistent home and the directory scan picks up user-dropped extension assemblies.
builder.Services.AddSingleton(new NotebookServiceOptions
{
    ExtensionsDirectories = new[] { ExtensionDirectoryResolver.GetDefaultManagedDir() }
});

builder.Services.AddSingleton<LayoutAssetCache>();
builder.Services.AddSingleton<LayoutAssetProvider>();
builder.Services.AddScoped<INotebookService, ServerNotebookService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/_verso/layout-assets", async (HttpContext ctx, LayoutAssetCache cache, LayoutAssetProvider provider) =>
{
    var ext = ctx.Request.Query["ext"].ToString();
    var layout = ctx.Request.Query["layout"].ToString();
    var asset = ctx.Request.Query["asset"].ToString();

    if (string.IsNullOrEmpty(ext) || string.IsNullOrEmpty(layout) || string.IsNullOrEmpty(asset))
        return Results.BadRequest("ext, layout, and asset query parameters are required.");

    if (cache.TryGet(ext, layout, asset, out var contentType, out var content))
        return Results.File(content, contentType);

    // Cache miss: the per-circuit registration hasn't run for this process yet (fresh
    // start, server restart, or a prerender/early fetch racing the circuit). Regenerate
    // the asset on demand and cache it so the stable URL keeps resolving without a live
    // notebook circuit, matching the stateless asset delivery of the out-of-process host.
    var generated = await provider.TryGenerateAsync(ext, layout, asset);
    if (generated is null)
        return Results.NotFound();

    cache.Register(ext, layout, asset, generated.Value.ContentType, generated.Value.Content);
    return Results.File(generated.Value.Content, generated.Value.ContentType);
});

app.MapRazorComponents<Verso.Blazor.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
