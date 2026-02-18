using Verso.Abstractions;
using Verso.Ado.CellType;
using Verso.Ado.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.CellType;

[TestClass]
public sealed class SqlCellRendererTests
{
    private readonly SqlCellRenderer _renderer = new();

    [TestMethod]
    public void CellTypeId_IsSql()
        => Assert.AreEqual("sql", _renderer.CellTypeId);

    [TestMethod]
    public void DisplayName_IsSql()
        => Assert.AreEqual("SQL", _renderer.DisplayName);

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.ado.renderer.sql", _renderer.ExtensionId);

    [TestMethod]
    public void GetEditorLanguage_ReturnsSql()
        => Assert.AreEqual("sql", _renderer.GetEditorLanguage());

    [TestMethod]
    public void CollapsesInputOnExecute_ReturnsFalse()
        => Assert.IsFalse(_renderer.CollapsesInputOnExecute);

    [TestMethod]
    public async Task RenderInputAsync_WithConnection_ShowsIndicator()
    {
        var context = new StubCellRenderContext();
        context.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "northwind");

        var result = await _renderer.RenderInputAsync("SELECT * FROM Products", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("Connected: northwind"));
        Assert.IsTrue(result.Content.Contains("verso-sql-connection-indicator"));
    }

    [TestMethod]
    public async Task RenderInputAsync_NoConnection_ShowsDisconnected()
    {
        var context = new StubCellRenderContext();

        var result = await _renderer.RenderInputAsync("SELECT 1", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("No connection"));
        Assert.IsTrue(result.Content.Contains("verso-sql-disconnected"));
    }

    [TestMethod]
    public async Task RenderInputAsync_WithConnectionDirective_UsesDirectiveName()
    {
        var context = new StubCellRenderContext();
        context.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "default_db");

        var result = await _renderer.RenderInputAsync("--connection mydb\nSELECT 1", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("Connected: mydb"));
    }

    [TestMethod]
    public async Task RenderOutputAsync_PassesThrough()
    {
        var context = new StubCellRenderContext();
        var output = new CellOutput("text/html", "<table>...</table>");

        var result = await _renderer.RenderOutputAsync(output, context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.AreEqual("<table>...</table>", result.Content);
    }
}
