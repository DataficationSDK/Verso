using Verso.Extensions.CellTypes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ParametersCellTypeTests
{
    private readonly ParametersCellType _cellType = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.celltype.parameters", _cellType.ExtensionId);

    [TestMethod]
    public void Name_IsCorrect()
        => Assert.AreEqual("Parameters Cell Type", _cellType.Name);

    [TestMethod]
    public void Version_IsValidSemver()
        => Assert.AreEqual("1.0.0", _cellType.Version);

    [TestMethod]
    public void CellTypeId_IsParameters()
        => Assert.AreEqual("parameters", _cellType.CellTypeId);

    [TestMethod]
    public void DisplayName_IsParameters()
        => Assert.AreEqual("Parameters", _cellType.DisplayName);

    [TestMethod]
    public void IsEditable_IsFalse()
        => Assert.IsFalse(_cellType.IsEditable);

    [TestMethod]
    public void Kernel_IsNull()
        => Assert.IsNull(_cellType.Kernel);

    [TestMethod]
    public void Renderer_IsNotNull()
        => Assert.IsNotNull(_cellType.Renderer);

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
