using Verso.Extensions.CellTypes;
using Verso.Extensions.Renderers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class MarkdownCellTypeTests
{
    private readonly MarkdownCellType _cellType = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.celltype.markdown", _cellType.ExtensionId);

    [TestMethod]
    public void Name_IsCorrect()
        => Assert.AreEqual("Markdown Cell Type", _cellType.Name);

    [TestMethod]
    public void Version_IsValidSemver()
        => Assert.AreEqual("1.0.0", _cellType.Version);

    [TestMethod]
    public void CellTypeId_IsMarkdown()
        => Assert.AreEqual("markdown", _cellType.CellTypeId);

    [TestMethod]
    public void DisplayName_IsMarkdown()
        => Assert.AreEqual("Markdown", _cellType.DisplayName);

    [TestMethod]
    public void IsEditable_IsTrue()
        => Assert.IsTrue(_cellType.IsEditable);

    [TestMethod]
    public void Kernel_IsNull()
        => Assert.IsNull(_cellType.Kernel);

    [TestMethod]
    public void Renderer_IsMarkdownRenderer()
        => Assert.IsInstanceOfType<MarkdownRenderer>(_cellType.Renderer);

    [TestMethod]
    public void PersistsOutputs_IsFalse()
        => Assert.IsFalse(_cellType.PersistsOutputs);

    [TestMethod]
    public void GetDefaultContent_ReturnsEmptyString()
        => Assert.AreEqual(string.Empty, _cellType.GetDefaultContent());

    [TestMethod]
    public void Icon_IsNotNull()
        => Assert.IsNotNull(_cellType.Icon);

    [TestMethod]
    public async Task OnLoadedAsync_DoesNotThrow()
        => await _cellType.OnLoadedAsync(null!);

    [TestMethod]
    public async Task OnUnloadedAsync_DoesNotThrow()
        => await _cellType.OnUnloadedAsync();
}
