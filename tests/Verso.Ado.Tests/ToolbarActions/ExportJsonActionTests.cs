using System.Data;
using System.Text;
using System.Text.Json;
using Verso.Ado.ToolbarActions;
using Verso.Ado.Tests.Helpers;

namespace Verso.Ado.Tests.ToolbarActions;

[TestClass]
public sealed class ExportJsonActionTests
{
    private readonly ExportJsonAction _action = new();

    [TestMethod]
    public void ActionId_IsCorrect()
        => Assert.AreEqual("verso.ado.action.export-json", _action.ActionId);

    [TestMethod]
    public void Placement_IsCellToolbar()
        => Assert.AreEqual(Verso.Abstractions.ToolbarPlacement.CellToolbar, _action.Placement);

    [TestMethod]
    public void Order_Is81()
        => Assert.AreEqual(81, _action.Order);

    [TestMethod]
    public async Task IsEnabledAsync_WithDataTable_ReturnsTrue()
    {
        var cellId = Guid.NewGuid();
        var context = new StubToolbarActionContext
        {
            SelectedCellIds = new[] { cellId }
        };

        var dt = CreateTestDataTable();
        context.Variables.Set($"__verso_ado_cellvar_{cellId}", "testResult");
        context.Variables.Set("testResult", dt);

        Assert.IsTrue(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabledAsync_NoDataTable_ReturnsFalse()
    {
        var context = new StubToolbarActionContext
        {
            SelectedCellIds = new[] { Guid.NewGuid() }
        };

        Assert.IsFalse(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task ExecuteAsync_CallsRequestFileDownload()
    {
        var cellId = Guid.NewGuid();
        var context = new StubToolbarActionContext
        {
            SelectedCellIds = new[] { cellId }
        };

        var dt = CreateTestDataTable();
        context.Variables.Set($"__verso_ado_cellvar_{cellId}", "testResult");
        context.Variables.Set("testResult", dt);

        await _action.ExecuteAsync(context);

        Assert.AreEqual(1, context.DownloadedFiles.Count);
        Assert.AreEqual("application/json", context.DownloadedFiles[0].ContentType);
        Assert.IsTrue(context.DownloadedFiles[0].FileName.EndsWith(".json"));

        var json = Encoding.UTF8.GetString(context.DownloadedFiles[0].Data);
        Assert.IsTrue(json.Contains("Alice"));
    }

    [TestMethod]
    public void BuildJson_ColumnNamesAsKeys()
    {
        var dt = CreateTestDataTable();
        var json = ExportJsonAction.BuildJson(dt);

        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;
        Assert.AreEqual(JsonValueKind.Array, array.ValueKind);
        Assert.AreEqual(2, array.GetArrayLength());

        var first = array[0];
        Assert.IsTrue(first.TryGetProperty("Name", out _));
        Assert.IsTrue(first.TryGetProperty("Price", out _));
        Assert.AreEqual("Alice", first.GetProperty("Name").GetString());
    }

    [TestMethod]
    public void BuildJson_HandlesNulls()
    {
        var dt = new DataTable();
        dt.Columns.Add("Value", typeof(string));
        dt.Rows.Add(DBNull.Value);

        var json = ExportJsonAction.BuildJson(dt);
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement[0];
        Assert.AreEqual(JsonValueKind.Null, first.GetProperty("Value").ValueKind);
    }

    [TestMethod]
    public void BuildJson_HandlesNumericTypes()
    {
        var dt = new DataTable();
        dt.Columns.Add("IntVal", typeof(int));
        dt.Columns.Add("DoubleVal", typeof(double));
        dt.Rows.Add(42, 3.14);

        var json = ExportJsonAction.BuildJson(dt);
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement[0];
        Assert.AreEqual(42, first.GetProperty("IntVal").GetInt32());
        Assert.AreEqual(3.14, first.GetProperty("DoubleVal").GetDouble(), 0.001);
    }

    private static DataTable CreateTestDataTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("Name", typeof(string));
        dt.Columns.Add("Price", typeof(double));
        dt.Rows.Add("Alice", 9.99);
        dt.Rows.Add("Bob", 19.99);
        return dt;
    }
}
