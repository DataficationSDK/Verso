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
        Assert.AreEqual(3, command.Parameters.Count);
        Assert.AreEqual("path", command.Parameters[0].Name);
        Assert.IsTrue(command.Parameters[0].IsRequired);
        Assert.AreEqual("--param", command.Parameters[1].Name);
        Assert.IsFalse(command.Parameters[1].IsRequired);
        Assert.AreEqual("--show-output", command.Parameters[2].Name);
        Assert.IsFalse(command.Parameters[2].IsRequired);
    }

    // --- Argument validation ---

    [TestMethod]
    public async Task EmptyArguments_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsFalse(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("#!import"));
    }

    [TestMethod]
    public async Task WhitespaceArguments_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("   ", context);

        Assert.IsFalse(context.SuppressExecution);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
    }

    // --- File not found ---

    [TestMethod]
    public async Task FileNotFound_WritesError()
    {
        var command = new ImportMagicCommand();
        var context = CreateContextWithSerializer();

        await command.ExecuteAsync("/nonexistent/path/notebook.verso", context);

        Assert.IsFalse(context.SuppressExecution);
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

            Assert.IsFalse(context.SuppressExecution);
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

            Assert.IsFalse(context.SuppressExecution);
            Assert.AreEqual(2, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("var x = 1;", notebookOps.ExecutedCodeCalls[0].Code);
            Assert.AreEqual("csharp", notebookOps.ExecutedCodeCalls[0].Language);
            Assert.AreEqual("var y = 2;", notebookOps.ExecutedCodeCalls[1].Code);

            // Confirmation message
            Assert.AreEqual(1, context.WrittenOutputs.Count);
            Assert.IsFalse(context.WrittenOutputs[0].IsError);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("2 cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task Import_FailingCell_SurfacesErrorWithoutShowOutput()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations
        {
            // The failing cell yields an error output plus a normal output; the healthy cell
            // yields only a normal output.
            CaptureOutputsHandler = (code, _) => code.Contains("boom")
                ? new[]
                {
                    new CellOutput("text/plain", "kaboom", IsError: true, ErrorName: "CompilationError"),
                    new CellOutput("text/plain", "partial result")
                }
                : new[] { new CellOutput("text/plain", "healthy result") }
        };
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var ok = 1;" });
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "boom" });

            var serializer = new VersoSerializer();
            await File.WriteAllTextAsync(tempFile, await serializer.SerializeAsync(notebook));

            await command.ExecuteAsync(tempFile, context); // silent mode: no --show-output

            Assert.AreEqual(2, notebookOps.ExecutedCodeCalls.Count);

            // The error output surfaces even without --show-output; normal outputs stay silent.
            Assert.IsTrue(context.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("kaboom")));
            Assert.IsFalse(context.WrittenOutputs.Any(o => o.Content.Contains("healthy result")));
            Assert.IsFalse(context.WrittenOutputs.Any(o => o.Content.Contains("partial result")));

            // The summary reports the failure and is itself an error output.
            var summary = context.WrittenOutputs[^1];
            Assert.IsTrue(summary.IsError);
            Assert.IsTrue(summary.Content.Contains("Imported 2 cells"));
            Assert.IsTrue(summary.Content.Contains("(1 failed)"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task Import_ShowOutput_SurfacesAllOutputs()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations
        {
            CaptureOutputsHandler = (_, _) => new[] { new CellOutput("text/plain", "healthy result") }
        };
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var ok = 1;" });

            var serializer = new VersoSerializer();
            await File.WriteAllTextAsync(tempFile, await serializer.SerializeAsync(notebook));

            await command.ExecuteAsync($"{tempFile} --show-output", context);

            Assert.IsTrue(context.WrittenOutputs.Any(o => !o.IsError && o.Content.Contains("healthy result")));

            var summary = context.WrittenOutputs[^1];
            Assert.IsFalse(summary.IsError);
            Assert.IsTrue(summary.Content.Contains("Imported 1 cell"));
            Assert.IsFalse(summary.Content.Contains("failed"));
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
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("1 cell"));
            Assert.IsFalse(context.WrittenOutputs[0].Content.Contains("cells"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task SuccessfulImport_ExecutesNonCodeCellTypes_WithLanguageRouting()
    {
        // Regression: native .verso files store SQL cells as Type="sql" (likewise for python,
        // fsharp, etc). The earlier filter only counted Type=="code" and silently skipped them,
        // surfacing as "Imported 0 code cells" even when the imported notebook clearly had
        // runnable cells. Mixed-type imports must execute every cell that carries source.
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = CreateContextWithSerializer(notebookOps);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.verso");
        try
        {
            var notebook = new NotebookModel { DefaultKernelId = "csharp" };
            notebook.Cells.Add(new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" });
            notebook.Cells.Add(new CellModel { Type = "markdown", Source = "# skip me" });
            notebook.Cells.Add(new CellModel { Type = "sql", Language = "sql", Source = "SELECT 1" });
            notebook.Cells.Add(new CellModel { Type = "raw", Source = "skip me too" });
            notebook.Cells.Add(new CellModel { Type = "python", Language = "python", Source = "print('hi')" });

            var serializer = new VersoSerializer();
            await File.WriteAllTextAsync(tempFile, await serializer.SerializeAsync(notebook));

            await command.ExecuteAsync(tempFile, context);

            Assert.AreEqual(3, notebookOps.ExecutedCodeCalls.Count);
            Assert.AreEqual("var x = 1;", notebookOps.ExecutedCodeCalls[0].Code);
            Assert.AreEqual("csharp", notebookOps.ExecutedCodeCalls[0].Language);
            Assert.AreEqual("SELECT 1", notebookOps.ExecutedCodeCalls[1].Code);
            Assert.AreEqual("sql", notebookOps.ExecutedCodeCalls[1].Language);
            Assert.AreEqual("print('hi')", notebookOps.ExecutedCodeCalls[2].Code);
            Assert.AreEqual("python", notebookOps.ExecutedCodeCalls[2].Language);
            Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("3 cells"));
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
        var (path, overrides, showOutput) = ImportMagicCommand.ParseArguments("pipeline.verso");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual(0, overrides.Count);
        Assert.IsFalse(showOutput);
    }

    [TestMethod]
    public void ParseArguments_WithParams()
    {
        var (path, overrides, showOutput) = ImportMagicCommand.ParseArguments(
            "pipeline.verso --param region=us-east --param limit=50");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual(2, overrides.Count);
        Assert.AreEqual("us-east", overrides["region"]);
        Assert.AreEqual("50", overrides["limit"]);
        Assert.IsFalse(showOutput);
    }

    [TestMethod]
    public void ParseArguments_ShortFlag()
    {
        var (path, overrides, _) = ImportMagicCommand.ParseArguments(
            "pipeline.verso -p region=US");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual("US", overrides["region"]);
    }

    [TestMethod]
    public void ParseArguments_ShowOutputFlag()
    {
        var (path, overrides, showOutput) = ImportMagicCommand.ParseArguments(
            "pipeline.verso --show-output");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual(0, overrides.Count);
        Assert.IsTrue(showOutput);
    }

    [TestMethod]
    public void ParseArguments_ShowOutputFlagBeforePath()
    {
        var (path, overrides, showOutput) = ImportMagicCommand.ParseArguments(
            "--show-output pipeline.verso --param region=US");

        Assert.AreEqual("pipeline.verso", path);
        Assert.AreEqual("US", overrides["region"]);
        Assert.IsTrue(showOutput);
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

            Assert.IsFalse(context.SuppressExecution);
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

            Assert.IsFalse(context.SuppressExecution);
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

            Assert.IsFalse(context.SuppressExecution);
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

            // Each directive is submitted on its own, and the file's own code last, so that a
            // directive behind another is still acted on rather than reaching the kernel as code.
            var submissions = notebookOps.ExecutedCodeCalls.Select(c => c.Code).ToList();
            Assert.AreEqual(3, submissions.Count);

            var nugetIndex = submissions.FindIndex(s => s.StartsWith("#r \"nuget:", StringComparison.Ordinal));
            var importIndex = submissions.FindIndex(s => s.StartsWith("#!import", StringComparison.Ordinal));
            Assert.IsTrue(nugetIndex >= 0, "NuGet directive should be submitted");
            Assert.IsTrue(importIndex >= 0, "Import directive should be submitted");
            Assert.IsTrue(nugetIndex < importIndex, "NuGet should be submitted before import");

            // The code follows the directives, and carries none of them.
            var code = submissions[^1];
            Assert.IsTrue(code.Contains("using Microsoft", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("#!import", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("#r \"nuget:", StringComparison.Ordinal));

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

            var submissions = notebookOps.ExecutedCodeCalls.Select(c => c.Code).ToList();
            var pipIndex = submissions.FindIndex(s => s.StartsWith("#!pip", StringComparison.Ordinal));
            var importIndex = submissions.FindIndex(s => s.StartsWith("#!import", StringComparison.Ordinal));
            Assert.IsTrue(pipIndex >= 0, "pip directive should be submitted");
            Assert.IsTrue(importIndex >= 0, "Import directive should be submitted");
            Assert.IsTrue(pipIndex < importIndex, "pip should be submitted before import");
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

    // --- Nested imports ---

    [TestMethod]
    public async Task ImportSourceFile_NestedImportBehindPackageDirective_IsStillRun()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var csharpKernel = new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { csharpKernel });

        var directory = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            var nested = Path.Combine(directory.FullName, "Keys.cs");
            await File.WriteAllTextAsync(nested, "public static class Keys { }");

            var importer = Path.Combine(directory.FullName, "Fetcher.cs");
            await File.WriteAllTextAsync(importer,
                "#!import Keys.cs\n#r \"nuget: Newtonsoft.Json, 13.0.3\"\npublic static class Fetcher { }");

            await command.ExecuteAsync(importer, context);

            // The package directive sorts ahead of the import, which is what used to hide the
            // import from the reader of the reassembled content.
            var submissions = notebookOps.ExecutedCodeCalls.Select(c => c.Code).ToList();
            Assert.IsTrue(submissions.Any(s => s.StartsWith("#r \"nuget:", StringComparison.Ordinal)));
            Assert.IsTrue(
                submissions.Any(s => s.StartsWith("#!import", StringComparison.Ordinal)),
                "The nested import must be submitted, not swallowed behind the package directive.");
            Assert.IsTrue(
                submissions.Any(s => s.Contains("public static class Fetcher", StringComparison.Ordinal)),
                "The file's own code must run after its directives.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveImportPath_PrefersTheFileBesideTheImporter()
    {
        var directory = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            var beside = Path.Combine(directory.FullName, "Keys.cs");
            File.WriteAllText(beside, "public static class Keys { }");

            var resolved = ImportMagicCommand.ResolveImportPath(
                "Keys.cs", directory.FullName, "/somewhere/else/notebook.verso");

            Assert.AreEqual(beside, resolved);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveImportPath_UnderADirectoryWhoseNameHasASpace()
    {
        // The path a nested import resolves to is never written back into the directive, so a
        // space in it cannot be read as the end of the path. Rewriting the line lost everything
        // from the space onward.
        var root = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            var spaced = Directory.CreateDirectory(Path.Combine(root.FullName, "my helpers"));
            var beside = Path.Combine(spaced.FullName, "Keys.cs");
            File.WriteAllText(beside, "public static class Keys { }");

            var resolved = ImportMagicCommand.ResolveImportPath(
                "Keys.cs", spaced.FullName, Path.Combine(root.FullName, "notebook.verso"));

            Assert.AreEqual(beside, resolved);
            Assert.IsTrue(File.Exists(resolved), "The resolved path must still name a real file.");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveImportPath_FallsBackToTheNotebookWhenNothingSitsBeside()
    {
        var root = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            // The importing file has no shared/Common.cs beside it, but the notebook does, so
            // a path written against the notebook goes on meaning what it did.
            var importer = Directory.CreateDirectory(Path.Combine(root.FullName, "helpers"));
            var shared = Directory.CreateDirectory(Path.Combine(root.FullName, "shared"));
            var target = Path.Combine(shared.FullName, "Common.cs");
            File.WriteAllText(target, "public static class Common { }");

            var resolved = ImportMagicCommand.ResolveImportPath(
                "shared/Common.cs", importer.FullName, Path.Combine(root.FullName, "notebook.verso"));

            Assert.AreEqual(target, resolved);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveImportPath_RootedPathIsTakenAsWritten()
    {
        var directory = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            var rooted = Path.Combine(directory.FullName, "Absolute.cs");
            File.WriteAllText(rooted, "public static class Absolute { }");

            var resolved = ImportMagicCommand.ResolveImportPath(
                rooted, "/some/other/place", "/somewhere/else/notebook.verso");

            Assert.AreEqual(rooted, resolved);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ResolveImportPath_TopLevelImportResolvesAgainstTheNotebook()
    {
        // Nothing is being imported yet, so there is no importing directory and the notebook
        // answers, exactly as it did before nesting was addressed.
        var resolved = ImportMagicCommand.ResolveImportPath(
            "helpers/setup.verso", null, "/notebooks/main.verso");

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine("/notebooks", "helpers/setup.verso")), resolved);
    }

    [TestMethod]
    public async Task ImportSourceFile_CircularImport_Terminates()
    {
        var command = new ImportMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var csharpKernel = new FakeLanguageKernel("csharp", "C#", fileExtensions: new[] { ".cs" });
        var context = CreateContextWithSerializer(notebookOps, kernels: new[] { csharpKernel });

        // The stub does not run the directives it is handed, so drive the cycle directly:
        // importing the same file from inside itself must be passed over rather than recur.
        var directory = Directory.CreateTempSubdirectory("verso_import_");
        try
        {
            var first = Path.Combine(directory.FullName, "First.cs");
            await File.WriteAllTextAsync(first, "#!import First.cs\npublic static class First { }");

            await command.ExecuteAsync(first, context);

            Assert.IsTrue(context.WrittenOutputs.Any(o => o.Content.Contains("Imported")));
            Assert.IsFalse(context.WrittenOutputs.Any(o => o.IsError));
        }
        finally
        {
            directory.Delete(recursive: true);
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
