using System.Text;
using Verso.Ado.Helpers;
using Verso.Ado.Kernel;

namespace Verso.Ado.Scaffold;

/// <summary>
/// Pure code generator: takes schema metadata and produces a C# source string
/// containing EF Core entity classes and a DbContext subclass.
/// </summary>
internal sealed class EfCoreScaffolder
{
    private readonly string _connectionName;
    private readonly string _connectionString;
    private readonly string? _providerName;
    private readonly List<TableInfo> _tables;
    private readonly Dictionary<string, List<ColumnInfo>> _columns;
    private readonly Dictionary<string, List<ForeignKeyInfo>> _foreignKeys;

    internal EfCoreScaffolder(
        string connectionName,
        string connectionString,
        string? providerName,
        List<TableInfo> tables,
        Dictionary<string, List<ColumnInfo>> columns,
        Dictionary<string, List<ForeignKeyInfo>> foreignKeys)
    {
        _connectionName = connectionName;
        _connectionString = connectionString;
        _providerName = providerName;
        _tables = tables;
        _columns = columns;
        _foreignKeys = foreignKeys;
    }

    internal ScaffoldResult Generate()
    {
        var sb = new StringBuilder();
        var entityNames = new List<string>();
        int relationshipCount = 0;

        // Map table name -> entity class name for FK navigation lookups
        var tableToEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in _tables)
        {
            var entityName = NamingConventions.ToEntityClassName(table.Name);
            tableToEntity[table.Name] = entityName;
        }

        // Usings
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();

        // Entity classes
        foreach (var table in _tables)
        {
            var entityName = tableToEntity[table.Name];
            entityNames.Add(entityName);

            if (!_columns.TryGetValue(table.Name, out var columns))
                continue;

            sb.AppendLine($"[Table(\"{table.Name}\")]");
            sb.AppendLine($"public class {entityName}");
            sb.AppendLine("{");

            // Properties from columns
            foreach (var col in columns.OrderBy(c => c.OrdinalPosition))
            {
                var propName = NamingConventions.ToPropertyName(col.Name);
                var clrType = SqlClrTypeMapper.MapSqlType(col.DataType);
                bool isNullable = col.IsNullable;

                // PK attribute: if column name is "Id" or "{EntityName}Id", rely on EF convention
                bool needsKeyAttribute = col.IsPrimaryKey && !IsConventionalPkName(col.Name, entityName);
                if (needsKeyAttribute)
                    sb.AppendLine($"    [Key]");

                if (col.Name != propName.TrimStart('@'))
                    sb.AppendLine($"    [Column(\"{col.Name}\")]");

                var typeName = SqlClrTypeMapper.GetCSharpTypeName(clrType, isNullable);
                string defaultInit = "";

                // Non-nullable string needs = default! to suppress nullable warning
                if (clrType == typeof(string) && !isNullable)
                    defaultInit = " = default!;";

                if (string.IsNullOrEmpty(defaultInit))
                    sb.AppendLine($"    public {typeName} {propName} {{ get; set; }}");
                else
                    sb.AppendLine($"    public {typeName} {propName} {{ get; set; }}{defaultInit}");
            }

            // FK navigation properties (dependent side)
            if (_foreignKeys.TryGetValue(table.Name, out var fks))
            {
                foreach (var fk in fks)
                {
                    if (!tableToEntity.TryGetValue(fk.ToTable, out var referencedEntity))
                        continue;

                    var fkPropName = NamingConventions.ToPropertyName(fk.FromColumn);
                    var navPropName = referencedEntity;

                    // Avoid collision with FK column property name
                    if (navPropName == fkPropName)
                        navPropName = navPropName + "Navigation";

                    sb.AppendLine();
                    sb.AppendLine($"    [ForeignKey(\"{fkPropName}\")]");
                    sb.AppendLine($"    public virtual {referencedEntity}? {navPropName} {{ get; set; }}");
                    relationshipCount++;
                }
            }

            // Collection navigation properties (principal side)
            foreach (var kvp in _foreignKeys)
            {
                foreach (var fk in kvp.Value)
                {
                    if (!fk.ToTable.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!tableToEntity.TryGetValue(fk.FromTable, out var dependentEntity))
                        continue;

                    var collectionName = dependentEntity + "s";
                    // Avoid name clashing with existing properties
                    sb.AppendLine();
                    sb.AppendLine($"    public virtual ICollection<{dependentEntity}> {collectionName} {{ get; set; }} = new List<{dependentEntity}>();");
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        // DbContext class
        var contextName = NamingConventions.ToContextClassName(_connectionName);

        sb.AppendLine($"public class {contextName} : DbContext");
        sb.AppendLine("{");

        // DbSet properties
        foreach (var table in _tables)
        {
            var entityName = tableToEntity[table.Name];
            var dbSetName = table.Name; // Use original table name for DbSet property
            sb.AppendLine($"    public DbSet<{entityName}> {dbSetName} {{ get; set; }} = default!;");
        }

        sb.AppendLine();

        // Constructor accepting DbContextOptions
        sb.AppendLine($"    public {contextName}(DbContextOptions<{contextName}> options) : base(options) {{ }}");
        sb.AppendLine("}");
        sb.AppendLine();

        // Instantiate context using the live connection from the variable store
        var useMethod = GetUseProviderMethod();
        var escapedCs = _connectionString.Replace("\"", "\\\"");
        var connectionVarKey = $"__verso_scaffold_{_connectionName}_connection";
        var varName = ToPascalCaseFirstLower(_connectionName) + "Context";

        sb.AppendLine($"var __verso_builder = new DbContextOptionsBuilder<{contextName}>();");
        sb.AppendLine($"var __verso_conn = Variables.Get<System.Data.Common.DbConnection>(\"{connectionVarKey}\");");
        sb.AppendLine($"if (__verso_conn != null)");
        sb.AppendLine($"    __verso_builder.{useMethod}(__verso_conn);");
        sb.AppendLine($"else");
        sb.AppendLine($"    __verso_builder.{useMethod}(\"{escapedCs}\");");
        sb.AppendLine($"var {varName} = new {contextName}(__verso_builder.Options);");

        return new ScaffoldResult(
            sb.ToString(),
            entityNames.Count,
            relationshipCount,
            entityNames,
            contextName);
    }

    private static bool IsConventionalPkName(string columnName, string entityName)
    {
        if (columnName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            return true;
        if (columnName.Equals(entityName + "Id", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private string GetUseProviderMethod()
    {
        if (_providerName is null)
            return "UseSqlite";

        if (_providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return "UseSqlite";
        if (_providerName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase))
            return "UseSqlServer";
        if (_providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return "UseNpgsql";
        if (_providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return "UseMySql";

        return "UseSqlite";
    }

    private static string ToPascalCaseFirstLower(string name)
    {
        var pascal = NamingConventions.ToPascalCase(name);
        if (pascal.Length == 0)
            return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }
}
