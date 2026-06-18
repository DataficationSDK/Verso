using Verso.Abstractions;

namespace Verso.Serializers.Migrations;

/// <summary>
/// Upgrades a parsed <see cref="NotebookModel"/> from its declared format version to
/// <see cref="NotebookFormatVersion.Current"/> by walking a chain of registered
/// <see cref="INotebookMigration"/> steps. Migrations are pure-data transforms; host-dependent
/// resolution is performed separately during notebook wire-up.
/// </summary>
public sealed class NotebookMigrationPipeline
{
    /// <summary>The built-in pipeline, pre-registered with the shipped migrations.</summary>
    public static NotebookMigrationPipeline Default { get; } = new(new INotebookMigration[]
    {
        new V1_0ToV1_1Migration(),
    });

    private readonly IReadOnlyList<INotebookMigration> _migrations;

    /// <summary>
    /// Creates a pipeline over the given migrations. The order they are supplied in does not
    /// matter; the chain is walked by matching each step's <see cref="INotebookMigration.From"/>.
    /// </summary>
    public NotebookMigrationPipeline(IEnumerable<INotebookMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = migrations.ToList();
    }

    /// <summary>
    /// Migrates <paramref name="notebook"/> in place toward the current format version and stamps
    /// the version it reached. Returns a log of applied steps and warnings. A document declaring a
    /// newer version than this build supports is left untouched and a warning is recorded.
    /// </summary>
    public NotebookMigrationLog Run(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        var log = new NotebookMigrationLog();

        if (!NotebookFormatVersion.TryParse(notebook.FormatVersion, out var from))
        {
            log.AddWarning(
                $"Unrecognized notebook format version '{notebook.FormatVersion}'; treating it as the current format and skipping migrations.");
            notebook.FormatVersion = NotebookFormatVersion.Current;
            return log;
        }

        NotebookFormatVersion.TryParse(NotebookFormatVersion.Current, out var current);

        if (from > current)
        {
            log.AddWarning(
                $"Notebook declares format version {from}, but this build supports {current}. " +
                $"Opening best-effort; saving will rewrite it as {current} and may drop newer fields.");
            return log;
        }

        // Walk contiguous migrations from the declared version up to current.
        var cursor = from;
        while (cursor < current)
        {
            var next = _migrations.FirstOrDefault(m => m.From == cursor);
            if (next is null)
            {
                log.AddWarning($"No migration registered from format {cursor} to {current}; stopping at {cursor}.");
                break;
            }

            next.Apply(notebook, log);
            cursor = next.To;
        }

        notebook.FormatVersion = cursor.ToString();
        return log;
    }
}
