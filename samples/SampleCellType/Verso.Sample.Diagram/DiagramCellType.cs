using Verso.Abstractions;

namespace Verso.Sample.Diagram;

/// <summary>
/// Diagram cell type combining the diagram renderer with the diagram kernel.
/// Parses arrow notation and renders SVG flowcharts.
/// </summary>
[VersoExtension]
public sealed class DiagramCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "com.verso.sample.diagram.celltype";
    public string Name => "Diagram Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Diagram cell type for creating flowcharts with arrow notation.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "diagram";
    public string DisplayName => "Diagram";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<rect x=\"2\" y=\"2\" width=\"8\" height=\"5\" rx=\"1\" />"
        + "<rect x=\"14\" y=\"2\" width=\"8\" height=\"5\" rx=\"1\" />"
        + "<rect x=\"8\" y=\"17\" width=\"8\" height=\"5\" rx=\"1\" />"
        + "<line x1=\"6\" y1=\"7\" x2=\"6\" y2=\"12\" stroke=\"currentColor\" stroke-width=\"1.5\" />"
        + "<line x1=\"6\" y1=\"12\" x2=\"12\" y2=\"17\" stroke=\"currentColor\" stroke-width=\"1.5\" />"
        + "<line x1=\"18\" y1=\"7\" x2=\"18\" y2=\"12\" stroke=\"currentColor\" stroke-width=\"1.5\" />"
        + "<line x1=\"18\" y1=\"12\" x2=\"12\" y2=\"17\" stroke=\"currentColor\" stroke-width=\"1.5\" />"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new DiagramRenderer();
    public ILanguageKernel? Kernel { get; } = new DiagramKernel();
    public bool IsEditable => true;

    public string GetDefaultContent() => "// Define your flowchart\nStart --> Process\nProcess --> End";
}
