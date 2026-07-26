using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Execution;

namespace Verso.Cli.Tests.Execution;

[TestClass]
public class JsonOutputWriterTests
{
    [TestMethod]
    public void Build_ProducesCorrectStructure()
    {
        var cell = new CellModel
        {
            Type = "code",
            Language = "csharp",
            Source = "Console.WriteLine(42);"
        };
        cell.Outputs.Add(new CellOutput("text/plain", "42"));

        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromMilliseconds(1200));

        var doc = JsonOutputWriter.Build(
            "/path/to/notebook.verso",
            new[] { cell },
            new[] { result },
            TimeSpan.FromMilliseconds(1200));

        Assert.AreEqual("/path/to/notebook.verso", doc.Notebook);
        Assert.AreEqual(1, doc.Cells.Count);
        Assert.AreEqual(0, doc.Cells[0].Index);
        Assert.AreEqual(cell.Id.ToString(), doc.Cells[0].Id);
        Assert.AreEqual("csharp", doc.Cells[0].Language);
        Assert.AreEqual("Success", doc.Cells[0].Status);
        Assert.AreEqual(1, doc.Cells[0].Outputs.Count);
        Assert.AreEqual("text/plain", doc.Cells[0].Outputs[0].MimeType);
        Assert.AreEqual("42", doc.Cells[0].Outputs[0].Content);
    }

    [TestMethod]
    public void Build_SummaryCountsAreCorrect()
    {
        var cells = new[]
        {
            new CellModel { Type = "code", Language = "csharp" },
            new CellModel { Type = "code", Language = "csharp" },
            new CellModel { Type = "code", Language = "csharp" }
        };

        var results = new[]
        {
            ExecutionResult.Success(cells[0].Id, 1, TimeSpan.FromSeconds(1)),
            ExecutionResult.Success(cells[1].Id, 2, TimeSpan.FromSeconds(2)),
            ExecutionResult.Failed(cells[2].Id, 3, TimeSpan.FromSeconds(1), new Exception("fail"))
        };

        var doc = JsonOutputWriter.Build("test.verso", cells, results, TimeSpan.FromSeconds(4));

        Assert.AreEqual(3, doc.Summary.Total);
        Assert.AreEqual(2, doc.Summary.Succeeded);
        Assert.AreEqual(1, doc.Summary.Failed);
    }

    /// <summary>
    /// The usual shape of a failing cell: a kernel catches the exception, reports it as an error
    /// output, and returns a completed execution. Counting the recorded status alone called a
    /// notebook full of tracebacks entirely successful, which contradicted the exit code.
    /// </summary>
    [TestMethod]
    public void Build_CellThatRaised_CountsAsFailedDespiteCompleting()
    {
        var cells = new[]
        {
            new CellModel { Type = "code", Language = "python" },
            new CellModel { Type = "code", Language = "python" }
        };

        cells[1].Outputs.Add(new CellOutput(
            "text/plain",
            "Traceback (most recent call last):\nModuleNotFoundError: No module named 'humanize'",
            IsError: true,
            ErrorName: "ModuleNotFoundError"));

        var results = new[]
        {
            ExecutionResult.Success(cells[0].Id, 1, TimeSpan.FromSeconds(1)),
            ExecutionResult.Success(cells[1].Id, 2, TimeSpan.FromSeconds(1))
        };

        var doc = JsonOutputWriter.Build("test.verso", cells, results, TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, doc.Summary.Succeeded);
        Assert.AreEqual(1, doc.Summary.Failed);
        Assert.AreEqual("Success", doc.Cells[0].Status);
        Assert.AreEqual("Failed", doc.Cells[1].Status, "the per-cell status must agree with the summary");
    }

    /// <summary>
    /// Progress bars, logging, and warnings are routinely written to standard error by programs
    /// that are working, so the channel on its own must not fail the cell or the run.
    /// </summary>
    [TestMethod]
    public void Build_CellThatWroteToStandardError_StillCountsAsSucceeded()
    {
        var cell = new CellModel { Type = "code", Language = "python" };
        cell.Outputs.Add(CellOutput.Stderr("working:  50%|#####     | 10/20"));

        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, doc.Summary.Succeeded);
        Assert.AreEqual(0, doc.Summary.Failed);
        Assert.AreEqual("Success", doc.Cells[0].Status);
    }

    /// <summary>
    /// A pipeline can opt back into strictness, which is the only thing that makes standard error
    /// count as a failure.
    /// </summary>
    [TestMethod]
    public void Build_WithFailOnStandardError_CountsStandardErrorAsFailed()
    {
        var cell = new CellModel { Type = "code", Language = "python" };
        cell.Outputs.Add(CellOutput.Stderr("DeprecationWarning: this will stop working"));

        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        CellOutcome.FailOnStandardError = true;
        try
        {
            var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));

            Assert.AreEqual(0, doc.Summary.Succeeded);
            Assert.AreEqual(1, doc.Summary.Failed);
            Assert.AreEqual("Failed", doc.Cells[0].Status);
        }
        finally
        {
            CellOutcome.FailOnStandardError = false;
        }
    }

    [TestMethod]
    public void Build_FailedCell_IncludesErrorInfo()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        cell.Outputs.Add(new CellOutput(
            "text/plain",
            "System.Exception: Something went wrong",
            IsError: true,
            ErrorName: "System.Exception",
            ErrorStackTrace: "at Main()"));

        var result = ExecutionResult.Failed(cell.Id, 1, TimeSpan.FromSeconds(1), new Exception("fail"));

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));

        Assert.AreEqual("Failed", doc.Cells[0].Status);
        Assert.AreEqual(true, doc.Cells[0].Outputs[0].IsError);
        Assert.AreEqual("System.Exception", doc.Cells[0].Outputs[0].ErrorName);
        Assert.AreEqual("at Main()", doc.Cells[0].Outputs[0].ErrorStackTrace);
    }

    [TestMethod]
    public void Build_ElapsedTimesFormatted()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var elapsed = TimeSpan.FromMilliseconds(4200);
        var result = ExecutionResult.Success(cell.Id, 1, elapsed);

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, elapsed);

        Assert.AreEqual(elapsed.ToString(), doc.Cells[0].Elapsed);
        Assert.AreEqual(elapsed.ToString(), doc.Summary.Elapsed);
    }

    [TestMethod]
    public void Serialize_ProducesValidJson()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        cell.Outputs.Add(new CellOutput("text/plain", "test"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));
        var json = JsonOutputWriter.Serialize(doc);

        // Verify it parses as valid JSON
        var parsed = JsonDocument.Parse(json);
        Assert.IsNotNull(parsed);

        // Verify camelCase naming
        Assert.IsTrue(json.Contains("\"notebook\""));
        Assert.IsTrue(json.Contains("\"cells\""));
        Assert.IsTrue(json.Contains("\"mimeType\""));
        Assert.IsTrue(json.Contains("\"summary\""));
    }

    [TestMethod]
    public void Build_WithVariables_IncludesVariablesSection()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));
        var variables = new List<VariableDescriptor>
        {
            new("count", 42, typeof(int)),
            new("name", "test", typeof(string))
        };

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result },
            TimeSpan.FromSeconds(1), variables);

        Assert.IsNotNull(doc.Variables);
        Assert.AreEqual(2, doc.Variables.Count);
        Assert.AreEqual(42, doc.Variables["count"]);
        Assert.AreEqual("test", doc.Variables["name"]);
    }

    [TestMethod]
    public void Build_WithoutVariables_NoVariablesSection()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));

        Assert.IsNull(doc.Variables);

        var json = JsonOutputWriter.Serialize(doc);
        Assert.IsFalse(json.Contains("\"variables\""));
    }

    [TestMethod]
    public void Build_WithParameters_IncludesParametersSection()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));
        var parameters = new Dictionary<string, object>
        {
            ["region"] = "us-east",
            ["batchSize"] = 1000L
        };

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result },
            TimeSpan.FromSeconds(1), parameters: parameters);

        Assert.IsNotNull(doc.Parameters);
        Assert.AreEqual(2, doc.Parameters.Count);
        Assert.AreEqual("us-east", doc.Parameters["region"]);
        Assert.AreEqual(1000L, doc.Parameters["batchSize"]);
    }

    [TestMethod]
    public void Build_WithoutParameters_NoParametersSection()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result }, TimeSpan.FromSeconds(1));

        Assert.IsNull(doc.Parameters);

        var json = JsonOutputWriter.Serialize(doc);
        Assert.IsFalse(json.Contains("\"parameters\""));
    }

    [TestMethod]
    public void Serialize_WithParameters_ProducesValidJson()
    {
        var cell = new CellModel { Type = "code", Language = "csharp" };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));
        var parameters = new Dictionary<string, object> { ["dryRun"] = false };

        var doc = JsonOutputWriter.Build("test.verso", new[] { cell }, new[] { result },
            TimeSpan.FromSeconds(1), parameters: parameters);
        var json = JsonOutputWriter.Serialize(doc);

        var parsed = JsonDocument.Parse(json);
        Assert.IsTrue(parsed.RootElement.TryGetProperty("parameters", out var paramsEl));
        Assert.AreEqual(JsonValueKind.Object, paramsEl.ValueKind);
        Assert.IsFalse(paramsEl.GetProperty("dryRun").GetBoolean());
    }
}
