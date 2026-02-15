using System.Data;
using System.Text;
using Verso.Ado.ToolbarActions;
using Verso.Ado.Tests.Helpers;

namespace Verso.Ado.Tests.ToolbarActions;

[TestClass]
public sealed class ExportCsvActionTests
{
    private readonly ExportCsvAction _action = new();

    [TestMethod]
    public void ActionId_IsCorrect()
        => Assert.AreEqual("verso.ado.action.export-csv", _action.ActionId);

    [TestMethod]
    public void Placement_IsCellToolbar()
        => Assert.AreEqual(Verso.Abstractions.ToolbarPlacement.CellToolbar, _action.Placement);

    [TestMethod]
    public void Order_Is80()
        => Assert.AreEqual(80, _action.Order);

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
        Assert.AreEqual("text/csv", context.DownloadedFiles[0].ContentType);
        Assert.IsTrue(context.DownloadedFiles[0].FileName.EndsWith(".csv"));

        var csv = Encoding.UTF8.GetString(context.DownloadedFiles[0].Data);
        Assert.IsTrue(csv.Contains("Name"));
        Assert.IsTrue(csv.Contains("Alice"));
    }

    [TestMethod]
    public void BuildCsv_CorrectHeaderAndRows()
    {
        var dt = CreateTestDataTable();
        var csv = ExportCsvAction.BuildCsv(dt);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("Name,Price", lines[0]);
        Assert.AreEqual("Alice,9.99", lines[1]);
        Assert.AreEqual("Bob,19.99", lines[2]);
    }

    [TestMethod]
    public void CsvEscape_CommasAndQuotes()
    {
        Assert.AreEqual("\"hello, world\"", ExportCsvAction.CsvEscape("hello, world"));
        Assert.AreEqual("\"say \"\"hi\"\"\"", ExportCsvAction.CsvEscape("say \"hi\""));
        Assert.AreEqual("simple", ExportCsvAction.CsvEscape("simple"));
        Assert.AreEqual("\"line1\nline2\"", ExportCsvAction.CsvEscape("line1\nline2"));
    }

    [TestMethod]
    public void BuildCsv_HandlesNullValues()
    {
        var dt = new DataTable();
        dt.Columns.Add("Value", typeof(string));
        dt.Rows.Add(DBNull.Value);
        dt.Rows.Add("data");

        var csv = ExportCsvAction.BuildCsv(dt);
        var lines = csv.Split(Environment.NewLine);
        Assert.AreEqual("Value", lines[0]);
        Assert.AreEqual("", lines[1]); // null becomes empty field
        Assert.AreEqual("data", lines[2]);
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
