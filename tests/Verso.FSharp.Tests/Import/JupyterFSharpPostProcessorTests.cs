using Verso.Abstractions;
using Verso.FSharp.Import;

namespace Verso.FSharp.Tests.Import;

[TestClass]
public sealed class JupyterFSharpPostProcessorTests
{
    private JupyterFSharpPostProcessor _hook = null!;

    [TestInitialize]
    public void Setup()
    {
        _hook = new JupyterFSharpPostProcessor();
    }

    // --- CanProcess ---

    [TestMethod]
    public void CanProcess_JupyterFormat_ReturnsTrue()
    {
        Assert.IsTrue(_hook.CanProcess(null, "jupyter"));
    }

    [TestMethod]
    public void CanProcess_IpynbFile_ReturnsTrue()
    {
        Assert.IsTrue(_hook.CanProcess("notebook.ipynb", "unknown"));
    }

    [TestMethod]
    public void CanProcess_VersoNative_ReturnsFalse()
    {
        Assert.IsFalse(_hook.CanProcess("notebook.vnb", "verso-native"));
    }

    // --- #!fsharp magic ---

    [TestMethod]
    public async Task PostDeserialize_FSharpMagic_SetsLanguageAndRemovesMagicLine()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!fsharp\nlet x = 42\nprintfn \"%d\" x"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var cell = result.Cells[0];
        Assert.AreEqual("fsharp", cell.Language);
        Assert.IsFalse(cell.Source.Contains("#!fsharp"), "Magic line should be removed");
        Assert.IsTrue(cell.Source.Contains("let x = 42"), "Remaining source should be preserved");
    }

    // --- #!f# magic ---

    [TestMethod]
    public async Task PostDeserialize_FHashMagic_SetsLanguageAndRemovesMagicLine()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!f#\nlet y = 99"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var cell = result.Cells[0];
        Assert.AreEqual("fsharp", cell.Language);
        Assert.IsFalse(cell.Source.Contains("#!f#"), "Magic line should be removed");
        Assert.IsTrue(cell.Source.Contains("let y = 99"));
    }

    // --- Cell metadata ---

    [TestMethod]
    public async Task PostDeserialize_CellMetadata_SetsLanguage()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Source = "let z = 1",
                    Metadata = new Dictionary<string, object>
                    {
                        ["dotnet_interactive"] = new Dictionary<string, object>
                        {
                            ["language"] = "fsharp"
                        }
                    }
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.AreEqual("fsharp", result.Cells[0].Language);
    }

    // --- #!set conversion ---

    [TestMethod]
    public async Task PostDeserialize_SetMagic_ConvertsToVariablesSet()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!set --name myVar --value @fsharp:42"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var cell = result.Cells[0];
        Assert.AreEqual("fsharp", cell.Language);
        Assert.IsTrue(cell.Source.Contains("Variables.Set(\"myVar\", 42)"),
            $"Expected Variables.Set call, got: {cell.Source}");
    }

    // --- #!share conversion ---

    [TestMethod]
    public async Task PostDeserialize_ShareMagic_ConvertsToVariablesGet()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!share --from csharp myData"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var cell = result.Cells[0];
        Assert.AreEqual("fsharp", cell.Language);
        Assert.IsTrue(cell.Source.Contains("let myData = Variables.Get<obj>(\"myData\")"),
            $"Expected let binding with Variables.Get, got: {cell.Source}");
        Assert.IsTrue(cell.Source.Contains("shared from csharp"),
            "Should contain a comment about the source kernel");
    }

    // --- RequiredExtensions ---

    [TestMethod]
    public async Task PostDeserialize_FSharpDetected_AddsVersoFSharpToRequiredExtensions()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!fsharp\nlet a = 1"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.IsTrue(result.RequiredExtensions.Contains("verso.fsharp"),
            "Should add verso.fsharp to required extensions");
    }

    [TestMethod]
    public async Task PostDeserialize_NoFSharp_DoesNotAddExtension()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "Console.WriteLine(\"Hello\");"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.IsFalse(result.RequiredExtensions.Contains("verso.fsharp"),
            "Should not add verso.fsharp when no F# detected");
    }

    // --- Pass-through ---

    [TestMethod]
    public async Task PostDeserialize_NonFSharpNotebook_PassesThroughUnchanged()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "Console.WriteLine(\"Hello\");"
                },
                new CellModel
                {
                    Type = "markdown",
                    Source = "# Title"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.AreEqual(2, result.Cells.Count);
        Assert.AreEqual("csharp", result.Cells[0].Language);
        Assert.AreEqual("markdown", result.Cells[1].Type);
    }

    [TestMethod]
    public async Task PostDeserialize_MarkdownCells_ArePreserved()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "markdown",
                    Source = "# My Notebook"
                },
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!fsharp\nlet x = 1"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.AreEqual("markdown", result.Cells[0].Type);
        Assert.AreEqual("# My Notebook", result.Cells[0].Source);
    }

    // --- PreSerializeAsync ---

    [TestMethod]
    public async Task PreSerializeAsync_ReturnsUnchanged()
    {
        var notebook = new NotebookModel { Title = "Test" };

        var result = await _hook.PreSerializeAsync(notebook, null);

        Assert.AreSame(notebook, result);
    }

    // --- Kernel spec detection ---

    [TestMethod]
    public async Task PostDeserialize_FSharpKernelSpec_SetsDefaultKernelId()
    {
        var notebook = new NotebookModel
        {
            DefaultKernelId = ".net-fsharp",
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Source = "let x = 1"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.AreEqual("fsharp", result.DefaultKernelId);
    }
}
