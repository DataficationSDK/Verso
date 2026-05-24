using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console.Testing;
using Verso.Abstractions;
using Verso.Cli.Repl;
using Verso.Cli.Repl.Meta;
using Verso.Cli.Repl.Meta.Commands;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Repl.Settings;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class SaveMetaTests
{
    [TestMethod]
    public async Task Save_NoArg_PreserveFormatTrue_KeepsIpynbExtension()
    {
        using var tmp = new TempIpynb();
        var session = await BuildSessionAsync(tmp.Path, preserveFormat: true);
        var context = BuildContext(session);
        var save = new SaveMeta();

        await save.ExecuteAsync("", context, CancellationToken.None);

        Assert.IsTrue(File.Exists(tmp.Path), "Original .ipynb should still exist.");
        Assert.AreEqual(tmp.Path, session.NotebookPath);
        var written = await File.ReadAllTextAsync(tmp.Path);
        Assert.IsTrue(written.Contains("\"nbformat\": 4"), "File should be re-serialized as nbformat 4.");
    }

    [TestMethod]
    public async Task Save_NoArg_PreserveFormatFalse_RewritesToVersoSibling()
    {
        using var tmp = new TempIpynb();
        var session = await BuildSessionAsync(tmp.Path, preserveFormat: false);
        var context = BuildContext(session);
        var save = new SaveMeta();

        await save.ExecuteAsync("", context, CancellationToken.None);

        var expectedVerso = Path.ChangeExtension(tmp.Path, ".verso");
        Assert.IsTrue(File.Exists(expectedVerso),
            $"Sibling .verso file expected at {expectedVerso}.");
        Assert.AreEqual(expectedVerso, session.NotebookPath);
        var written = await File.ReadAllTextAsync(expectedVerso);
        Assert.IsTrue(written.Contains("\"verso\""), "Sibling file should be Verso-native JSON.");

        File.Delete(expectedVerso);
    }

    [TestMethod]
    public async Task Save_ExplicitIpynbPath_AlwaysHonored_RegardlessOfFlag()
    {
        using var tmp = new TempIpynb();
        var session = await BuildSessionAsync(tmp.Path, preserveFormat: false);
        var context = BuildContext(session);
        var save = new SaveMeta();

        var explicitTarget = Path.Combine(Path.GetDirectoryName(tmp.Path)!,
            $"explicit-{Guid.NewGuid():N}.ipynb");
        try
        {
            await save.ExecuteAsync(explicitTarget, context, CancellationToken.None);

            Assert.IsTrue(File.Exists(explicitTarget),
                "Explicit .ipynb target must be honored even with preserve-format off.");
        }
        finally
        {
            if (File.Exists(explicitTarget)) File.Delete(explicitTarget);
        }
    }

    // --- helpers ---

    private static async Task<ReplSession> BuildSessionAsync(string notebookPath, bool preserveFormat)
    {
        var host = new ExtensionHost();
        host.ConsentHandler = (_, _) => Task.FromResult(true);
        await host.LoadBuiltInExtensionsAsync();

        var notebook = new NotebookModel
        {
            Cells = { new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" } }
        };
        var scaffold = new Scaffold(notebook, host, notebookPath);

        return new ReplSession(notebook, scaffold, host, notebookPath)
        {
            PreserveFormat = preserveFormat
        };
    }

    private static MetaContext BuildContext(ReplSession session)
    {
        var console = new TestConsole();
        var renderer = new TerminalRenderer(console, useColor: false);
        renderer.BindSettings(new ReplSettings());
        var registry = new MetaCommandRegistry();
        return new MetaContext(session, console, renderer, registry, useColor: false);
    }

    private sealed class TempIpynb : IDisposable
    {
        public string Path { get; }

        public TempIpynb()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"verso-save-test-{Guid.NewGuid():N}.ipynb");
            File.WriteAllText(Path, "{ \"nbformat\": 4, \"nbformat_minor\": 5, \"metadata\": {}, \"cells\": [] }");
        }

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
