using System.Data;
using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class DbTypeMapperTests
{
    [TestMethod]
    [DataRow(typeof(string), DbType.String)]
    [DataRow(typeof(int), DbType.Int32)]
    [DataRow(typeof(long), DbType.Int64)]
    [DataRow(typeof(short), DbType.Int16)]
    [DataRow(typeof(byte), DbType.Byte)]
    [DataRow(typeof(bool), DbType.Boolean)]
    [DataRow(typeof(decimal), DbType.Decimal)]
    [DataRow(typeof(double), DbType.Double)]
    [DataRow(typeof(float), DbType.Single)]
    [DataRow(typeof(DateTime), DbType.DateTime)]
    [DataRow(typeof(Guid), DbType.Guid)]
    public void TryMapDbType_SupportedType_ReturnsTrueWithCorrectType(Type clrType, DbType expected)
    {
        var result = DbTypeMapper.TryMapDbType(clrType, out var dbType);

        Assert.IsTrue(result);
        Assert.AreEqual(expected, dbType);
    }

    [TestMethod]
    public void TryMapDbType_UnsupportedType_ReturnsFalse()
    {
        var result = DbTypeMapper.TryMapDbType(typeof(object), out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryMapDbType_CustomClass_ReturnsFalse()
    {
        var result = DbTypeMapper.TryMapDbType(typeof(DbTypeMapperTests), out _);

        Assert.IsFalse(result);
    }
}
