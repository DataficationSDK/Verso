using Verso.Abstractions;
using Verso.Testing.Stubs;

namespace Verso.Sample.Dice.Tests;

[TestClass]
public sealed class DiceRollActionTests
{
    private readonly DiceRollAction _action = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("dice.action.roll-all", _action.ActionId);
        Assert.AreEqual("Roll All", _action.DisplayName);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, _action.Placement);
    }

    [TestMethod]
    public async Task IsEnabled_WithDiceCells_ReturnsTrue()
    {
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[]
            {
                new CellModel { Language = "dice", Source = "2d6" }
            }
        };

        Assert.IsTrue(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabled_WithoutDiceCells_ReturnsFalse()
    {
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[]
            {
                new CellModel { Language = "csharp", Source = "var x = 1;" }
            }
        };

        Assert.IsFalse(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabled_EmptyNotebook_ReturnsFalse()
    {
        var context = new StubToolbarActionContext
        {
            NotebookCells = Array.Empty<CellModel>()
        };

        Assert.IsFalse(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task Execute_ExecutesOnlyDiceCells()
    {
        var diceCell = new CellModel { Language = "dice", Source = "2d6" };
        var codeCell = new CellModel { Language = "csharp", Source = "var x = 1;" };
        var notebook = new StubNotebookOperations();
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[] { diceCell, codeCell },
            Notebook = notebook
        };

        await _action.ExecuteAsync(context);

        Assert.AreEqual(1, notebook.ExecutedCellIds.Count);
        Assert.AreEqual(diceCell.Id, notebook.ExecutedCellIds[0]);
    }

    [TestMethod]
    public async Task Execute_MultipleDiceCells_ExecutesAll()
    {
        var cell1 = new CellModel { Language = "dice", Source = "2d6" };
        var cell2 = new CellModel { Language = "dice", Source = "1d20" };
        var notebook = new StubNotebookOperations();
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[] { cell1, cell2 },
            Notebook = notebook
        };

        await _action.ExecuteAsync(context);

        Assert.AreEqual(2, notebook.ExecutedCellIds.Count);
    }
}
