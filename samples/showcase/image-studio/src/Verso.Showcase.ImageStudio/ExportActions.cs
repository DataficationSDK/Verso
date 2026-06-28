using Verso.Abstractions;

namespace Verso.Showcase.ImageStudio;

/// <summary>
/// Contributes the composite image exports ("PNG Image" and "SVG Image") to the host's Export
/// menu. The compositor lives inside the isolated frame, so these actions do not produce bytes
/// themselves: each asks the live <see cref="ImageStudioLayout"/> frame to render the requested
/// format, and the frame streams the result back as an "export" interaction that the layout
/// turns into a host file download.
/// </summary>
/// <remarks>
/// The actions enable only while the Image Studio layout is the active layout, so they appear in
/// the Export menu exactly in that layout and stay absent in every other. Enablement keys off the
/// active layout (which flips synchronously on a layout switch) rather than a live-frame flag,
/// because the frame mounts asynchronously and the toolbar would not re-check a frame flag after
/// the mount completed; the layout is its frame, so "active layout" is the reliable signal. They
/// reach the layout through the extension host: both the layout and these actions are singletons
/// in the same host, so the action resolves the layout instance and drives it directly.
/// </remarks>
internal static class ImageStudioExport
{
    public const string LayoutId = "image-studio";

    /// <summary>Resolves the live layout singleton, or null when it is not loaded.</summary>
    public static ImageStudioLayout? ResolveLayout(IToolbarActionContext context)
        => context.ExtensionHost.GetLayouts().OfType<ImageStudioLayout>().FirstOrDefault();

    /// <summary>An export is offered only while Image Studio is the active layout.</summary>
    public static bool CanExport(IToolbarActionContext context)
        => string.Equals(context.ActiveLayoutId, LayoutId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Exports the composited canvas as a raster PNG.</summary>
[VersoExtension]
public sealed class ImageStudioExportPngAction : IToolbarAction
{
    public string ExtensionId => "com.verso.showcase.image-studio.export-png";
    public string Name => "Image Studio: Export PNG";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Exports the Image Studio composite as a PNG image.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string ActionId => "com.verso.showcase.image-studio.export-png";
    public string DisplayName => "PNG Image";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.ExportMenu;
    public int Order => 10;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
        => Task.FromResult(ImageStudioExport.CanExport(context));

    public Task ExecuteAsync(IToolbarActionContext context)
    {
        ImageStudioExport.ResolveLayout(context)?.RequestFrameExport("png", context.CancellationToken);
        return Task.CompletedTask;
    }
}

/// <summary>Exports the composite as resolution-independent SVG (the layers are vector primitives).</summary>
[VersoExtension]
public sealed class ImageStudioExportSvgAction : IToolbarAction
{
    public string ExtensionId => "com.verso.showcase.image-studio.export-svg";
    public string Name => "Image Studio: Export SVG";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "Exports the Image Studio composite as an SVG image.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string ActionId => "com.verso.showcase.image-studio.export-svg";
    public string DisplayName => "SVG Image";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.ExportMenu;
    public int Order => 20;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
        => Task.FromResult(ImageStudioExport.CanExport(context));

    public Task ExecuteAsync(IToolbarActionContext context)
    {
        ImageStudioExport.ResolveLayout(context)?.RequestFrameExport("svg", context.CancellationToken);
        return Task.CompletedTask;
    }
}
