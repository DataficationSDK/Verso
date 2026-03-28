using System.Text.Json;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Extensions;
using Verso.Extensions.Renderers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ParametersCellIntegrationTests
{
    /// <summary>
    /// Verifies that a parameterized notebook renders form HTML from the parameters cell
    /// when loaded via Scaffold with extension discovery.
    /// </summary>
    [TestMethod]
    public async Task Scaffold_ParameterizedNotebook_RendersParametersCell()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-east", Description = "AWS region" },
                ["batchSize"] = new() { Type = "int", Default = 1000L }
            }
        };
        notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });

        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        await using var scaffold = new Verso.Scaffold(notebook, host);
        scaffold.InitializeSubsystems();

        var results = await scaffold.ExecuteAllAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Success", results[0].Status.ToString());

        // The parameters cell should have output with form HTML
        var paramsCell = notebook.Cells[0];
        Assert.IsTrue(paramsCell.Outputs.Count > 0, "Parameters cell should have output");
        Assert.IsTrue(paramsCell.Outputs[0].Content.Contains("region"), "Output should contain parameter name");
        Assert.IsTrue(paramsCell.Outputs[0].Content.Contains("batchSize"), "Output should contain second parameter");
    }

    /// <summary>
    /// Verifies that parameter-update interaction updates the variable store,
    /// making the value accessible for subsequent code execution.
    /// </summary>
    [TestMethod]
    public async Task InteractionThenExecution_ParameterAccessibleInVariableStore()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-east" }
            }
        };

        var variables = new VariableStore();
        var renderer = new ParametersCellRenderer();

        // Fire parameter-update interaction
        var payload = JsonSerializer.Serialize(new { name = "region", value = "eu-west" });
        var ctx = new CellInteractionContext
        {
            InteractionType = "parameter-update",
            Payload = payload,
            ExtensionId = "verso.renderer.parameters",
            CellId = Guid.NewGuid(),
            NotebookModel = notebook,
            Variables = variables
        };

        var result = await renderer.OnCellInteractionAsync(ctx);
        Assert.IsNotNull(result);

        // Verify the value is in the variable store
        Assert.AreEqual("eu-west", variables.Get<string>("region"));
        Assert.AreEqual("eu-west", notebook.Parameters!["region"].Default);
    }

    /// <summary>
    /// Verifies that parameter-submit validates and injects all values at once.
    /// </summary>
    [TestMethod]
    public async Task ParameterSubmit_InjectsAllValues()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string" },
                ["count"] = new() { Type = "int" },
                ["enabled"] = new() { Type = "bool" }
            }
        };

        var variables = new VariableStore();
        var renderer = new ParametersCellRenderer();

        var values = new Dictionary<string, string>
        {
            ["region"] = "ap-southeast",
            ["count"] = "250",
            ["enabled"] = "true"
        };

        var payload = JsonSerializer.Serialize(new { values });
        var ctx = new CellInteractionContext
        {
            InteractionType = "parameter-submit",
            Payload = payload,
            ExtensionId = "verso.renderer.parameters",
            CellId = Guid.NewGuid(),
            NotebookModel = notebook,
            Variables = variables
        };

        var result = await renderer.OnCellInteractionAsync(ctx);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("verso-parameters-success"));

        Assert.AreEqual("ap-southeast", variables.Get<string>("region"));
        Assert.AreEqual(250L, variables.Get<long>("count"));
        Assert.AreEqual(true, variables.Get<bool>("enabled"));
    }

    /// <summary>
    /// Verifies ExecuteAllAsync with a parameters cell: the cell is rendered
    /// successfully (no kernel, render-only path) and does not block execution.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAll_ParametersCellDoesNotBlock()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["x"] = new() { Type = "string", Default = "test" }
            }
        };
        notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });

        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        await using var scaffold = new Verso.Scaffold(notebook, host);
        scaffold.InitializeSubsystems();

        var results = await scaffold.ExecuteAllAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Success", results[0].Status.ToString());
    }

    /// <summary>
    /// Verifies that ExecuteAllAsync blocks when a required parameter has no value.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAll_RequiredParamMissing_BlocksExecution()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-east" },
                ["requiredValue"] = new() { Type = "string", Required = true }
            }
        };
        notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });
        notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "Console.WriteLine(\"should not run\");" });

        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        await using var scaffold = new Verso.Scaffold(notebook, host);
        scaffold.InitializeSubsystems();

        var results = await scaffold.ExecuteAllAsync();

        Assert.AreEqual(1, results.Count, "Should return a single failed result");
        Assert.AreEqual("Failed", results[0].Status.ToString());

        // The parameters cell should have an error output
        var paramsCell = notebook.Cells[0];
        Assert.IsTrue(paramsCell.Outputs.Any(o => o.IsError), "Parameters cell should show validation error");
    }

    /// <summary>
    /// Verifies that an empty string in the variable store does not satisfy a required string parameter.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAll_RequiredStringWithEmptyValue_BlocksExecution()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["requiredValue"] = new() { Type = "string", Required = true }
            }
        };
        notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });

        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        await using var scaffold = new Verso.Scaffold(notebook, host);
        scaffold.InitializeSubsystems();

        // Pre-set empty string to simulate stale variable store
        scaffold.Variables.Set("requiredValue", "");

        var results = await scaffold.ExecuteAllAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Failed", results[0].Status.ToString(), "Empty string should not satisfy required parameter");
    }

    /// <summary>
    /// Verifies that a required parameter with a provided value allows execution.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAll_RequiredParamWithValue_Succeeds()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["requiredValue"] = new() { Type = "string", Required = true }
            }
        };
        notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });

        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();
        await using var scaffold = new Verso.Scaffold(notebook, host);
        scaffold.InitializeSubsystems();

        scaffold.Variables.Set("requiredValue", "hello");

        var results = await scaffold.ExecuteAllAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Success", results[0].Status.ToString());
    }
}
