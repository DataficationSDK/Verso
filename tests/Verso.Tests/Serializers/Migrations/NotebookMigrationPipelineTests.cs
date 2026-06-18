using Verso.Abstractions;
using Verso.Serializers.Migrations;

namespace Verso.Tests.Serializers.Migrations;

[TestClass]
public sealed class NotebookMigrationPipelineTests
{
    /// <summary>A test migration that marks the model so application is observable.</summary>
    private sealed class FakeMigration : INotebookMigration
    {
        public FakeMigration(Version from, Version to)
        {
            From = from;
            To = to;
        }

        public Version From { get; }
        public Version To { get; }

        public void Apply(NotebookModel notebook, NotebookMigrationLog log)
        {
            notebook.DefaultKernelId = "migrated";
            log.AddStep($"{From} -> {To}");
        }
    }

    private static Version CurrentVersion()
    {
        NotebookFormatVersion.TryParse(NotebookFormatVersion.Current, out var v);
        return v;
    }

    [TestMethod]
    public void Run_OlderVersion_AppliesChainAndStampsCurrent()
    {
        var pipeline = new NotebookMigrationPipeline(new[] { new FakeMigration(new Version(1, 0), CurrentVersion()) });
        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Initial };

        var log = pipeline.Run(notebook);

        Assert.AreEqual("migrated", notebook.DefaultKernelId);
        Assert.AreEqual(NotebookFormatVersion.Current, notebook.FormatVersion);
        Assert.IsFalse(log.HasWarnings);
        Assert.AreEqual(1, log.Steps.Count);
    }

    [TestMethod]
    public void Run_CurrentVersion_IsNoOp()
    {
        var pipeline = new NotebookMigrationPipeline(new[] { new FakeMigration(new Version(1, 0), CurrentVersion()) });
        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Current };

        var log = pipeline.Run(notebook);

        Assert.IsNull(notebook.DefaultKernelId);
        Assert.AreEqual(NotebookFormatVersion.Current, notebook.FormatVersion);
        Assert.IsFalse(log.HasWarnings);
        Assert.AreEqual(0, log.Steps.Count);
    }

    [TestMethod]
    public void Run_NewerVersion_WarnsAndLeavesModelUnchanged()
    {
        var pipeline = new NotebookMigrationPipeline(new[] { new FakeMigration(new Version(1, 0), CurrentVersion()) });
        var notebook = new NotebookModel { FormatVersion = "99.0" };

        var log = pipeline.Run(notebook);

        Assert.IsNull(notebook.DefaultKernelId);    // best-effort: migration not applied
        Assert.AreEqual("99.0", notebook.FormatVersion);
        Assert.IsTrue(log.HasWarnings);
    }

    [TestMethod]
    public void Run_UnparseableVersion_WarnsAndStampsCurrent()
    {
        var pipeline = new NotebookMigrationPipeline(new[] { new FakeMigration(new Version(1, 0), CurrentVersion()) });
        var notebook = new NotebookModel { FormatVersion = "not-a-version" };

        var log = pipeline.Run(notebook);

        Assert.IsNull(notebook.DefaultKernelId);
        Assert.AreEqual(NotebookFormatVersion.Current, notebook.FormatVersion);
        Assert.IsTrue(log.HasWarnings);
    }

    [TestMethod]
    public void Run_MissingMigrationInChain_WarnsAndStopsAtReachedVersion()
    {
        // No migration is registered starting from the initial version.
        var pipeline = new NotebookMigrationPipeline(new[] { new FakeMigration(new Version(5, 0), new Version(6, 0)) });
        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Initial };

        var log = pipeline.Run(notebook);

        Assert.IsNull(notebook.DefaultKernelId);
        Assert.AreEqual(NotebookFormatVersion.Initial, notebook.FormatVersion);
        Assert.IsTrue(log.HasWarnings);
    }

    [TestMethod]
    public void Default_MigratesLegacyCellMetadataKey()
    {
        var notebook = new NotebookModel { FormatVersion = NotebookFormatVersion.Initial };
        notebook.Cells.Add(new CellModel
        {
            Source = "x",
            Metadata = new Dictionary<string, object> { [CellLayoutVisibilityMetadata.LegacyMetadataKey] = "hidden" }
        });

        NotebookMigrationPipeline.Default.Run(notebook);

        var cell = notebook.Cells.Single();
        Assert.IsFalse(cell.Metadata.ContainsKey(CellLayoutVisibilityMetadata.LegacyMetadataKey));
        Assert.AreEqual("hidden", cell.Metadata[CellLayoutVisibilityMetadata.MetadataKey]);
        Assert.AreEqual(NotebookFormatVersion.Current, notebook.FormatVersion);
    }
}
