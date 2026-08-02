using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Kernel;
using Verso.Ado.Scaffold;
using Verso.Ado.Localization;
using Verso.Ado.Resources;

namespace Verso.Ado.MagicCommands;

/// <summary>
/// <c>#!sql-scaffold --connection db [--tables "Orders,Products"] [--schema dbo]</c>
/// — generates EF Core DbContext and entity classes from a live database connection.
/// </summary>
[VersoExtension]
public sealed class SqlScaffoldMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.ado.magic.sql-scaffold";
    string IExtension.Name => Strings.Magic_Scaffold_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => Strings.Magic_Scaffold_Description;

    // --- IMagicCommand ---
    public string Name => "sql-scaffold";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("connection", Strings.Magic_Scaffold_Param_Connection, typeof(string), IsRequired: true),
        new ParameterDefinition("tables", Strings.Magic_Scaffold_Param_Tables, typeof(string)),
        new ParameterDefinition("schema", Strings.Magic_Scaffold_Param_Schema, typeof(string)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var args = ArgumentParser.Parse(arguments);

        // 1. Validate --connection parameter
        if (!args.TryGetValue("connection", out var connectionName) || string.IsNullOrWhiteSpace(connectionName))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                CellText.Error(Strings.Magic_Scaffold_ConnectionRequired),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // 2. Resolve connection
        var connInfo = ConnectionResolver.Resolve(connectionName, context.Variables);
        if (connInfo is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                CellText.Error(string.Format(Strings.Magic_Scaffold_NotFound, connectionName)),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // 3. Validate connection is open
        if (connInfo.Connection is null || connInfo.Connection.State != System.Data.ConnectionState.Open)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain",
                CellText.Error(string.Format(Strings.Magic_Scaffold_ConnectionClosed, connectionName)),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // 4. Get schema from cache
        var schema = await SchemaCache.Instance.GetOrRefreshAsync(
            connectionName, connInfo.Connection, context.CancellationToken).ConfigureAwait(false);

        // 6. Filter tables
        var tables = schema.Tables.Where(t => t.TableType == "TABLE").ToList();

        args.TryGetValue("schema", out var schemaFilter);
        if (!string.IsNullOrWhiteSpace(schemaFilter))
        {
            tables = tables.Where(t =>
                t.Schema?.Equals(schemaFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        args.TryGetValue("tables", out var tablesFilter);
        if (!string.IsNullOrWhiteSpace(tablesFilter))
        {
            var requestedTables = tablesFilter.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            tables = tables.Where(t => requestedTables.Contains(t.Name)).ToList();
        }

        if (tables.Count == 0)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", Strings.Magic_Scaffold_NoTables,
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // 7. Filter columns and FKs to only include selected tables
        var filteredColumns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var filteredFks = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
        var tableNames = tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            if (schema.Columns.TryGetValue(table.Name, out var cols))
                filteredColumns[table.Name] = cols;

            if (schema.ForeignKeys.TryGetValue(table.Name, out var fks))
            {
                // Only include FKs that reference tables in our filtered set
                var validFks = fks.Where(fk => tableNames.Contains(fk.ToTable)).ToList();
                if (validFks.Count > 0)
                    filteredFks[table.Name] = validFks;
            }
        }

        // 8. Generate code
        var scaffolder = new EfCoreScaffolder(
            connectionName,
            connInfo.ConnectionString,
            connInfo.ProviderName,
            tables,
            filteredColumns,
            filteredFks);

        var result = scaffolder.Generate();

        // 9. Store live DbConnection for the generated context to use
        context.Variables.Set($"__verso_scaffold_{connectionName}_connection", connInfo.Connection);

        // 10. Find and execute via C# kernel
        var csharpKernel = context.ExtensionHost.GetKernels()
            .FirstOrDefault(k => k.LanguageId.Equals("csharp", StringComparison.OrdinalIgnoreCase));

        bool compilationSucceeded = true;
        if (csharpKernel is not null)
        {
            await csharpKernel.InitializeAsync().ConfigureAwait(false);
            var execContext = new MagicCommandExecutionContext(context);
            var execOutputs = await csharpKernel.ExecuteAsync(result.GeneratedCode, execContext).ConfigureAwait(false);

            if (execOutputs.Any(o => o.IsError))
            {
                compilationSucceeded = false;

                // Surface the actual compilation errors so they are visible in cell output
                foreach (var errorOutput in execOutputs.Where(o => o.IsError))
                    await context.WriteOutputAsync(errorOutput).ConfigureAwait(false);

                // Check if it looks like a missing EF Core reference
                var errorContent = string.Join("\n", execOutputs.Where(o => o.IsError).Select(o => o.Content));
                if (errorContent.Contains("DbContext") || errorContent.Contains("EntityFrameworkCore"))
                {
                    var hint = EfCorePrerequisiteChecker.GetInstallHint(connInfo.ProviderName);
                    await context.WriteOutputAsync(new CellOutput("text/plain",
                        string.Format(Strings.Magic_Scaffold_EfCoreHint, hint),
                        IsError: true)).ConfigureAwait(false);
                }
            }
        }
        else
        {
            compilationSucceeded = false;
            await context.WriteOutputAsync(new CellOutput("text/plain",
                CellText.Error(Strings.Magic_Scaffold_NoCSharpKernel),
                IsError: true)).ConfigureAwait(false);
        }

        if (!compilationSucceeded)
            return;

        // 11. Output success summary
        var entityList = string.Join(", ", result.EntityNames);
        var varName = char.ToLowerInvariant(result.ContextClassName[0]) + result.ContextClassName.Substring(1);
        varName = varName.Replace("Context", "Context"); // keep as-is

        // Calculate variable name (camelCase of context name)
        var contextVarName = char.ToLowerInvariant(connectionName[0]) + connectionName.Substring(1) + "Context";

        var relSuffix = result.RelationshipCount > 0
            ? string.Format(
                Plural.Of(result.RelationshipCount,
                    Strings.Magic_Scaffold_RelationshipCount_One,
                    Strings.Magic_Scaffold_RelationshipCount_Other),
                result.RelationshipCount)
            : "";

        // The counts are written out on their own and go in as arguments, so the report is
        // one entry a translator can reorder rather than six fragments glued together.
        var entities = string.Format(
            Plural.Of(result.EntityCount,
                Strings.Magic_Scaffold_EntityCount_One, Strings.Magic_Scaffold_EntityCount_Other),
            result.EntityCount);

        var summaryText = string.Format(Strings.Magic_Scaffold_Summary,
            entities, relSuffix, result.ContextClassName, entityList, contextVarName, tables[0].Name);

        // Output the summary as text
        await context.WriteOutputAsync(new CellOutput("text/plain", summaryText)).ConfigureAwait(false);

        // Output the generated code as collapsible HTML for inspection
        var escapedCode = System.Net.WebUtility.HtmlEncode(result.GeneratedCode);
        var detailsHtml =
            "<details>\n" +
            $"<summary>{System.Net.WebUtility.HtmlEncode(Strings.Magic_Scaffold_GeneratedCode)}</summary>\n\n" +
            $"<pre><code>{escapedCode}</code></pre>\n" +
            "</details>";

        await context.WriteOutputAsync(new CellOutput("text/html", detailsHtml)).ConfigureAwait(false);
    }
}
