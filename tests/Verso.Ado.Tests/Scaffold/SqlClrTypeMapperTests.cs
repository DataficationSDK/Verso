using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Scaffold;

[TestClass]
public sealed class SqlClrTypeMapperTests
{
    [TestMethod]
    [DataRow("int", typeof(int))]
    [DataRow("integer", typeof(int))]
    [DataRow("bigint", typeof(long))]
    [DataRow("smallint", typeof(short))]
    [DataRow("tinyint", typeof(byte))]
    [DataRow("bit", typeof(bool))]
    [DataRow("boolean", typeof(bool))]
    [DataRow("decimal", typeof(decimal))]
    [DataRow("numeric", typeof(decimal))]
    [DataRow("money", typeof(decimal))]
    [DataRow("float", typeof(double))]
    [DataRow("real", typeof(float))]
    [DataRow("varchar", typeof(string))]
    [DataRow("nvarchar", typeof(string))]
    [DataRow("text", typeof(string))]
    [DataRow("datetime", typeof(DateTime))]
    [DataRow("datetime2", typeof(DateTime))]
    [DataRow("date", typeof(DateTime))]
    [DataRow("time", typeof(TimeSpan))]
    [DataRow("uniqueidentifier", typeof(Guid))]
    [DataRow("uuid", typeof(Guid))]
    [DataRow("varbinary", typeof(byte[]))]
    [DataRow("image", typeof(byte[]))]
    [DataRow("blob", typeof(byte[]))]
    public void MapSqlType_StandardTypes(string sqlType, Type expected)
    {
        Assert.AreEqual(expected, SqlClrTypeMapper.MapSqlType(sqlType));
    }

    [TestMethod]
    [DataRow("TEXT", typeof(string))]
    [DataRow("INTEGER", typeof(int))]
    [DataRow("REAL", typeof(float))]
    [DataRow("BLOB", typeof(byte[]))]
    [DataRow("NUMERIC", typeof(decimal))]
    public void MapSqlType_SqliteAffinityTypes(string sqlType, Type expected)
    {
        Assert.AreEqual(expected, SqlClrTypeMapper.MapSqlType(sqlType));
    }

    [TestMethod]
    public void MapSqlType_WithLength_StripsParentheses()
    {
        Assert.AreEqual(typeof(string), SqlClrTypeMapper.MapSqlType("varchar(50)"));
        Assert.AreEqual(typeof(string), SqlClrTypeMapper.MapSqlType("nvarchar(max)"));
        Assert.AreEqual(typeof(decimal), SqlClrTypeMapper.MapSqlType("decimal(18,2)"));
    }

    [TestMethod]
    public void MapSqlType_UnknownType_ReturnsObject()
    {
        Assert.AreEqual(typeof(object), SqlClrTypeMapper.MapSqlType("unknown_custom_type"));
    }

    [TestMethod]
    public void MapSqlType_NullOrEmpty_ReturnsObject()
    {
        Assert.AreEqual(typeof(object), SqlClrTypeMapper.MapSqlType(""));
        Assert.AreEqual(typeof(object), SqlClrTypeMapper.MapSqlType("  "));
    }

    [TestMethod]
    public void GetCSharpTypeName_NullableValueTypes()
    {
        Assert.AreEqual("int?", SqlClrTypeMapper.GetCSharpTypeName(typeof(int), true));
        Assert.AreEqual("DateTime?", SqlClrTypeMapper.GetCSharpTypeName(typeof(DateTime), true));
        Assert.AreEqual("Guid?", SqlClrTypeMapper.GetCSharpTypeName(typeof(Guid), true));
        Assert.AreEqual("bool?", SqlClrTypeMapper.GetCSharpTypeName(typeof(bool), true));
    }

    [TestMethod]
    public void GetCSharpTypeName_NonNullableTypes()
    {
        Assert.AreEqual("int", SqlClrTypeMapper.GetCSharpTypeName(typeof(int), false));
        Assert.AreEqual("string", SqlClrTypeMapper.GetCSharpTypeName(typeof(string), false));
        Assert.AreEqual("DateTime", SqlClrTypeMapper.GetCSharpTypeName(typeof(DateTime), false));
    }

    [TestMethod]
    public void GetCSharpTypeName_NullableString()
    {
        Assert.AreEqual("string?", SqlClrTypeMapper.GetCSharpTypeName(typeof(string), true));
    }

    [TestMethod]
    public void GetCSharpTypeName_ByteArray()
    {
        Assert.AreEqual("byte[]", SqlClrTypeMapper.GetCSharpTypeName(typeof(byte[]), false));
        Assert.AreEqual("byte[]?", SqlClrTypeMapper.GetCSharpTypeName(typeof(byte[]), true));
    }
}
