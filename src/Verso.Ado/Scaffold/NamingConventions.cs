using System.Text;

namespace Verso.Ado.Scaffold;

/// <summary>
/// Utility for converting database table/column names to C#-idiomatic identifiers.
/// </summary>
internal static class NamingConventions
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// Converts a name to PascalCase, splitting on underscores, hyphens, and existing case transitions.
    /// </summary>
    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = true;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            if (c == '_' || c == '-' || c == ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Basic singularization: "Orders" -> "Order", "Categories" -> "Category",
    /// "Addresses" -> "Address". No external dependency.
    /// </summary>
    internal static string Singularize(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            return name;

        // "ies" -> "y" (e.g. Categories -> Category)
        if (name.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 3) + "y";

        // "sses" -> "ss" (e.g. Addresses -> Address... but actually Addresses ends in "es")
        if (name.EndsWith("sses", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 2);

        // "xes", "ches", "shes", "zes" -> remove "es"
        if (name.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("zes", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 2);

        // "ses" -> remove trailing "s" (e.g. Addresses -> Addresse... no)
        // Actually "ses" for sibilant: "Buses" -> "Bus"
        if (name.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
            return name.Substring(0, name.Length - 2);

        // Don't singularize words ending in "ss", "us", "is"
        if (name.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("us", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("is", StringComparison.OrdinalIgnoreCase))
            return name;

        // General trailing "s"
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 1);

        return name;
    }

    /// <summary>
    /// Converts a table name to an entity class name: PascalCase + singularize, strips "tbl_" prefix.
    /// </summary>
    internal static string ToEntityClassName(string tableName)
    {
        var name = tableName;

        // Strip common prefixes
        if (name.StartsWith("tbl_", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(4);
        else if (name.StartsWith("tbl", StringComparison.OrdinalIgnoreCase) && name.Length > 3 && char.IsUpper(name[3]))
            name = name.Substring(3);

        name = ToPascalCase(name);
        name = Singularize(name);

        return name;
    }

    /// <summary>
    /// Converts a column name to a C# property name: PascalCase, escapes C# keywords with @.
    /// </summary>
    internal static string ToPropertyName(string columnName)
    {
        var name = ToPascalCase(columnName);

        if (IsCSharpKeyword(name))
            return "@" + name;

        return name;
    }

    /// <summary>
    /// Generates a DbContext class name from a connection name: PascalCase + "Context".
    /// </summary>
    internal static string ToContextClassName(string connectionName)
    {
        return ToPascalCase(connectionName) + "Context";
    }

    /// <summary>
    /// Returns <c>true</c> if the name is a C# reserved keyword.
    /// </summary>
    internal static bool IsCSharpKeyword(string name)
    {
        // Keywords are case-sensitive in C#; compare lowercase since keywords are lowercase
        return CSharpKeywords.Contains(name.ToLowerInvariant());
    }
}
