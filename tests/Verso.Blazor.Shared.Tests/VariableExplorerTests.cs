namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class VariableExplorerTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };
    }

    [TestMethod]
    public void EmptyState_ShowsNoVariablesMessage()
    {
        _service.Variables = new List<VariableEntryDto>();

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("No variables defined"));
    }

    [TestMethod]
    public void WithVariables_RendersTable()
    {
        _service.Variables = new List<VariableEntryDto>
        {
            new("x", "Int32", "42", false),
            new("name", "String", "\"hello\"", false)
        };

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("x"));
        Assert.IsTrue(cut.Markup.Contains("Int32"));
        Assert.IsTrue(cut.Markup.Contains("42"));
        Assert.IsTrue(cut.Markup.Contains("name"));
        Assert.IsTrue(cut.Markup.Contains("String"));
    }

    [TestMethod]
    public void Table_ShowsNameTypeValueColumns()
    {
        _service.Variables = new List<VariableEntryDto>
        {
            new("count", "Int32", "10", false)
        };

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        var markup = cut.Markup;
        Assert.IsTrue(markup.Contains("Name") || markup.Contains("name"));
        Assert.IsTrue(markup.Contains("Type") || markup.Contains("type"));
        Assert.IsTrue(markup.Contains("Value") || markup.Contains("value") || markup.Contains("Preview") || markup.Contains("preview"));
    }

    [TestMethod]
    public void ClickingRow_ShowsInspectPanel()
    {
        _service.Variables = new List<VariableEntryDto>
        {
            new("data", "DataTable", "[10 rows]", true)
        };
        _service.InspectResult = new VariableInspectResultDto(
            "data", "DataTable", "text/html", "<table>...</table>");

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        // Click the data row (which has @onclick)
        var row = cut.Find("tr.verso-variable-row");
        row.Click();

        // The inspect panel should appear
        Assert.IsTrue(cut.Markup.Contains("verso-variable-inspect-panel"));
    }

    [TestMethod]
    public void MultipleVariables_AllRendered()
    {
        _service.Variables = new List<VariableEntryDto>
        {
            new("a", "Int32", "1", false),
            new("b", "String", "\"two\"", false),
            new("c", "Double", "3.14", false)
        };

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("a"));
        Assert.IsTrue(cut.Markup.Contains("b"));
        Assert.IsTrue(cut.Markup.Contains("c"));
        Assert.IsTrue(cut.Markup.Contains("3.14"));
    }

    [TestMethod]
    public void NotLoaded_ShowsNoVariables()
    {
        _service.IsLoaded = false;
        _service.Variables = new List<VariableEntryDto>();

        var cut = RenderComponent<VariableExplorer>(p => p
            .Add(v => v.Service, _service));

        // When not loaded, should show empty or message state
        Assert.IsFalse(cut.Markup.Contains("<td>"));
    }
}
