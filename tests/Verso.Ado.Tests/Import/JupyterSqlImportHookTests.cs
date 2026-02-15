using Verso.Abstractions;
using Verso.Ado.Import;

namespace Verso.Ado.Tests.Import;

[TestClass]
public sealed class JupyterSqlImportHookTests
{
    private JupyterSqlImportHook _hook = null!;

    [TestInitialize]
    public void Setup()
    {
        _hook = new JupyterSqlImportHook();
    }

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

    [TestMethod]
    public async Task PostDeserialize_MssqlConnect_ConvertsToSqlConnect()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name mydb \"Server=localhost;Database=test\""
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        // Should have nuget cell + connect cell
        Assert.IsTrue(result.Cells.Count >= 2);

        var nugetCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#r \"nuget:"));
        Assert.IsNotNull(nugetCell);
        Assert.IsTrue(nugetCell!.Source.Contains("Microsoft.Data.SqlClient"));

        var connectCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#!sql-connect"));
        Assert.IsNotNull(connectCell);
        Assert.IsTrue(connectCell!.Source.Contains("--name mydb"));
        Assert.IsTrue(connectCell.Source.Contains("--provider Microsoft.Data.SqlClient"));
        Assert.IsTrue(connectCell.Source.Contains("Server=localhost;Database=test"));
    }

    [TestMethod]
    public async Task PostDeserialize_PostgresqlConnect_ConvertsWithNpgsql()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect postgresql --kernel-name pgdb \"Host=localhost;Database=test\""
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var connectCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#!sql-connect"));
        Assert.IsNotNull(connectCell);
        Assert.IsTrue(connectCell!.Source.Contains("--provider Npgsql"));
    }

    [TestMethod]
    public async Task PostDeserialize_MysqlConnect_ConvertsWithMySqlClient()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mysql --kernel-name mydb \"Server=localhost;Database=test\""
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var connectCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#!sql-connect"));
        Assert.IsNotNull(connectCell);
        Assert.IsTrue(connectCell!.Source.Contains("--provider MySql.Data.MySqlClient"));
    }

    [TestMethod]
    public async Task PostDeserialize_SqliteConnect_ConvertsWithSqlite()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect sqlite --kernel-name litdb \"Data Source=test.db\""
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var connectCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#!sql-connect"));
        Assert.IsNotNull(connectCell);
        Assert.IsTrue(connectCell!.Source.Contains("--provider Microsoft.Data.Sqlite"));
    }

    [TestMethod]
    public async Task PostDeserialize_SqlMagic_ConvertsToCellType()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!sql\nSELECT * FROM Products"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var sqlCell = result.Cells[0];
        Assert.AreEqual("code", sqlCell.Type);
        Assert.AreEqual("sql", sqlCell.Language);
        Assert.AreEqual("SELECT * FROM Products", sqlCell.Source);
    }

    [TestMethod]
    public async Task PostDeserialize_KernelNameMagic_ConvertsToSqlWithDirective()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                // First cell: establish the kernel name via #!connect
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name mydb \"Server=localhost\""
                },
                // Second cell: use #!mydb magic
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!mydb\nSELECT * FROM Products"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        // Find the SQL cell (skip nuget and connect cells)
        var sqlCell = result.Cells.LastOrDefault(c => c.Language == "sql");
        Assert.IsNotNull(sqlCell);
        Assert.IsTrue(sqlCell!.Source.Contains("--connection mydb"));
        Assert.IsTrue(sqlCell.Source.Contains("SELECT * FROM Products"));
    }

    [TestMethod]
    public async Task PostDeserialize_CreateDbContext_InsertsSqlScaffoldCell()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name mydb \"Server=localhost\" --create-dbcontext"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var scaffoldCell = result.Cells.FirstOrDefault(c => c.Source.Contains("#!sql-scaffold"));
        Assert.IsNotNull(scaffoldCell);
        Assert.IsTrue(scaffoldCell!.Source.Contains("--connection mydb"));
    }

    [TestMethod]
    public async Task PostDeserialize_NuGetDeduplication_DoesNotDuplicate()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                // Existing nuget reference
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#r \"nuget: Microsoft.Data.SqlClient\""
                },
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name db1 \"Server=s1\""
                },
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name db2 \"Server=s2\""
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        var nugetCells = result.Cells.Where(c =>
            c.Source.Contains("#r \"nuget:") && c.Source.Contains("Microsoft.Data.SqlClient")).ToList();

        Assert.AreEqual(1, nugetCells.Count, "Should not duplicate existing NuGet reference.");
    }

    [TestMethod]
    public async Task PostDeserialize_AddsVersoAdoToRequiredExtensions()
    {
        var notebook = new NotebookModel
        {
            Cells = new List<CellModel>
            {
                new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!sql\nSELECT 1"
                }
            }
        };

        var result = await _hook.PostDeserializeAsync(notebook, "test.ipynb");

        Assert.IsTrue(result.RequiredExtensions.Contains("verso.ado"));
    }

    [TestMethod]
    public async Task PostDeserialize_NonSqlNotebook_PassesThroughUnchanged()
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
        Assert.IsFalse(result.RequiredExtensions.Contains("verso.ado"));
    }

    [TestMethod]
    public async Task PreSerializeAsync_ReturnsUnchanged()
    {
        var notebook = new NotebookModel { Title = "Test" };

        var result = await _hook.PreSerializeAsync(notebook, null);

        Assert.AreSame(notebook, result);
    }
}
