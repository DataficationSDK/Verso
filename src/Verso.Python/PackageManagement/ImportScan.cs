namespace Verso.Python.PackageManagement;

/// <summary>
/// A root module a cell imports that the interpreter could not resolve.
/// </summary>
/// <param name="Module">The root module name.</param>
/// <param name="Optional">
/// Whether every import of it was inside a <c>try</c>. A cell that guards an import has said
/// it can run without the module, so it is never installed on the cell's behalf.
/// </param>
internal sealed record ScannedImport(string Module, bool Optional);

/// <summary>
/// What the interpreter reported it cannot satisfy for a cell: modules the cell imports and
/// declared requirements that are not installed.
/// </summary>
internal sealed record ImportScan(
    IReadOnlyList<ScannedImport> Missing,
    IReadOnlyList<string> Unsatisfied)
{
    public static readonly ImportScan Empty =
        new(Array.Empty<ScannedImport>(), Array.Empty<string>());

    public bool IsEmpty => Missing.Count == 0 && Unsatisfied.Count == 0;
}
