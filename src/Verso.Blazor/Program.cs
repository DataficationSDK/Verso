using Verso.Blazor.Services;
using Verso.Blazor.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(1);
    });

builder.Services.AddSingleton<LayoutAssetCache>();
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

app.MapGet("/_verso/layout-assets", (HttpContext ctx, LayoutAssetCache cache) =>
{
    var ext = ctx.Request.Query["ext"].ToString();
    var layout = ctx.Request.Query["layout"].ToString();
    var asset = ctx.Request.Query["asset"].ToString();

    if (string.IsNullOrEmpty(ext) || string.IsNullOrEmpty(layout) || string.IsNullOrEmpty(asset))
        return Results.BadRequest("ext, layout, and asset query parameters are required.");

    if (!cache.TryGet(ext, layout, asset, out var contentType, out var content))
        return Results.NotFound();

    return Results.File(content, contentType);
});

app.MapRazorComponents<Verso.Blazor.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
