using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// <see cref="ICellType"/> test double pairing a renderer with an optional kernel, used to verify
/// how a cell type contributes its language when new cells are created.
/// </summary>
public sealed class FakeCellType : ICellType
{
    public FakeCellType(
        string cellTypeId = "fake",
        string? displayName = null,
        ILanguageKernel? kernel = null,
        ICellRenderer? renderer = null,
        bool isEditable = true,
        string defaultContent = "")
    {
        CellTypeId = cellTypeId;
        DisplayName = displayName ?? cellTypeId;
        Kernel = kernel;
        Renderer = renderer ?? new FakeCellRenderer(
            extensionId: $"com.test.{cellTypeId}.renderer",
            cellTypeId: cellTypeId);
        IsEditable = isEditable;
        _defaultContent = defaultContent;
    }

    private readonly string _defaultContent;

    public string ExtensionId => $"com.test.{CellTypeId}.celltype";
    public string Name => DisplayName;
    public string Version => "1.0.0";
    public string? Author => "Test";
    public string? Description => "Fake cell type for testing";

    public string CellTypeId { get; }
    public string DisplayName { get; }
    public string? Icon => null;
    public ICellRenderer Renderer { get; }
    public ILanguageKernel? Kernel { get; }
    public bool IsEditable { get; }

    public string GetDefaultContent() => _defaultContent;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;

    public Task OnUnloadedAsync() => Task.CompletedTask;
}
