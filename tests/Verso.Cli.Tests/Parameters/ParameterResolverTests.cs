using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Parameters;

namespace Verso.Cli.Tests.Parameters;

[TestClass]
public class ParameterResolverTests
{
    [TestMethod]
    public void Resolve_NoDefinitionsNoParams_ReturnsEmpty()
    {
        var resolver = new ParameterResolver(null, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Parameters.Count);
    }

    [TestMethod]
    public void Resolve_DefaultsApplied_WhenNoCliOverride()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-west-2" },
            ["batchSize"] = new() { Type = "int", Default = 1000L }
        };

        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("us-west-2", result.Parameters["region"]);
        Assert.AreEqual(1000L, result.Parameters["batchSize"]);
    }

    [TestMethod]
    public void Resolve_CliOverrides_TakePrecedenceOverDefaults()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-west-2" }
        };
        var cliParams = new Dictionary<string, string> { ["region"] = "eu-west-1" };

        var resolver = new ParameterResolver(defs, cliParams, isVersoFormat: true, error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("eu-west-1", result.Parameters["region"]);
    }

    [TestMethod]
    public void Resolve_RequiredMissing_ReturnsFailure()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["date"] = new() { Type = "date", Required = true, Description = "Processing date" },
            ["region"] = new() { Type = "string", Required = true }
        };

        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage, "Missing required");
        StringAssert.Contains(result.ErrorMessage, "date");
        StringAssert.Contains(result.ErrorMessage, "region");
    }

    [TestMethod]
    public void Resolve_RequiredWithDefault_Succeeds()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Required = true, Default = "us-east-1" }
        };

        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("us-east-1", result.Parameters["region"]);
    }

    [TestMethod]
    public void Resolve_UnknownParam_InjectsAsStringWithWarning()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-west-2" }
        };
        var cliParams = new Dictionary<string, string> { ["unknown"] = "value" };

        var errorWriter = new StringWriter();
        var resolver = new ParameterResolver(defs, cliParams, isVersoFormat: true, error: errorWriter);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("value", result.Parameters["unknown"]);
        StringAssert.Contains(errorWriter.ToString(), "Warning");
        StringAssert.Contains(errorWriter.ToString(), "unknown");
    }

    [TestMethod]
    public void Resolve_InvalidValue_ReturnsFailure()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["batchSize"] = new() { Type = "int" }
        };
        var cliParams = new Dictionary<string, string> { ["batchSize"] = "abc" };

        var resolver = new ParameterResolver(defs, cliParams, isVersoFormat: true, error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage, "batchSize");
        StringAssert.Contains(result.ErrorMessage, "integer");
    }

    [TestMethod]
    public void Resolve_TypeParsing_AllTypes()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["s"] = new() { Type = "string" },
            ["i"] = new() { Type = "int" },
            ["f"] = new() { Type = "float" },
            ["b"] = new() { Type = "bool" },
            ["d"] = new() { Type = "date" },
            ["dt"] = new() { Type = "datetime" }
        };
        var cliParams = new Dictionary<string, string>
        {
            ["s"] = "hello",
            ["i"] = "42",
            ["f"] = "3.14",
            ["b"] = "true",
            ["d"] = "2024-01-15",
            ["dt"] = "2024-01-15T08:00:00Z"
        };

        var resolver = new ParameterResolver(defs, cliParams, isVersoFormat: true, error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("hello", result.Parameters["s"]);
        Assert.AreEqual(42L, result.Parameters["i"]);
        Assert.AreEqual(3.14, result.Parameters["f"]);
        Assert.AreEqual(true, result.Parameters["b"]);
        Assert.AreEqual(new DateOnly(2024, 1, 15), result.Parameters["d"]);
        Assert.IsInstanceOfType(result.Parameters["dt"], typeof(DateTimeOffset));
    }

    [TestMethod]
    public void Resolve_OrderSorting_RespectsOrderField()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["charlie"] = new() { Type = "string", Default = "c", Order = 3 },
            ["alpha"] = new() { Type = "string", Default = "a", Order = 1 },
            ["bravo"] = new() { Type = "string", Default = "b", Order = 2 }
        };

        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        var keys = result.Parameters.Keys.ToList();
        Assert.AreEqual("alpha", keys[0]);
        Assert.AreEqual("bravo", keys[1]);
        Assert.AreEqual("charlie", keys[2]);
    }

    [TestMethod]
    public void Resolve_UnorderedParams_SortAlphabetically()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["zebra"] = new() { Type = "string", Default = "z" },
            ["alpha"] = new() { Type = "string", Default = "a" },
            ["middle"] = new() { Type = "string", Default = "m" }
        };

        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            error: TextWriter.Null);
        var result = resolver.Resolve();

        var keys = result.Parameters.Keys.ToList();
        Assert.AreEqual("alpha", keys[0]);
        Assert.AreEqual("middle", keys[1]);
        Assert.AreEqual("zebra", keys[2]);
    }

    [TestMethod]
    public void Resolve_NonVersoFormat_InjectsAsStrings()
    {
        var cliParams = new Dictionary<string, string>
        {
            ["region"] = "us-east",
            ["batchSize"] = "1000"
        };

        var errorWriter = new StringWriter();
        var resolver = new ParameterResolver(null, cliParams, isVersoFormat: false, error: errorWriter);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("us-east", result.Parameters["region"]);
        Assert.AreEqual("1000", result.Parameters["batchSize"]); // String, not long
        StringAssert.Contains(errorWriter.ToString(), "Warning");
    }

    [TestMethod]
    public void Resolve_NonVersoFormat_NoParams_NoWarning()
    {
        var errorWriter = new StringWriter();
        var resolver = new ParameterResolver(null, new Dictionary<string, string>(), isVersoFormat: false,
            error: errorWriter);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Parameters.Count);
        Assert.AreEqual("", errorWriter.ToString());
    }

    [TestMethod]
    public void Resolve_Interactive_PromptsForMissing()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Required = true, Description = "AWS region" }
        };

        var input = new StringReader("us-east\n");
        var output = new StringWriter();
        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            interactive: true, input: input, output: output, error: TextWriter.Null, isInputRedirected: false);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("us-east", result.Parameters["region"]);
    }

    [TestMethod]
    public void Resolve_Interactive_InvalidInput_RepromptsUntilValid()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["batchSize"] = new() { Type = "int", Required = true }
        };

        // First two lines are invalid, third is valid
        var input = new StringReader("abc\nnot-a-number\n42\n");
        var output = new StringWriter();
        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            interactive: true, input: input, output: output, error: TextWriter.Null, isInputRedirected: false);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(42L, result.Parameters["batchSize"]);

        // Verify re-prompt messages were written
        var outputText = output.ToString();
        StringAssert.Contains(outputText, "integer");
    }

    [TestMethod]
    public void Resolve_Interactive_AcceptsDefault()
    {
        var defs = new Dictionary<string, NotebookParameterDefinition>
        {
            ["region"] = new() { Type = "string", Default = "us-west-2" }
        };

        var input = new StringReader("\n"); // Empty line accepts default
        var output = new StringWriter();
        var resolver = new ParameterResolver(defs, new Dictionary<string, string>(), isVersoFormat: true,
            interactive: true, input: input, output: output, error: TextWriter.Null, isInputRedirected: false);
        var result = resolver.Resolve();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("us-west-2", result.Parameters["region"]);
    }
}
