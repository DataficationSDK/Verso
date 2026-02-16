using FSharp.Compiler.EditorServices;
using Verso.FSharp.Helpers;

namespace Verso.FSharp.Tests.Helpers;

[TestClass]
public class GlyphMapperTests
{
    [TestMethod]
    public void MapGlyph_Class_ReturnsClass() =>
        Assert.AreEqual("Class", GlyphMapper.MapGlyph(FSharpGlyph.Class));

    [TestMethod]
    public void MapGlyph_Struct_ReturnsStruct() =>
        Assert.AreEqual("Struct", GlyphMapper.MapGlyph(FSharpGlyph.Struct));

    [TestMethod]
    public void MapGlyph_Interface_ReturnsInterface() =>
        Assert.AreEqual("Interface", GlyphMapper.MapGlyph(FSharpGlyph.Interface));

    [TestMethod]
    public void MapGlyph_Enum_ReturnsEnum() =>
        Assert.AreEqual("Enum", GlyphMapper.MapGlyph(FSharpGlyph.Enum));

    [TestMethod]
    public void MapGlyph_EnumMember_ReturnsEnumMember() =>
        Assert.AreEqual("EnumMember", GlyphMapper.MapGlyph(FSharpGlyph.EnumMember));

    [TestMethod]
    public void MapGlyph_Module_ReturnsModule() =>
        Assert.AreEqual("Module", GlyphMapper.MapGlyph(FSharpGlyph.Module));

    [TestMethod]
    public void MapGlyph_NameSpace_ReturnsNamespace() =>
        Assert.AreEqual("Namespace", GlyphMapper.MapGlyph(FSharpGlyph.NameSpace));

    [TestMethod]
    public void MapGlyph_Method_ReturnsMethod() =>
        Assert.AreEqual("Method", GlyphMapper.MapGlyph(FSharpGlyph.Method));

    [TestMethod]
    public void MapGlyph_OverridenMethod_ReturnsMethod() =>
        Assert.AreEqual("Method", GlyphMapper.MapGlyph(FSharpGlyph.OverridenMethod));

    [TestMethod]
    public void MapGlyph_ExtensionMethod_ReturnsMethod() =>
        Assert.AreEqual("Method", GlyphMapper.MapGlyph(FSharpGlyph.ExtensionMethod));

    [TestMethod]
    public void MapGlyph_Property_ReturnsProperty() =>
        Assert.AreEqual("Property", GlyphMapper.MapGlyph(FSharpGlyph.Property));

    [TestMethod]
    public void MapGlyph_Field_ReturnsField() =>
        Assert.AreEqual("Field", GlyphMapper.MapGlyph(FSharpGlyph.Field));

    [TestMethod]
    public void MapGlyph_Event_ReturnsEvent() =>
        Assert.AreEqual("Event", GlyphMapper.MapGlyph(FSharpGlyph.Event));

    [TestMethod]
    public void MapGlyph_Delegate_ReturnsDelegate() =>
        Assert.AreEqual("Delegate", GlyphMapper.MapGlyph(FSharpGlyph.Delegate));

    [TestMethod]
    public void MapGlyph_Union_ReturnsEnum() =>
        Assert.AreEqual("Enum", GlyphMapper.MapGlyph(FSharpGlyph.Union));

    [TestMethod]
    public void MapGlyph_Variable_ReturnsVariable() =>
        Assert.AreEqual("Variable", GlyphMapper.MapGlyph(FSharpGlyph.Variable));

    [TestMethod]
    public void MapGlyph_Type_ReturnsClass() =>
        Assert.AreEqual("Class", GlyphMapper.MapGlyph(FSharpGlyph.Type));

    [TestMethod]
    public void MapGlyph_Constant_ReturnsConstant() =>
        Assert.AreEqual("Constant", GlyphMapper.MapGlyph(FSharpGlyph.Constant));

    [TestMethod]
    public void MapGlyph_Exception_ReturnsClass() =>
        Assert.AreEqual("Class", GlyphMapper.MapGlyph(FSharpGlyph.Exception));

    [TestMethod]
    public void MapGlyph_TypeParameter_ReturnsTypeParameter() =>
        Assert.AreEqual("TypeParameter", GlyphMapper.MapGlyph(FSharpGlyph.TypeParameter));

    [TestMethod]
    public void MapGlyph_Typedef_ReturnsClass() =>
        Assert.AreEqual("Class", GlyphMapper.MapGlyph(FSharpGlyph.Typedef));

    [TestMethod]
    public void MapGlyph_Error_ReturnsKeyword() =>
        Assert.AreEqual("Keyword", GlyphMapper.MapGlyph(FSharpGlyph.Error));
}
