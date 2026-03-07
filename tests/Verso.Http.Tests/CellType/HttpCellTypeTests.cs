using Verso.Http.CellType;

namespace Verso.Http.Tests.CellType;

[TestClass]
public sealed class HttpCellTypeTests
{
    [TestMethod]
    public void Properties_AreCorrect()
    {
        var cellType = new HttpCellType();

        Assert.AreEqual("verso.http.celltype.http", cellType.ExtensionId);
        Assert.AreEqual("http", cellType.CellTypeId);
        Assert.AreEqual("HTTP", cellType.DisplayName);
        Assert.IsTrue(cellType.IsEditable);
        Assert.IsNotNull(cellType.Kernel);
        Assert.IsNotNull(cellType.Renderer);
        Assert.IsNotNull(cellType.Icon);
    }

    [TestMethod]
    public void GetDefaultContent_ContainsGetRequest()
    {
        var cellType = new HttpCellType();
        var content = cellType.GetDefaultContent();

        Assert.IsTrue(content.StartsWith("GET "));
        Assert.IsTrue(content.Contains("Accept: application/json"));
    }

    [TestMethod]
    public void Renderer_Properties_AreCorrect()
    {
        var renderer = new HttpCellRenderer();

        Assert.AreEqual("verso.http.renderer.http", renderer.ExtensionId);
        Assert.AreEqual("http", renderer.CellTypeId);
        Assert.AreEqual("plaintext", renderer.GetEditorLanguage());
        Assert.IsFalse(renderer.CollapsesInputOnExecute);
    }
}
