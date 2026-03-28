using System.Text.Json;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Extensions.Renderers;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ParametersCellRendererTests
{
    private readonly ParametersCellRenderer _renderer = new();

    // --- Extension metadata ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.renderer.parameters", _renderer.ExtensionId);

    [TestMethod]
    public void CellTypeId_IsParameters()
        => Assert.AreEqual("parameters", _renderer.CellTypeId);

    [TestMethod]
    public void CollapsesInputOnExecute_IsFalse()
        => Assert.IsFalse(_renderer.CollapsesInputOnExecute);

    [TestMethod]
    public void GetEditorLanguage_ReturnsNull()
        => Assert.IsNull(_renderer.GetEditorLanguage());

    // --- Rendering ---

    [TestMethod]
    public async Task RenderInput_NoParameters_ShowsEmptyState()
    {
        var context = CreateContext(parameters: null);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("No parameters defined"));
        Assert.IsTrue(result.Content.Contains("parameter-add"));
    }

    [TestMethod]
    public async Task RenderInput_WithParameters_ShowsFormTable()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Description = "AWS region", Required = true },
            ["batchSize"] = new() { Type = "int", Description = "Batch size", Default = 1000L }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("region"), "Should contain parameter name");
        Assert.IsTrue(result.Content.Contains("batchSize"), "Should contain second parameter name");
        Assert.IsTrue(result.Content.Contains("AWS region"), "Should contain description");
        Assert.IsTrue(result.Content.Contains("verso-parameter-type-badge"), "Should contain type badge");
        Assert.IsTrue(result.Content.Contains("verso-parameter-required"), "Should contain required indicator");
    }

    [TestMethod]
    public async Task RenderInput_StringParam_RendersTextInput()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["name"] = new() { Type = "string", Default = "hello" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"text\""), "Should render text input for string type");
        Assert.IsTrue(result.Content.Contains("value=\"hello\""), "Should pre-fill default value");
    }

    [TestMethod]
    public async Task RenderInput_IntParam_RendersNumberInput()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["count"] = new() { Type = "int", Default = 42L }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"number\""), "Should render number input for int type");
    }

    [TestMethod]
    public async Task RenderInput_BoolParam_RendersCheckbox()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["dryRun"] = new() { Type = "bool", Default = true }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"checkbox\""), "Should render checkbox for bool type");
        Assert.IsTrue(result.Content.Contains("checked"), "Should check box when default is true");
    }

    [TestMethod]
    public async Task RenderInput_DateParam_RendersDateInput()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["startDate"] = new() { Type = "date" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"date\""), "Should render date input");
    }

    [TestMethod]
    public async Task RenderInput_DatetimeParam_RendersDatetimeLocalInput()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["timestamp"] = new() { Type = "datetime" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"datetime-local\""), "Should render datetime-local input");
    }

    [TestMethod]
    public async Task RenderInput_FloatParam_RendersNumberWithStep()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["ratio"] = new() { Type = "float", Default = 0.5 }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("type=\"number\""), "Should render number input");
        Assert.IsTrue(result.Content.Contains("step=\"any\""), "Should include step=any for float");
    }

    [TestMethod]
    public async Task RenderInput_OrderedParameters_RenderInOrder()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["zeta"] = new() { Type = "string", Order = 2 },
            ["alpha"] = new() { Type = "string", Order = 1 },
            ["beta"] = new() { Type = "string", Order = 1 }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        var alphaIdx = result.Content.IndexOf("alpha");
        var betaIdx = result.Content.IndexOf("beta");
        var zetaIdx = result.Content.IndexOf("zeta");

        Assert.IsTrue(alphaIdx < betaIdx, "alpha (Order=1) should come before beta (Order=1, alphabetical)");
        Assert.IsTrue(betaIdx < zetaIdx, "beta should come before zeta (Order=2)");
    }

    // --- HTML encoding ---

    [TestMethod]
    public async Task RenderInput_ScriptInValue_IsHtmlEncoded()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["xss"] = new() { Type = "string", Default = "<script>alert('xss')</script>" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsFalse(result.Content.Contains("<script>"), "Script tags should be HTML-encoded");
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"), "Should contain encoded script tag");
    }

    [TestMethod]
    public async Task RenderInput_ScriptInName_IsHtmlEncoded()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["<b>bold</b>"] = new() { Type = "string" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsFalse(result.Content.Contains("<b>bold</b>"), "HTML in name should be encoded");
    }

    // --- Interaction handler: parameter-update ---

    [TestMethod]
    public async Task Interaction_ParameterUpdate_ValidValue_UpdatesModelAndVariables()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["count"] = new() { Type = "int", Default = 10L }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);

        var payload = JsonSerializer.Serialize(new { name = "count", value = "42" });
        var ctx = CreateInteractionContext("parameter-update", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.AreEqual(42L, parameters["count"].Default);
        Assert.AreEqual(42L, variables.Get<long>("count"));
    }

    [TestMethod]
    public async Task Interaction_ParameterUpdate_InvalidValue_ReturnsError()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["count"] = new() { Type = "int", Default = 10L }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);

        var payload = JsonSerializer.Serialize(new { name = "count", value = "not-a-number" });
        var ctx = CreateInteractionContext("parameter-update", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("verso-parameters-error"), "Should contain error element");
        Assert.AreEqual(10L, parameters["count"].Default, "Original default should be unchanged");
    }

    [TestMethod]
    public async Task Interaction_ParameterUpdate_UnknownParam_ReturnsError()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["x"] = new() { Type = "string" }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);

        var payload = JsonSerializer.Serialize(new { name = "nonexistent", value = "val" });
        var ctx = CreateInteractionContext("parameter-update", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("not found"), "Should report parameter not found");
    }

    // --- Interaction handler: parameter-add ---

    [TestMethod]
    public async Task Interaction_ParameterAdd_CreatesDefinition()
    {
        var notebook = new NotebookModel();
        var variables = new Verso.Contexts.VariableStore();
        var stubOps = new StubNotebookOperations();

        var payload = JsonSerializer.Serialize(new { name = "region", type = "string", description = "AWS region", required = false });
        var ctx = CreateInteractionContext("parameter-add", payload, notebook, variables, stubOps);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.IsNotNull(notebook.Parameters);
        Assert.IsTrue(notebook.Parameters.ContainsKey("region"));
        Assert.AreEqual("string", notebook.Parameters["region"].Type);
        Assert.AreEqual("AWS region", notebook.Parameters["region"].Description);
    }

    [TestMethod]
    public async Task Interaction_ParameterAdd_AutoInsertsCellWhenNoneExists()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp" });
        var variables = new Verso.Contexts.VariableStore();
        var stubOps = new StubNotebookOperations();

        var payload = JsonSerializer.Serialize(new { name = "x", type = "string" });
        var ctx = CreateInteractionContext("parameter-add", payload, notebook, variables, stubOps);

        await _renderer.OnCellInteractionAsync(ctx);

        Assert.AreEqual(1, stubOps.InsertedCells.Count, "Should auto-insert a parameters cell");
        Assert.AreEqual(0, stubOps.InsertedCells[0].Index, "Should insert at index 0");
        Assert.AreEqual("parameters", stubOps.InsertedCells[0].Type);
    }

    [TestMethod]
    public async Task Interaction_ParameterAdd_DoesNotDuplicateCell()
    {
        var notebook = new NotebookModel();
        notebook.Cells.Add(new CellModel { Type = "parameters" });
        notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp" });
        var variables = new Verso.Contexts.VariableStore();
        var stubOps = new StubNotebookOperations();

        var payload = JsonSerializer.Serialize(new { name = "x", type = "string" });
        var ctx = CreateInteractionContext("parameter-add", payload, notebook, variables, stubOps);

        await _renderer.OnCellInteractionAsync(ctx);

        Assert.AreEqual(0, stubOps.InsertedCells.Count, "Should not insert when parameters cell already exists");
    }

    // --- Interaction handler: parameter-remove ---

    [TestMethod]
    public async Task Interaction_ParameterRemove_DeletesDefinitionAndVariable()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-east" },
            ["count"] = new() { Type = "int", Default = 10L }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);
        variables.Set("region", "us-east");

        var payload = JsonSerializer.Serialize(new { name = "region" });
        var ctx = CreateInteractionContext("parameter-remove", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.IsFalse(parameters.ContainsKey("region"), "Parameter should be removed");
        Assert.IsFalse(variables.TryGet<string>("region", out _), "Variable should be removed");
        Assert.IsTrue(parameters.ContainsKey("count"), "Other parameters should remain");
    }

    // --- Interaction handler: parameter-submit ---

    [TestMethod]
    public async Task Interaction_ParameterSubmit_ValidValues_InjectsAll()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string" },
            ["count"] = new() { Type = "int" }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);

        var values = new Dictionary<string, string> { ["region"] = "eu-west", ["count"] = "500" };
        var payload = JsonSerializer.Serialize(new { values });
        var ctx = CreateInteractionContext("parameter-submit", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.AreEqual("eu-west", variables.Get<string>("region"));
        Assert.AreEqual(500L, variables.Get<long>("count"));
        Assert.IsTrue(result.Contains("verso-parameters-success"), "Should show success indicator");
    }

    [TestMethod]
    public async Task Interaction_ParameterSubmit_InvalidValue_ShowsFieldError()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string" },
            ["count"] = new() { Type = "int" }
        };
        var (notebook, variables) = CreateNotebookAndVariables(parameters);

        var values = new Dictionary<string, string> { ["region"] = "eu-west", ["count"] = "abc" };
        var payload = JsonSerializer.Serialize(new { values });
        var ctx = CreateInteractionContext("parameter-submit", payload, notebook, variables);

        var result = await _renderer.OnCellInteractionAsync(ctx);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("verso-parameter-error"), "Should contain field error");
        Assert.IsFalse(variables.TryGet<string>("region", out _), "Should not inject any values on error");
    }

    // --- Interaction handler: unknown type ---

    [TestMethod]
    public async Task Interaction_UnknownType_ReturnsNull()
    {
        var ctx = new CellInteractionContext
        {
            InteractionType = "unknown-action",
            Payload = "{}",
            ExtensionId = "verso.renderer.parameters"
        };

        var result = await _renderer.OnCellInteractionAsync(ctx);
        Assert.IsNull(result);
    }

    // --- Variable store takes precedence over default ---

    [TestMethod]
    public async Task RenderInput_VariableStoreTakesPrecedence()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["name"] = new() { Type = "string", Default = "default-val" }
        };
        var context = CreateContext(parameters);
        context.Variables.Set("name", "overridden");
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("overridden"), "Variable store value should be used");
    }

    // --- Remove button present ---

    [TestMethod]
    public async Task RenderInput_HasRemoveButton()
    {
        var parameters = new Dictionary<string, NotebookParameterDefinition>
        {
            ["x"] = new() { Type = "string" }
        };
        var context = CreateContext(parameters);
        var result = await _renderer.RenderInputAsync("", context);

        Assert.IsTrue(result.Content.Contains("verso-btn-remove"), "Should have remove button");
        Assert.IsTrue(result.Content.Contains("parameter-remove"), "Remove button should have correct action");
    }

    // --- Helpers ---

    private StubCellRenderContext CreateContext(
        Dictionary<string, NotebookParameterDefinition>? parameters)
    {
        var notebook = new NotebookModel { Parameters = parameters };

        return new StubCellRenderContext
        {
            NotebookMetadata = new NotebookMetadataContext(notebook),
            CellMetadata = new Dictionary<string, object>()
        };
    }

    private static (NotebookModel, Verso.Contexts.VariableStore) CreateNotebookAndVariables(
        Dictionary<string, NotebookParameterDefinition> parameters)
    {
        var notebook = new NotebookModel { Parameters = parameters };
        var variables = new Verso.Contexts.VariableStore();
        return (notebook, variables);
    }

    private static CellInteractionContext CreateInteractionContext(
        string interactionType,
        string payload,
        NotebookModel notebook,
        IVariableStore variables,
        INotebookOperations? notebookOps = null)
    {
        return new CellInteractionContext
        {
            InteractionType = interactionType,
            Payload = payload,
            ExtensionId = "verso.renderer.parameters",
            CellId = Guid.NewGuid(),
            NotebookModel = notebook,
            Variables = variables,
            Notebook = notebookOps
        };
    }
}
