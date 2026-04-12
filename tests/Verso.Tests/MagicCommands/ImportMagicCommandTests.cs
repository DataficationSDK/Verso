using Verso.Abstractions;
using Verso.Contexts;
using Verso.MagicCommands;
using Verso.Serializers;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;
using StubNotebookOperations = Verso.Testing.Stubs.StubNotebookOperations;

namespace Verso.Tests.MagicCommands;

[TestClass]
public sealed class ImportMagicCommandTests
{
    // --- Metadata ---

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        var command = new ImportMagicCommand();

        Assert.AreEqual("import", command.Name);
        Assert.AreEqual("verso.magic.import", command.ExtensionId);
        Assert.AreEqual("1.0.0", command.Version);
        Assert.AreEqual(2, command.Parameters.Count);
        Assert.AreEqual("path", command.Parameters[0].Name);
        Assert.IsTrue(command.Parameters[0].IsRequired);
        Assert.AreEqual("--param", command.Parameters[1].Name);
        Assert.IsFalse(command.Parameters[1].IsRequired);
    }

    // --- Argument validation ---

    [TestMethod]
    public async Task EmptyArguments_SuppressesAndWritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("#!import"));
    }

    [TestMethod]
    public async Task WhitespaceArguments_SuppressesAndWritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("   ", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
    }

    // --- File not found ---

    [TestMethod]
    public async Task FileNotFound_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = CreateContextWithSerializer();

        await command.ExecuteAsync("/nonexistent/path/notebook.verso", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("File not found"));
    }

    // --- Unsupported format ---

    [TestMethod]
    public async Task UnsupportedExtension_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = CreateContextWithSerializer();

        // Use an extension that no serializer or kernel handles
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xyz");
        try
        {
            await File.WriteAllTextAsync(tempFile, "some content");

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(1, context.WrittenOutputs.Count);
            Assert.IsTrue(context.WrittenOutputs[0].IsError);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("No serializer or kernel"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Successful import ---

    [TestMethod]
    public async Task SuccessfulImport_ExecutesOnlyCodeCells()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        // Create a temp .verso file with mixed cell types
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });
            notebook.Cells.Add(new CellModel { Type = "markdown", Source = "# Header" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var y = 2;" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "" }); // empty — should be skipped
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "   " }); // whitespace — should be skipped

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(2, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("var x = 1;", notebookOps.ExecutedCodeCalls[0].Code);
            Assert.AreEqual("csharp", notebookOps.ExecutedCodeCalls[0].Language);
            Assert.AreEqual("var y = 2;", notebookOps.ExecutedCodeCalls[1].Code);

            // Confirmation message
            Assert.AreEqual(1, context.WrittenOutputs.Count);
            Assert.IsFalse(context.WrittenOutputs[0].IsError);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("2 code cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task SuccessfulImport_SingleCell_UsesSingular()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("1 code cell"));
            Assert.IsFalse(context.WrittenOutputs[0].Content.Contains("cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Path resolution ---

    [TestMethod]
    public void ResolvePath_AbsolutePath_ReturnsAsIs()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "notebook.verso");
        var result = ImportMagicCommand.ResolvePath(absolutePath, "/some/dir/current.verso");

        Assert.AreEqual(Path.GetFullPath(absolutePath), result);
    }

    [TestMethod]
    public void ResolvePath_RelativePath_ResolvesAgainstNotebookDir()
    {
        var notebookPath = Path.Combine(Path.GetTempPath(), "notebooks", "current.verso");
        var result = ImportMagicCommand.ResolvePath("helpers/setup.verso", notebookPath);

        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "notebooks", "helpers", "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_NullNotebookPath_ResolvesAgainstCwd()
    {
        var result = ImportMagicCommand.ResolvePath("setup.verso", null);

        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_EmptyNotebookPath_ResolvesAgainstCwd()
    {
        var result = ImportMagicCommand.ResolvePath("setup.verso", "");

        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "setup.verso"));
        Assert.AreEqual(expected, result);
    }

    // --- Argument parsing ---

    [TestMethod]
    public void ParseArguments_PathOnly()
    {
        var (path, overrides) = ImportMagicCommand.ParseArguments("pipeline.verso");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual(0, overrides.Count);
    }

    [TestMethod]
    public void ParseArguments_WithParams()
    {
        var (path, overrides) = ImportMagicCommand.ParseArguments(
            "pipeline.verso --param region=us-east --param limit=50");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual(2, overrides.Count);
        Assert.AreEqual("us-east", overrides["region"]);
        Assert.AreEqual("50", overrides["limit"]);
    }

    [TestMethod]
    public void ParseArguments_ShortFlag()
    {
        var (path, overrides) = ImportMagicCommand.ParseArguments(
            "pipeline.verso -p region=US");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual("US", overrides["region"]);
    }

    // --- Parameter resolution ---

    [TestMethod]
    public void Resolve_InjectsDefaults()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" },
                ["limit"] = new() { Type = "int", Default = "100" }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string>(), variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<object>("region", out var region));
        Assert.AreEqual("US", region);
        Assert.IsTrue(variables.TryGet<object>("limit", out var limit));
        Assert.AreEqual(100L, limit);
    }

    [TestMethod]
    public void Resolve_ParamOverridesDefault()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string> { ["region"] = "EU" }, variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<string>("region", out var region));
        Assert.AreEqual("EU", region);
    }

    [TestMethod]
    public void Resolve_ParamOverwritesExistingStoreValue()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" }
            }
        };

        var variables = new VariableStore();
        variables.Set("region", "APAC");
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string> { ["region"] = "EU" }, variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<string>("region", out var region));
        Assert.AreEqual("EU", region);
    }

    [TestMethod]
    public void Resolve_DefaultDoesNotOverwriteExistingStoreValue()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" }
            }
        };

        var variables = new VariableStore();
        variables.Set("region", "EU");
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string>(), variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<string>("region", out var region));
        Assert.AreEqual("EU", region);
    }

    [TestMethod]
    public void Resolve_CoercesParamToType()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["startDate"] = new() { Type = "date", Default = "2026-01-01" },
                ["limit"] = new() { Type = "int" }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook,
            new Dictionary<string, string> { ["startDate"] = "2026-06-15", ["limit"] = "200" },
            variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<object>("startDate", out var date));
        Assert.IsInstanceOfType(date, typeof(DateOnly));
        Assert.AreEqual(new DateOnly(2026, 6, 15), date);
        Assert.IsTrue(variables.TryGet<object>("limit", out var limit));
        Assert.AreEqual(200L, limit);
    }

    [TestMethod]
    public void Resolve_InvalidParamType_ReturnsError()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["limit"] = new() { Type = "int" }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string> { ["limit"] = "not-a-number" }, variables);

        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("limit"));
        Assert.IsTrue(error.Contains("int"));
    }

    [TestMethod]
    public void Resolve_MissingRequiredParam_ReturnsError()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Required = true }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string>(), variables);

        Assert.IsNotNull(error);
        Assert.IsTrue(error.Contains("region"));
        Assert.IsTrue(error.Contains("Missing required"));
    }

    [TestMethod]
    public void Resolve_RequiredParamSatisfiedByOverride_Succeeds()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Required = true }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string> { ["region"] = "US" }, variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<string>("region", out var region));
        Assert.AreEqual("US", region);
    }

    [TestMethod]
    public void Resolve_RequiredParamSatisfiedByStore_Succeeds()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Required = true }
            }
        };

        var variables = new VariableStore();
        variables.Set("region", "EU");
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string>(), variables);

        Assert.IsNull(error);
    }

    [TestMethod]
    public void Resolve_UnknownParam_InjectedAsString()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" }
            }
        };

        var variables = new VariableStore();
        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string> { ["extra"] = "hello" }, variables);

        Assert.IsNull(error);
        Assert.IsTrue(variables.TryGet<string>("extra", out var extra));
        Assert.AreEqual("hello", extra);
    }

    [TestMethod]
    public void Resolve_NullParameters_NoOp()
    {
        var notebook = new NotebookModel();
        var variables = new VariableStore();

        var error = ImportMagicCommand.ResolveAndInjectParameters(
            notebook, new Dictionary<string, string>(), variables);

        Assert.IsNull(error);
        Assert.IsFalse(variables.TryGet<object>("anything", out _));
    }

    // --- Integration: import with --param ---

    [TestMethod]
    public async Task Import_InjectsParameterDefaults_BeforeCodeCells()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US" },
                ["limit"] = new() { Type = "int", Default = 50 }
            };
            notebook.Cells.Add(new CellModel { Type = "parameters", Source = "" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.IsFalse(context.WrittenOutputs.Any(o => o.IsError),
                "Unexpected error: " + string.Join("; ", context.WrittenOutputs.Where(o => o.IsError).Select(o => o.Content)));
            Assert.IsTrue(context.Variables.TryGet<object>("region", out var region));
            Assert.AreEqual("US", region);
            Assert.IsTrue(context.Variables.TryGet<object>("limit", out var limit));
            Assert.AreEqual(50L, limit);
            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task Import_ParamOverrides_AppliedBeforeExecution()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "US", Required = true },
                ["limit"] = new() { Type = "int", Default = 10 }
            };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync($"{tempFile} --param region=eu-west --param limit=200", context);

            Assert.IsFalse(context.WrittenOutputs.Any(o => o.IsError),
                "Unexpected error: " + string.Join("; ", context.WrittenOutputs.Where(o => o.IsError).Select(o => o.Content)));
            Assert.IsTrue(context.Variables.TryGet<string>("region", out var region));
            Assert.AreEqual("eu-west", region);
            Assert.IsTrue(context.Variables.TryGet<object>("limit", out var limit));
            Assert.AreEqual(200L, limit);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task Import_MissingRequired_WritesErrorAndStops()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["apiKey"] = new() { Type = "string", Required = true }
            };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });

            var serializer = new VersoSerializer();
            var json = await serializer.SerializeAsync(notebook);
            await File.WriteAllTextAsync(tempFile, json);

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.WrittenOutputs.Any(o => o.IsError));
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("apiKey"));
            Assert.AreEqual(0, notebookOps.ExecutedCodeCalls.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- Source file import ---

    [TestMethod]
    public async Task ImportCsFile_ExecutesInCSharpKernel()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var csharpKernel = new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs", ".csx" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { csharpKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        try
        {
            await File.WriteAllTextAsync(tempFile, "var x = 1;\nConsole.WriteLine(x);");

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("csharp", notebookOps.ExecutedCodeCalls[0].Language);
            Assert.IsTrue(notebookOps.ExecutedCodeCalls[0].Code.Contains("var x = 1;"));
            Assert.IsTrue(context.WrittenOutputs.Any(o => !o.IsError && o.Content.Contains("Imported")));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ImportPyFile_ExecutesInPythonKernel()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var pythonKernel = new FakeLanguageKernel("python", "Python", fileExtensions: new[] { ".py" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { pythonKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.py");
        try
        {
            await File.WriteAllTextAsync(tempFile, "print('hello')");

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("python", notebookOps.ExecutedCodeCalls[0].Language);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ImportTsFile_ExecutesInJavaScriptKernel()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var jsKernel = new FakeLanguageKernel("javascript", "JavaScript", fileExtensions: new[] { ".js", ".mjs", ".ts", ".tsx" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { jsKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.ts");
        try
        {
            await File.WriteAllTextAsync(tempFile, "const x: number = 42;");

            await command.ExecuteAsync(tempFile, context);

            Assert.IsTrue(context.SuppressExecution);
            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("javascript", notebookOps.ExecutedCodeCalls[0].Language);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ImportSourceFile_ExtractsMagicCommands_NuGetFirst()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var csharpKernel = new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs", ".csx" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { csharpKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        try
        {
            var content = "#!import helper/Keys.cs\n#r \"nuget: Microsoft.Extensions.AI, 9.6.0\"\nusing Microsoft.Extensions.AI;\nvar client = new ChatClient();";
            await File.WriteAllTextAsync(tempFile, content);

            await command.ExecuteAsync(tempFile, context);

            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            var executedCode = notebookOps.ExecutedCodeCalls[0].Code;

            // NuGet should come before import
            var nugetPos = executedCode.IndexOf("#r \"nuget:", StringComparison.Ordinal);
            var importPos = executedCode.IndexOf("#!import", StringComparison.Ordinal);
            Assert.IsTrue(nugetPos >= 0, "NuGet directive should be present");
            Assert.IsTrue(importPos >= 0, "Import directive should be present");
            Assert.IsTrue(nugetPos < importPos, "NuGet should appear before import");

            // Code should come after directives
            var codePos = executedCode.IndexOf("using Microsoft", StringComparison.Ordinal);
            Assert.IsTrue(codePos > importPos, "Code should appear after directives");

            // Summary should mention extracted directives
            Assert.IsTrue(context.WrittenOutputs.Any(o => o.Content.Contains("2 directives extracted")));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ImportSourceFile_PipDirectivesFirst()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var pythonKernel = new FakeLanguageKernel("python", "Python", fileExtensions: new[] { ".py" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { pythonKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.py");
        try
        {
            var content = "#!import helpers/config.py\n#!pip pandas matplotlib\nimport pandas as pd";
            await File.WriteAllTextAsync(tempFile, content);

            await command.ExecuteAsync(tempFile, context);

            var executedCode = notebookOps.ExecutedCodeCalls[0].Code;
            var pipPos = executedCode.IndexOf("#!pip", StringComparison.Ordinal);
            var importPos = executedCode.IndexOf("#!import", StringComparison.Ordinal);
            Assert.IsTrue(pipPos < importPos, "pip should appear before import");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ImportSourceFile_NoMagicCommands_ExecutesCleanly()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var csharpKernel = new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { csharpKernel });

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.cs");
        try
        {
            await File.WriteAllTextAsync(tempFile, "Console.WriteLine(\"hello\");");

            await command.ExecuteAsync(tempFile, context);

            Assert.AreEqual(1, notebookOps.ExecutedCodeCalls.Count);
            Assert.IsTrue(context.WrittenOutputs.Any(o => o.Content.Contains("Imported") && !o.Content.Contains("directive")));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- ExtractMagicCommands ---

    [TestMethod]
    public void ExtractMagicCommands_SeparatesMagicFromCode()
    {
        var content = "#r \"nuget: Newtonsoft.Json, 13.0.3\"\n#!import helper.cs\nusing Newtonsoft.Json;\nvar x = 1;";
        var (magic, code) = ImportMagicCommand.ExtractMagicCommands(content);

        Assert.AreEqual(2, magic.Count);
        Assert.IsTrue(magic[0].Contains("nuget:"));
        Assert.IsTrue(magic[1].Contains("#!import"));
        Assert.AreEqual(2, code.Count);
        Assert.IsTrue(code[0].Contains("using"));
        Assert.IsTrue(code[1].Contains("var x"));
    }

    [TestMethod]
    public void ExtractMagicCommands_MagicInMiddleOfFile()
    {
        var content = "using System;\n#r \"nuget: Foo, 1.0\"\nConsole.WriteLine(42);";
        var (magic, code) = ImportMagicCommand.ExtractMagicCommands(content);

        Assert.AreEqual(1, magic.Count);
        Assert.AreEqual(2, code.Count);
    }

    [TestMethod]
    public void ExtractMagicCommands_NoMagicLines()
    {
        var content = "var x = 1;\nvar y = 2;";
        var (magic, code) = ImportMagicCommand.ExtractMagicCommands(content);

        Assert.AreEqual(0, magic.Count);
        Assert.AreEqual(2, code.Count);
    }

    [TestMethod]
    public void ExtractMagicCommands_NonNuGetHashR_TreatedAsCode()
    {
        // #r "SomeAssembly.dll" is NOT a NuGet directive and doesn't start with #!
        var content = "#r \"SomeAssembly.dll\"\nvar x = 1;";
        var (magic, code) = ImportMagicCommand.ExtractMagicCommands(content);

        Assert.AreEqual(0, magic.Count);
        Assert.AreEqual(2, code.Count);
    }

    // --- FindKernelByFileExtension ---

    [TestMethod]
    public void FindKernel_MatchesExtension()
    {
        var host = new SerializerAwareStubExtensionHost(
            kernels: new[] { new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs", ".csx" }) });

        var kernel = ImportMagicCommand.FindKernelByFileExtension(".cs", host);
        Assert.IsNotNull(kernel);
        Assert.AreEqual("csharp", kernel.LanguageId);
    }

    [TestMethod]
    public void FindKernel_CaseInsensitive()
    {
        var host = new SerializerAwareStubExtensionHost(
            kernels: new[] { new FakeLanguageKernel("python", "Python", fileExtensions: new[] { ".py" }) });

        var kernel = ImportMagicCommand.FindKernelByFileExtension(".PY", host);
        Assert.IsNotNull(kernel);
    }

    [TestMethod]
    public void FindKernel_NoMatch_ReturnsNull()
    {
        var host = new SerializerAwareStubExtensionHost(
            kernels: new[] { new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs" }) });

        var kernel = ImportMagicCommand.FindKernelByFileExtension(".xyz", host);
        Assert.IsNull(kernel);
    }

    [TestMethod]
    public void FindKernel_EmptyExtension_ReturnsNull()
    {
        var host = new SerializerAwareStubExtensionHost(
            kernels: new[] { new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs" }) });

        var kernel = ImportMagicCommand.FindKernelByFileExtension("", host);
        Assert.IsNull(kernel);
    }

    // --- Helpers ---

    private static StubMagicCommandContext CreateContextWithSerializer(
        StubNotebookOperations? notebookOps = null,
        IReadOnlyList<ILanguageKernel>? kernels = null)
    {
        var extensionHost = new SerializerAwareStubExtensionHost(kernels: kernels);
        var context = new StubMagicCommandContext
        {
            ExtensionHost = extensionHost
        };
        if (notebookOps is not null)
            context.Notebook = notebookOps;
        return context;
    }

    /// <summary>
    /// Stub extension host that returns a <see cref="VersoSerializer"/> from <see cref="GetSerializers"/>
    /// and optionally returns registered kernels.
    /// </summary>
    private sealed class SerializerAwareStubExtensionHost : IExtensionHostContext
    {
        private readonly IReadOnlyList<INotebookSerializer> _serializers = new INotebookSerializer[]
        {
            new VersoSerializer()
        };

        private readonly IReadOnlyList<ILanguageKernel> _kernels;

        public SerializerAwareStubExtensionHost(IReadOnlyList<ILanguageKernel>? kernels = null)
        {
            _kernels = kernels ?? Array.Empty<ILanguageKernel>();
        }

        public IReadOnlyList<IExtension> GetLoadedExtensions() => Array.Empty<IExtension>();
        public IReadOnlyList<ILanguageKernel> GetKernels() => _kernels;
        public IReadOnlyList<ICellRenderer> GetRenderers() => Array.Empty<ICellRenderer>();
        public IReadOnlyList<IDataFormatter> GetFormatters() => Array.Empty<IDataFormatter>();
        public IReadOnlyList<ICellType> GetCellTypes() => Array.Empty<ICellType>();
        public IReadOnlyList<INotebookSerializer> GetSerializers() => _serializers;
        public IReadOnlyList<ILayoutEngine> GetLayouts() => Array.Empty<ILayoutEngine>();
        public IReadOnlyList<ITheme> GetThemes() => Array.Empty<ITheme>();
        public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => Array.Empty<INotebookPostProcessor>();
        public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => Array.Empty<ExtensionInfo>();
        public Task EnableExtensionAsync(string extensionId) => Task.CompletedTask;
        public Task DisableExtensionAsync(string extensionId) => Task.CompletedTask;
    }
}
