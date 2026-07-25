using System.Text.Json.Nodes;
using Verso.Python.PackageManagement;

namespace Verso.Python.Host;

/// <summary>
/// Turns an import-scan reply into the model the install path consumes. A reply that is missing
/// or malformed yields nothing, which skips the install and lets the cell run: a scan is an
/// optimization over failing at the import, never a precondition for executing.
/// </summary>
internal static class HostPackages
{
    public static ImportScan MapScan(JsonObject? reply)
    {
        if (reply is null)
            return ImportScan.Empty;

        var missing = new List<ScannedImport>();
        if (reply[HostProtocol.MissingField] is JsonArray items)
        {
            foreach (var node in items)
            {
                if (node is not JsonObject item)
                    continue;

                var module = HostProtocol.TryGetString(item, HostProtocol.ModuleField);
                if (string.IsNullOrWhiteSpace(module))
                    continue;

                var optional = item[HostProtocol.OptionalField] is JsonValue value
                    && value.TryGetValue<bool>(out var flag)
                    && flag;

                missing.Add(new ScannedImport(module!, optional));
            }
        }

        var unsatisfied = new List<string>();
        if (reply[HostProtocol.UnsatisfiedField] is JsonArray requirements)
        {
            foreach (var node in requirements)
            {
                if (node is not JsonValue value || !value.TryGetValue<string>(out var requirement))
                    continue;
                if (string.IsNullOrWhiteSpace(requirement))
                    continue;

                unsatisfied.Add(requirement.Trim());
            }
        }

        if (missing.Count == 0 && unsatisfied.Count == 0)
            return ImportScan.Empty;

        return new ImportScan(missing, unsatisfied);
    }
}
