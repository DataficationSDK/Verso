using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Resources;

namespace Verso.Ado.Import;

/// <summary>
/// Post-processor that converts Polyglot Notebooks SQL patterns in imported Jupyter notebooks
/// into Verso SQL cell types and magic commands.
/// </summary>
[VersoExtension]
public sealed class JupyterSqlImportHook : INotebookPostProcessor
{
    // Metadata key set by the Jupyter serializer carrying a cell's Polyglot kernel name
    // (e.g. "sql-PrimaryServer"). Consumed and removed here so it never reaches the output.
    private const string PolyglotKernelNameKey = "polyglotKernelName";

    private static readonly Regex ConnectLinePattern = new(
        @"^#!connect\s+(mssql|postgresql|mysql|sqlite)\b\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches an `#r "nuget: Package"` / `#r nuget:Package` reference and captures the package id.
    private static readonly Regex NuGetReferencePattern = new(
        @"#r\s+""?nuget:\s*([^"",\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    // Polyglot Notebooks ships its SQL kernels as Microsoft.DotNet.Interactive.* packages.
    // Those reference the Polyglot kernel, not the database, so on import we swap them for the
    // ADO.NET provider package that the Verso SQL kernel actually needs.
    private static readonly Dictionary<string, string> InteractivePackageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.DotNet.Interactive.SqlServer"] = "Microsoft.Data.SqlClient",
        ["Microsoft.DotNet.Interactive.PostgreSQL"] = "Npgsql",
        ["Microsoft.DotNet.Interactive.SQLite"] = "Microsoft.Data.Sqlite",
    };

    // --- IExtension ---
    public string ExtensionId => "verso.ado.postprocessor.jupyter-sql";
    string IExtension.Name => Strings.Import_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Import_Description;

    // --- INotebookPostProcessor ---
    public int Priority => 100;

    public bool CanProcess(string? filePath, string formatId)
    {
        if (string.Equals(formatId, "jupyter", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(formatId, "dib", StringComparison.OrdinalIgnoreCase))
            return true;

        if (filePath is not null &&
            (filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase) ||
             filePath.EndsWith(".dib", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath)
    {
        bool anyTransformed = false;
        var newCells = new List<CellModel>();

        // Package ids already present in the notebook, tracked so the #!connect handler does
        // not insert a duplicate provider reference. Comparison is by package id, independent
        // of the `#r "nuget: X"` vs `#r nuget:X` spelling.
        var presentPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: swap Polyglot interactive SQL packages for ADO provider packages and
        // record every #r nuget package id for deduplication.
        foreach (var cell in notebook.Cells)
        {
            if (cell.Type != "code" || !cell.Source.Contains("nuget:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (RewriteInteractiveNuGetReferences(cell, presentPackages))
                anyTransformed = true;
        }

        // Track known connection names from #!connect commands.
        var knownKernelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Second pass: transform cells.
        foreach (var cell in notebook.Cells)
        {
            if (cell.Type != "code")
            {
                newCells.Add(cell);
                continue;
            }

            // Polyglot cells tagged with an "sql-<connection>" kernel via metadata. These carry
            // plain SQL (no leading directive), so the connection comes from the kernel name.
            if (cell.Metadata.TryGetValue(PolyglotKernelNameKey, out var rawKernel) &&
                rawKernel is string kernelName)
            {
                cell.Metadata.Remove(PolyglotKernelNameKey);
                ConvertToSqlCell(cell, ConnectionFromKernelName(kernelName));
                if (cell.Language == "sql")
                    knownKernelNames.Add(ConnectionFromKernelName(kernelName) ?? kernelName);
                newCells.Add(cell);
                anyTransformed = true;
                continue;
            }

            var source = cell.Source.Trim();

            // #!connect lines (one or more) — translate each to #!sql-connect, keeping them in
            // the same cell, and emit provider nuget / scaffold cells as needed.
            if (ContainsConnectDirective(source))
            {
                var emitted = ProcessConnectCell(cell, knownKernelNames, presentPackages);
                newCells.AddRange(emitted);
                anyTransformed = true;
                continue;
            }

            var lines = source.Split('\n');

            // #!sql cells.
            if (lines.Length > 0 && lines[0].Trim().Equals("#!sql", StringComparison.OrdinalIgnoreCase))
            {
                cell.Source = string.Join('\n', lines.Skip(1));
                ConvertToSqlCell(cell, connectionName: null);
                newCells.Add(cell);
                anyTransformed = true;
                continue;
            }

            // #!<kernelName> cells that map to known SQL connections.
            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();
                var kernelMatch = KernelNameMagicPattern.Match(firstLine);
                if (kernelMatch.Success && knownKernelNames.Contains(kernelMatch.Groups[1].Value))
                {
                    cell.Source = string.Join('\n', lines.Skip(1));
                    ConvertToSqlCell(cell, kernelMatch.Groups[1].Value);
                    newCells.Add(cell);
                    anyTransformed = true;
                    continue;
                }
            }

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

    // --- Helpers ---

    /// <summary>
    /// Turns a cell into a Verso SQL cell (type and language "sql"), prepending a
    /// <c>--connection &lt;name&gt;</c> directive when a connection is known and the source does
    /// not already carry one.
    /// </summary>
    private static void ConvertToSqlCell(CellModel cell, string? connectionName)
    {
        cell.Type = "sql";
        cell.Language = "sql";

        if (!string.IsNullOrEmpty(connectionName) &&
            !cell.Source.TrimStart().StartsWith("--connection", StringComparison.OrdinalIgnoreCase))
        {
            cell.Source = string.IsNullOrEmpty(cell.Source)
                ? $"--connection {connectionName}"
                : $"--connection {connectionName}\n{cell.Source}";
        }
    }

    /// <summary>
    /// Extracts the connection name from a Polyglot SQL kernel name ("sql-PrimaryServer" -&gt;
    /// "PrimaryServer"). Returns the kernel name unchanged when it carries no "sql-" prefix.
    /// </summary>
    private static string? ConnectionFromKernelName(string kernelName)
    {
        if (kernelName.StartsWith("sql-", StringComparison.OrdinalIgnoreCase))
        {
            var name = kernelName.Substring(4);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        return string.IsNullOrWhiteSpace(kernelName) ? null : kernelName;
    }

    private static bool ContainsConnectDirective(string source)
    {
        foreach (var line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("#!connect", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Translates every <c>#!connect</c> line in a cell to a <c>#!sql-connect</c> line (preserved
    /// in place), returning the connect cell preceded by any required provider nuget cells and
    /// followed by any scaffold cells.
    /// </summary>
    private List<CellModel> ProcessConnectCell(
        CellModel cell,
        HashSet<string> knownKernelNames,
        HashSet<string> presentPackages)
    {
        var nugetCells = new List<CellModel>();
        var scaffoldCells = new List<CellModel>();
        var rewrittenLines = new List<string>();

        foreach (var rawLine in cell.Source.Split('\n'))
        {
            var match = ConnectLinePattern.Match(rawLine.Trim());
            if (!match.Success)
            {
                rewrittenLines.Add(rawLine);
                continue;
            }

            var dbType = match.Groups[1].Value;
            var parsed = ParseConnectArguments(match.Groups[2].Value);

            if (parsed.KernelName is null || parsed.ConnectionString is null ||
                !ProviderMap.TryGetValue(dbType, out var provider))
            {
                // Leave anything we cannot confidently parse untouched.
                rewrittenLines.Add(rawLine);
                continue;
            }

            knownKernelNames.Add(parsed.KernelName);

            rewrittenLines.Add(
                $"#!sql-connect --name {parsed.KernelName} " +
                $"--connection-string \"{parsed.ConnectionString}\" --provider {provider}");

            if (NuGetPackageMap.TryGetValue(provider, out var nugetPackage) &&
                presentPackages.Add(nugetPackage))
            {
                nugetCells.Add(new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = $"#r \"nuget: {nugetPackage}\""
                });
            }

            if (parsed.CreateDbContext)
            {
                scaffoldCells.Add(new CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = $"#!sql-scaffold --connection {parsed.KernelName}"
                });
            }
        }

        cell.Source = string.Join('\n', rewrittenLines);

        var result = new List<CellModel>(nugetCells.Count + 1 + scaffoldCells.Count);
        result.AddRange(nugetCells);
        result.Add(cell);
        result.AddRange(scaffoldCells);
        return result;
    }

    /// <summary>
    /// Parses the argument portion of a <c>#!connect</c> line. Handles both the explicit
    /// <c>--kernel-name "X" --connection-string "Y"</c> form and the positional
    /// <c>--kernel-name X "Y"</c> form, with quoted or bare values.
    /// </summary>
    private static (string? KernelName, string? ConnectionString, bool CreateDbContext) ParseConnectArguments(string args)
    {
        string? kernelName = null;
        string? connectionString = null;
        bool createDbContext = false;

        var tokens = ArgumentParser.Tokenize(args);
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.ToLowerInvariant())
            {
                case "--kernel-name":
                case "--name":
                    if (i + 1 < tokens.Count) kernelName = tokens[++i];
                    break;
                case "--connection-string":
                    if (i + 1 < tokens.Count) connectionString = tokens[++i];
                    break;
                case "--create-dbcontext":
                    createDbContext = true;
                    break;
                default:
                    // A positional (non-flag) value is the connection string in the older form.
                    if (!token.StartsWith("--") && connectionString is null)
                        connectionString = token;
                    break;
            }
        }

        return (kernelName, connectionString, createDbContext);
    }

    /// <summary>
    /// Rewrites any Polyglot interactive SQL nuget reference in a cell to its ADO provider
    /// package and records every referenced package id in <paramref name="presentPackages"/>.
    /// Returns true when the cell source changed.
    /// </summary>
    private static bool RewriteInteractiveNuGetReferences(CellModel cell, HashSet<string> presentPackages)
    {
        bool changed = false;

        var rewritten = NuGetReferencePattern.Replace(cell.Source, m =>
        {
            var packageId = m.Groups[1].Value;
            if (InteractivePackageMap.TryGetValue(packageId, out var replacement))
            {
                presentPackages.Add(replacement);
                changed = true;
                return $"#r \"nuget:{replacement}\"";
            }

            presentPackages.Add(packageId);
            return m.Value;
        });

        if (changed)
            cell.Source = rewritten;

        return changed;
    }
}
