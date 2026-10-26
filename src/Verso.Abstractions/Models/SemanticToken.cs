namespace Verso.Abstractions;

/// <summary>
/// One classified span of a cell's source, returned by <see cref="ISemanticTokenProvider"/>.
/// A token lies on a single line.
/// </summary>
/// <param name="Line">The zero-based line of the token, relative to the cell's source as for a <see cref="Diagnostic"/>.</param>
/// <param name="StartColumn">The zero-based column where the token begins, in UTF-16 characters.</param>
/// <param name="Length">The token's length in UTF-16 characters (as <see cref="string.Length"/> counts them).</param>
/// <param name="Type">
/// What the token names, one of <see cref="SemanticTokenTypes"/>. The editor ignores a token of a
/// type it does not know.
/// </param>
/// <param name="Modifiers">
/// Refinements of <paramref name="Type"/>, from <see cref="SemanticTokenModifiers"/>, or <c>null</c>
/// for none. The editor ignores a modifier it does not know.
/// </param>
public sealed record SemanticToken(
    int Line,
    int StartColumn,
    int Length,
    string Type,
    IReadOnlyList<string>? Modifiers = null);

/// <summary>
/// The token types a <see cref="SemanticToken"/> may carry. The names are the standard ones of the
/// Language Server Protocol, which the editor's themes color by.
/// </summary>
public static class SemanticTokenTypes
{
    /// <summary>A namespace, or a module that groups declarations the way one does (an F# module).</summary>
    public const string Namespace = "namespace";
    /// <summary>A type with no more specific kind below, or a type abbreviation.</summary>
    public const string Type = "type";
    /// <summary>A class, record class, exception or other reference type.</summary>
    public const string Class = "class";
    /// <summary>A struct or other value type.</summary>
    public const string Struct = "struct";
    /// <summary>An enumeration.</summary>
    public const string Enum = "enum";
    /// <summary>An interface.</summary>
    public const string Interface = "interface";
    /// <summary>A generic type parameter.</summary>
    public const string TypeParameter = "typeParameter";
    /// <summary>A function or method parameter.</summary>
    public const string Parameter = "parameter";
    /// <summary>A variable or value.</summary>
    public const string Variable = "variable";
    /// <summary>A property or field, including a record field.</summary>
    public const string Property = "property";
    /// <summary>An enumeration member, a union case or a named constant.</summary>
    public const string EnumMember = "enumMember";
    /// <summary>An event.</summary>
    public const string Event = "event";
    /// <summary>A function.</summary>
    public const string Function = "function";
    /// <summary>A method.</summary>
    public const string Method = "method";
    /// <summary>A keyword that a lexer cannot recognize as one (an F# computation expression builder).</summary>
    public const string Keyword = "keyword";
    /// <summary>An operator.</summary>
    public const string Operator = "operator";
}

/// <summary>
/// The modifiers a <see cref="SemanticToken"/> may carry.
/// </summary>
public static class SemanticTokenModifiers
{
    /// <summary>A value or field that can be reassigned.</summary>
    public const string Mutable = "mutable";
    /// <summary>A member that belongs to its type rather than to an instance.</summary>
    public const string Static = "static";
}
