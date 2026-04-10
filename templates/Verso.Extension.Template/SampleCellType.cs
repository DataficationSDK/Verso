namespace MyExtension;

/// <summary>
/// Example <see cref="ICellType"/> scaffold.
/// A cell type binds a renderer (and optionally a kernel) to a named cell kind.
/// Replace this with your own cell type definition.
/// </summary>
[VersoExtension]
public sealed class SampleCellType : ICellType
{
    public string ExtensionId => "com.example.myextension.celltype";
    public string Name => "Sample Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample cell type that pairs a renderer with a kernel.";

    public string CellTypeId => "sample";
    public string DisplayName => "Sample";
    public string? Icon => "file-code";
    public ICellRenderer Renderer { get; private set; } = null!;
    public ILanguageKernel? Kernel { get; private set; }
    public bool IsEditable => true;

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        // Resolve renderer and kernel from the host's loaded extensions.
        Renderer = context.GetRenderers().First(r => r.CellTypeId == CellTypeId);
        Kernel = context.GetKernels().FirstOrDefault(k => k.LanguageId == "sample");
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string GetDefaultContent() => "// Enter sample code here";
}
