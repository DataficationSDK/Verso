namespace Verso.Abstractions;

/// <summary>
/// A single, host-independent transform that upgrades a <see cref="NotebookModel"/> from one
/// format version to the next. Migrations operate purely on the parsed model and must not depend
/// on the live extension host. Host-dependent resolution (for example, resolving a layout
/// reference against the loaded extension set) belongs in the notebook wire-up stage, not here.
/// </summary>
public interface INotebookMigration
{
    /// <summary>The format version this migration upgrades from.</summary>
    Version From { get; }

    /// <summary>The format version this migration produces.</summary>
    Version To { get; }

    /// <summary>
    /// Applies the transform to <paramref name="notebook"/> in place, recording notable changes to
    /// <paramref name="log"/>.
    /// </summary>
    void Apply(NotebookModel notebook, NotebookMigrationLog log);
}
