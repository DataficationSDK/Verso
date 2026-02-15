using Verso.Ado.Scaffold;

namespace Verso.Ado.Tests.Scaffold;

[TestClass]
public sealed class EfCorePrerequisiteCheckerTests
{
    [TestMethod]
    public void GetInstallHint_NullProvider_ReturnsBasePackageOnly()
    {
        var hint = EfCorePrerequisiteChecker.GetInstallHint(null);

        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore"));
        Assert.IsFalse(hint.Contains("Sqlite"));
        Assert.IsFalse(hint.Contains("SqlServer"));
    }

    [TestMethod]
    public void GetInstallHint_SqliteProvider_IncludesSqlitePackage()
    {
        var hint = EfCorePrerequisiteChecker.GetInstallHint("Microsoft.Data.Sqlite");

        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore"));
        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore.Sqlite"));
    }

    [TestMethod]
    public void GetInstallHint_SqlServerProvider_IncludesSqlServerPackage()
    {
        var hint = EfCorePrerequisiteChecker.GetInstallHint("Microsoft.Data.SqlClient");

        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore"));
        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore.SqlServer"));
    }

    [TestMethod]
    public void GetInstallHint_NpgsqlProvider_IncludesNpgsqlPackage()
    {
        var hint = EfCorePrerequisiteChecker.GetInstallHint("Npgsql");

        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore"));
        Assert.IsTrue(hint.Contains("Npgsql.EntityFrameworkCore.PostgreSQL"));
    }

    [TestMethod]
    public void GetInstallHint_UnknownProvider_ReturnsBasePackageOnly()
    {
        var hint = EfCorePrerequisiteChecker.GetInstallHint("Some.Unknown.Provider");

        Assert.IsTrue(hint.Contains("Microsoft.EntityFrameworkCore"));
        // Should not contain any provider-specific package
        Assert.IsFalse(hint.Contains("Sqlite"));
        Assert.IsFalse(hint.Contains("SqlServer"));
    }
}
