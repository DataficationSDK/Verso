using Verso.Abstractions;
using Verso.Extensions.Utilities;

namespace Verso.Serializers.Migrations;

/// <summary>
/// Upgrades a notebook from format 1.0 to 1.1. The only pure-data change is the cell-metadata key
/// rename handled by <see cref="LegacyMetadataMigrator"/>. The 1.1 active-layout shape change
/// (bare id to qualified reference) is resolved later against the loaded extension set during
/// notebook wire-up and is intentionally not performed here.
/// </summary>
internal sealed class V1_0ToV1_1Migration : INotebookMigration
{
    public Version From => new(1, 0);

    public Version To => new(1, 1);

    public void Apply(NotebookModel notebook, NotebookMigrationLog log)
    {
        LegacyMetadataMigrator.Migrate(notebook);
        log.AddStep("Renamed legacy cell visibility metadata keys.");
    }
}
