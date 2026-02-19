using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Ado.Import;

/// <summary>
/// Post-processor that converts Polyglot Notebooks SQL patterns in imported Jupyter notebooks
/// into Verso SQL cell types and magic commands.
/// </summary>
[VersoExtension]
public sealed class JupyterSqlImportHook : INotebookPostProcessor
{
    private static readonly Regex ConnectPattern = new(
        @"^#!connect\s+(mssql|postgresql|mysql|sqlite)\s+--kernel-name\s+(\S+)\s+""([^""]+)""(.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SqlMagicPattern = new(
        @"^#!sql\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex KernelNameMagicPattern = new(
        @"^#!(\w+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, string> ProviderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mssql"] = "Microsoft.Data.SqlClient",
        ["postgresql"] = "Npgsql",
        ["mysql"] = "MySql.Data.MySqlClient",
        ["sqlite"] = "Microsoft.Data.Sqlite",
    };

    private static readonly Dictionary<string, string> NuGetPackageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Data.SqlClient"] = "Microsoft.Data.SqlClient",
        ["Npgsql"] = "Npgsql",
        ["MySql.Data.MySqlClient"] = "MySql.Data",
        ["Microsoft.Data.Sqlite"] = "Microsoft.Data.Sqlite",
    };

    // --- IExtension ---
    public string ExtensionId => "verso.ado.postprocessor.jupyter-sql";
    string IExtension.Name => "Jupyter SQL Import Hook";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Converts Polyglot Notebooks SQL patterns to Verso SQL cells on Jupyter import.";

    // --- INotebookPostProcessor ---
    public int Priority => 100;

    public bool CanProcess(string? filePath, string formatId)
    {
        if (string.Equals(formatId, "jupyter", StringComparison.OrdinalIgnoreCase))
            return true;

        if (filePath is not null && filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath)
    {
        bool anyTransformed = false;
        var newCells = new List<CellModel>();
        var insertedNuGetPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: collect already-present #r nuget references
        foreach (var cell in notebook.Cells)
        {
            if (cell.Type == "code" && cell.Source.Contains("#r \"nuget:", StringComparison.OrdinalIgnoreCase))
            {
                // Track existing packages
                var lines = cell.Source.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#r \"nuget:", StringComparison.OrdinalIgnoreCase))
                    {
                        insertedNuGetPackages.Add(trimmed);
                    }
                }
            }
        }

        // Track known kernel names from #!connect commands
        var knownKernelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Second pass: transform cells
        foreach (var cell in notebook.Cells)
        {
            if (cell.Type != "code")
            {
                newCells.Add(cell);
                continue;
            }

            var source = cell.Source.Trim();

            // Check for #!connect pattern
            var connectMatch = ConnectPattern.Match(source);
            if (connectMatch.Success)
            {
                var dbType = connectMatch.Groups[1].Value;
                var kernelName = connectMatch.Groups[2].Value;
                var connStr = connectMatch.Groups[3].Value;
                var remaining = connectMatch.Groups[4].Value.Trim();

                knownKernelNames.Add(kernelName);

                if (ProviderMap.TryGetValue(dbType, out var provider))
                {
                    // Insert #r nuget cell if needed
                    if (NuGetPackageMap.TryGetValue(provider, out var nugetPackage))
                    {
                        var nugetRef = $"#r \"nuget: {nugetPackage}\"";
                        if (!insertedNuGetPackages.Contains(nugetRef))
                        {
                            insertedNuGetPackages.Add(nugetRef);
                            newCells.Add(new CellModel
                            {
                                Type = "code",
                                Language = "csharp",
                                Source = nugetRef
                            });
                        }
                    }

                    // Create sql-connect cell
                    cell.Source = $"#!sql-connect --name {kernelName} --connection-string \"{connStr}\" --provider {provider}";
                    newCells.Add(cell);

                    // Check for --create-dbcontext flag
                    if (remaining.Contains("--create-dbcontext", StringComparison.OrdinalIgnoreCase))
                    {
                        newCells.Add(new CellModel
                        {
                            Type = "code",
                            Language = "csharp",
                            Source = $"#!sql-scaffold --connection {kernelName}"
                        });
                    }

                    anyTransformed = true;
                    continue;
                }
            }

            // Check for #!sql cells
            var lines2 = source.Split('\n');
            if (lines2.Length > 0 && lines2[0].Trim().Equals("#!sql", StringComparison.OrdinalIgnoreCase))
            {
                cell.Type = "code";
                cell.Language = "sql";
                cell.Source = string.Join('\n', lines2.Skip(1));
                newCells.Add(cell);
                anyTransformed = true;
                continue;
            }

            // Check for #!<kernelName> cells that map to known SQL connections
            if (lines2.Length > 0)
            {
                var firstLine = lines2[0].Trim();
                var kernelMatch = KernelNameMagicPattern.Match(firstLine);
                if (kernelMatch.Success)
                {
                    var kernelName = kernelMatch.Groups[1].Value;
                    if (knownKernelNames.Contains(kernelName))
                    {
                        cell.Type = "code";
                        cell.Language = "sql";
                        var sqlBody = string.Join('\n', lines2.Skip(1));
                        cell.Source = $"--connection {kernelName}\n{sqlBody}";
                        newCells.Add(cell);
                        anyTransformed = true;
                        continue;
                    }
                }
            }

            // No transformation needed
            newCells.Add(cell);
        }

        if (anyTransformed)
        {
            notebook.Cells = newCells;
            if (!notebook.RequiredExtensions.Contains("verso.ado"))
            {
                notebook.RequiredExtensions.Add("verso.ado");
            }
        }

        return Task.FromResult(notebook);
    }

    public Task<NotebookModel> PreSerializeAsync(NotebookModel notebook, string? filePath)
    {
        // No-op for this extension
        return Task.FromResult(notebook);
    }
}
