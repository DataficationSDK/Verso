using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class SqlDialectResolverTests
{
    [TestMethod]
    public void FromProviderName_SqlClient_ReturnsSqlServer()
    {
        Assert.AreEqual(SqlDialect.SqlServer,
            SqlDialectResolver.FromProviderName("Microsoft.Data.SqlClient"));
    }

    [TestMethod]
    public void FromProviderName_Sqlite_ReturnsSqlite()
    {
        Assert.AreEqual(SqlDialect.Sqlite,
            SqlDialectResolver.FromProviderName("Microsoft.Data.Sqlite"));
    }

    [TestMethod]
    public void FromProviderName_Npgsql_ReturnsPostgres()
    {
        Assert.AreEqual(SqlDialect.Postgres,
            SqlDialectResolver.FromProviderName("Npgsql"));
    }

    [TestMethod]
    public void FromProviderName_MySqlData_ReturnsMySql()
    {
        Assert.AreEqual(SqlDialect.MySql,
            SqlDialectResolver.FromProviderName("MySql.Data.MySqlClient"));
    }

    [TestMethod]
    public void FromProviderName_MySqlConnector_ReturnsMySql()
    {
        Assert.AreEqual(SqlDialect.MySql,
            SqlDialectResolver.FromProviderName("MySqlConnector"));
    }

    [TestMethod]
    public void FromProviderName_Oracle_ReturnsOracle()
    {
        Assert.AreEqual(SqlDialect.Oracle,
            SqlDialectResolver.FromProviderName("Oracle.ManagedDataAccess.Client"));
    }

    [TestMethod]
    public void FromProviderName_Null_ReturnsUnknown()
    {
        Assert.AreEqual(SqlDialect.Unknown, SqlDialectResolver.FromProviderName(null));
    }

    [TestMethod]
    public void FromProviderName_Empty_ReturnsUnknown()
    {
        Assert.AreEqual(SqlDialect.Unknown, SqlDialectResolver.FromProviderName(""));
        Assert.AreEqual(SqlDialect.Unknown, SqlDialectResolver.FromProviderName("   "));
    }

    [TestMethod]
    public void FromProviderName_Unrecognized_ReturnsUnknown()
    {
        Assert.AreEqual(SqlDialect.Unknown,
            SqlDialectResolver.FromProviderName("SomeOther.Provider"));
    }

    [TestMethod]
    public void FromProviderName_CaseInsensitive()
    {
        Assert.AreEqual(SqlDialect.SqlServer,
            SqlDialectResolver.FromProviderName("microsoft.data.sqlclient"));
        Assert.AreEqual(SqlDialect.MySql,
            SqlDialectResolver.FromProviderName("MYSQLCONNECTOR"));
    }
}
