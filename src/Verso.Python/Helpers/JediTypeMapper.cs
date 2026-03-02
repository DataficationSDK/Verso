namespace Verso.Python.Helpers;

/// <summary>
/// Maps jedi completion type strings to Verso completion kind strings.
/// </summary>
internal static class JediTypeMapper
{
    internal static string Map(string jediType) => jediType switch
    {
        "function" => "Method",
        "class" => "Class",
        "instance" => "Variable",
        "module" => "Namespace",
        "keyword" => "Keyword",
        "statement" => "Variable",
        "param" => "Variable",
        "property" => "Property",
        _ => "Text"
    };
}
