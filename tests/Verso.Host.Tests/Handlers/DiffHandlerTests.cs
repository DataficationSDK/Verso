using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Serializers;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class DiffHandlerTests
{
    private static async Task<(HostSession Session, string NotebookId)> CreateOpenSession(string content = "")
    {
        var session = new HostSession(_ => { });
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = content },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId);
    }

    private static JsonElement ToParams<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonRpcMessage.SerializerOptions);

    private static async Task<string> SerializeNotebook(NotebookModel notebook)
        => await new VersoSerializer().SerializeAsync(notebook);

    [TestMethod]
    public async Task HandleDiffAsync_VersoBaseline_IdMatchedCellsAlignCorrectly()
    {
        var cellId = Guid.NewGuid();
        var baselineContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Id = cellId, Source = "var x = 1;", Language = "csharp" } },
        });
        var currentContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Id = cellId, Source = "var x = 2;", Language = "csharp" } },
        });
        var (session, notebookId) = await CreateOpenSession(currentContent);
        var ns = session.GetSession(notebookId);

        var result = await DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
        {
            BaselineContent = baselineContent,
            BaselineFilePath = "notebook.verso",
            BaselineLabel = "Git: HEAD",
        }));

        Assert.AreEqual("Git: HEAD", result.BaselineLabel);
        Assert.AreEqual(1, result.Summary.Modified);
        var entry = result.Cells.Single(e => e.Kind == CellDiffKind.Modified);
        Assert.IsFalse(entry.MatchedByContent);
        Assert.AreEqual("var x = 1;", entry.BaselineCell!.Source);
        Assert.AreEqual("var x = 2;", entry.CurrentCell!.Source);
    }

    [TestMethod]
    public async Task HandleDiffAsync_IpynbBaseline_NoPathHint_SniffsFormatAndAlignsByContent()
    {
        const string ipynb = "{\n" +
            "  \"nbformat\": 4,\n" +
            "  \"nbformat_minor\": 5,\n" +
            "  \"metadata\": {},\n" +
            "  \"cells\": [\n" +
            "    { \"cell_type\": \"code\", \"metadata\": {}, \"source\": [\"print('hello')\"], \"outputs\": [] }\n" +
            "  ]\n" +
            "}";
        var currentContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Source = "print('hello')", Language = "python" } },
        });
        var (session, notebookId) = await CreateOpenSession(currentContent);
        var ns = session.GetSession(notebookId);

        var result = await DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
        {
            BaselineContent = ipynb,
            BaselineLabel = "baseline.ipynb",
        }));

        var pair = result.Cells.Single(e => e.BaselineCell is not null && e.CurrentCell is not null);
        Assert.IsTrue(pair.MatchedByContent, "ipynb baselines carry no Verso cell ids; expected content alignment.");
        Assert.AreEqual("print('hello')", pair.BaselineCell!.Source);
    }

    [TestMethod]
    public async Task HandleDiffAsync_DibBaselineFilePath_SelectsDibSerializer()
    {
        const string dib = "#!csharp\n\nConsole.WriteLine(42);\n";
        var currentContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Source = "Console.WriteLine(42);", Language = "csharp" } },
        });
        var (session, notebookId) = await CreateOpenSession(currentContent);
        var ns = session.GetSession(notebookId);

        var result = await DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
        {
            BaselineContent = dib,
            BaselineFilePath = "baseline.dib",
            BaselineLabel = "baseline.dib",
        }));

        var pair = result.Cells.Single(e => e.BaselineCell is not null && e.CurrentCell is not null);
        Assert.AreEqual("Console.WriteLine(42);", pair.BaselineCell!.Source.Trim());
        Assert.IsTrue(pair.MatchedByContent);
    }

    [TestMethod]
    public async Task HandleDiffAsync_LiveSessionUnsavedEdits_ReflectedInCurrentSide()
    {
        var cellId = Guid.NewGuid();
        var original = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Id = cellId, Source = "before", Language = "csharp" } },
        });
        var (session, notebookId) = await CreateOpenSession(original);
        var ns = session.GetSession(notebookId);

        CellHandler.HandleUpdateSource(ns, ToParams(new CellUpdateSourceParams
        {
            CellId = cellId.ToString(),
            Source = "after (unsaved)",
        }));

        var result = await DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
        {
            BaselineContent = original,
            BaselineLabel = "Last Saved",
        }));

        var entry = result.Cells.Single(e => e.Kind == CellDiffKind.Modified);
        Assert.AreEqual("after (unsaved)", entry.CurrentCell!.Source,
            "The diff must see the live in-memory source, not the content notebook/open received.");
    }

    [TestMethod]
    public async Task HandleDiffAsync_MalformedBaseline_ThrowsFriendlyError()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
            {
                BaselineContent = "{ not valid json",
                BaselineLabel = "broken",
            })));
        StringAssert.Contains(ex.Message, "Could not parse");
    }

    [TestMethod]
    public async Task HandleDiffAsync_EmptyBaseline_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => DiffHandler.HandleDiffAsync(ns, ToParams(new NotebookDiffParams
            {
                BaselineContent = "",
            })));
    }

    [TestMethod]
    public async Task Dispatch_UnknownNotebookId_ReturnsInvalidParamsError()
    {
        var (session, _) = await CreateOpenSession();

        var response = await session.DispatchAsync(1, MethodNames.NotebookDiff, ToParams(new
        {
            notebookId = "nb-does-not-exist",
            baselineContent = "{}",
            baselineLabel = "x",
        }));

        StringAssert.Contains(response, "\"error\"");
    }

    [TestMethod]
    public async Task Dispatch_NotebookDiff_SerializesKindAsString()
    {
        var cellId = Guid.NewGuid();
        var baselineContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Id = cellId, Source = "before", Language = "csharp" } },
        });
        var currentContent = await SerializeNotebook(new NotebookModel
        {
            Cells = { new CellModel { Id = cellId, Source = "after", Language = "csharp" } },
        });
        var (session, notebookId) = await CreateOpenSession(currentContent);

        var response = await session.DispatchAsync(7, MethodNames.NotebookDiff, ToParams(new
        {
            notebookId,
            baselineContent,
            baselineLabel = "Git: HEAD",
        }));

        StringAssert.Contains(response, "\"kind\":\"Modified\"",
            "CellDiffKind must serialize as a string on the wire so untyped clients stay readable.");
        StringAssert.Contains(response, "\"summary\"");
    }
}
