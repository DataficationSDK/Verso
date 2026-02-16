using FSharp.Compiler.EditorServices;

namespace Verso.FSharp.Helpers;

/// <summary>
/// Maps F# compiler glyph values to Verso completion kind strings.
/// </summary>
internal static class GlyphMapper
{
    /// <summary>
    /// Converts an <see cref="FSharpGlyph"/> to a Verso completion kind string.
    /// </summary>
    public static string MapGlyph(FSharpGlyph glyph)
    {
        if (glyph.IsClass) return "Class";
        if (glyph.IsStruct) return "Struct";
        if (glyph.IsInterface) return "Interface";
        if (glyph.IsEnum) return "Enum";
        if (glyph.IsEnumMember) return "EnumMember";
        if (glyph.IsModule) return "Module";
        if (glyph.IsNameSpace) return "Namespace";
        if (glyph.IsMethod) return "Method";
        if (glyph.IsOverridenMethod) return "Method";
        if (glyph.IsExtensionMethod) return "Method";
        if (glyph.IsProperty) return "Property";
        if (glyph.IsField) return "Field";
        if (glyph.IsEvent) return "Event";
        if (glyph.IsDelegate) return "Delegate";
        if (glyph.IsUnion) return "Enum";
        if (glyph.IsVariable) return "Variable";
        if (glyph.IsType) return "Class";
        if (glyph.IsConstant) return "Constant";
        if (glyph.IsException) return "Class";
        if (glyph.IsTypeParameter) return "TypeParameter";
        if (glyph.IsTypedef) return "Class";
        if (glyph.IsError) return "Keyword";
        return "Text";
    }
}
