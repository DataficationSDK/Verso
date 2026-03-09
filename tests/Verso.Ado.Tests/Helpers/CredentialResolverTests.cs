using Verso.Ado.Helpers;
using Verso.Contexts;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class CredentialResolverTests
{
    [TestMethod]
    public void ResolveConnectionString_PlainString_PassesThrough()
    {
        var (resolved, error) = CredentialResolver.ResolveConnectionString("Data Source=:memory:");

        Assert.IsNull(error);
        Assert.AreEqual("Data Source=:memory:", resolved);
    }

    [TestMethod]
    public void ResolveConnectionString_EnvVar_Expands()
    {
        Environment.SetEnvironmentVariable("VERSO_TEST_DB", "mydb.sqlite");
        try
        {
            var (resolved, error) = CredentialResolver.ResolveConnectionString("Data Source=$env:VERSO_TEST_DB");

            Assert.IsNull(error);
            Assert.AreEqual("Data Source=mydb.sqlite", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERSO_TEST_DB", null);
        }
    }

    [TestMethod]
    public void ResolveConnectionString_UndefinedEnvVar_ReturnsError()
    {
        Environment.SetEnvironmentVariable("VERSO_NONEXISTENT_XYZ", null);

        var (resolved, error) = CredentialResolver.ResolveConnectionString("Data Source=$env:VERSO_NONEXISTENT_XYZ");

        Assert.IsNull(resolved);
        Assert.IsNotNull(error);
        Assert.IsTrue(error!.Contains("VERSO_NONEXISTENT_XYZ"));
    }

    [TestMethod]
    public void ResolveConnectionString_SecretPlaceholder_ReturnsError()
    {
        var (resolved, error) = CredentialResolver.ResolveConnectionString("Password=$secret:MyPassword");

        Assert.IsNull(resolved);
        Assert.IsNotNull(error);
        Assert.IsTrue(error!.Contains("$secret:"));
    }

    [TestMethod]
    public void ResolveConnectionString_EmptyString_ReturnsError()
    {
        var (resolved, error) = CredentialResolver.ResolveConnectionString("");

        Assert.IsNull(resolved);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ResolveConnectionString_MultipleEnvVars_ExpandsAll()
    {
        Environment.SetEnvironmentVariable("VERSO_TEST_HOST", "localhost");
        Environment.SetEnvironmentVariable("VERSO_TEST_PORT", "5432");
        try
        {
            var (resolved, error) = CredentialResolver.ResolveConnectionString(
                "Host=$env:VERSO_TEST_HOST;Port=$env:VERSO_TEST_PORT");

            Assert.IsNull(error);
            Assert.AreEqual("Host=localhost;Port=5432", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERSO_TEST_HOST", null);
            Environment.SetEnvironmentVariable("VERSO_TEST_PORT", null);
        }
    }

    // --- $var: variable store tests ---

    [TestMethod]
    public void ResolveConnectionString_Var_ExpandsFromStore()
    {
        var store = new VariableStore();
        store.Set("connStr", "Server=localhost;Database=mydb");

        var (resolved, error) = CredentialResolver.ResolveConnectionString(
            "$var:connStr", store);

        Assert.IsNull(error);
        Assert.AreEqual("Server=localhost;Database=mydb", resolved);
    }

    [TestMethod]
    public void ResolveConnectionString_Var_ExpandsPartialToken()
    {
        var store = new VariableStore();
        store.Set("dbHost", "prod-server");
        store.Set("dbName", "sales");

        var (resolved, error) = CredentialResolver.ResolveConnectionString(
            "Server=$var:dbHost;Database=$var:dbName", store);

        Assert.IsNull(error);
        Assert.AreEqual("Server=prod-server;Database=sales", resolved);
    }

    [TestMethod]
    public void ResolveConnectionString_Var_MixedWithEnv()
    {
        var store = new VariableStore();
        store.Set("dbName", "sales");
        Environment.SetEnvironmentVariable("VERSO_TEST_HOST2", "prod-server");
        try
        {
            var (resolved, error) = CredentialResolver.ResolveConnectionString(
                "Server=$env:VERSO_TEST_HOST2;Database=$var:dbName", store);

            Assert.IsNull(error);
            Assert.AreEqual("Server=prod-server;Database=sales", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERSO_TEST_HOST2", null);
        }
    }

    [TestMethod]
    public void ResolveConnectionString_Var_UndefinedVariable_ReturnsError()
    {
        var store = new VariableStore();

        var (resolved, error) = CredentialResolver.ResolveConnectionString(
            "Server=$var:noSuchVar", store);

        Assert.IsNull(resolved);
        Assert.IsNotNull(error);
        Assert.IsTrue(error!.Contains("noSuchVar"));
    }

    [TestMethod]
    public void ResolveConnectionString_Var_NoStore_ReturnsError()
    {
        var (resolved, error) = CredentialResolver.ResolveConnectionString(
            "Server=$var:myHost");

        Assert.IsNull(resolved);
        Assert.IsNotNull(error);
        Assert.IsTrue(error!.Contains("variable store"));
    }

    [TestMethod]
    public void ResolveConnectionString_Var_CaseInsensitiveLookup()
    {
        var store = new VariableStore();
        store.Set("MyConnStr", "Data Source=:memory:");

        var (resolved, error) = CredentialResolver.ResolveConnectionString(
            "$var:myconnstr", store);

        Assert.IsNull(error);
        Assert.AreEqual("Data Source=:memory:", resolved);
    }

    [TestMethod]
    public void RedactConnectionString_WithPassword_Redacts()
    {
        var result = CredentialResolver.RedactConnectionString(
            "Server=localhost;Database=mydb;Password=supersecret;");

        Assert.IsTrue(result.Contains("Password=***"));
        Assert.IsFalse(result.Contains("supersecret"));
    }

    [TestMethod]
    public void RedactConnectionString_WithPwd_Redacts()
    {
        var result = CredentialResolver.RedactConnectionString(
            "Server=localhost;Database=mydb;Pwd=supersecret;");

        Assert.IsTrue(result.Contains("Pwd=***"));
        Assert.IsFalse(result.Contains("supersecret"));
    }

    [TestMethod]
    public void RedactConnectionString_NoPassword_Unchanged()
    {
        var input = "Data Source=:memory:";
        var result = CredentialResolver.RedactConnectionString(input);

        Assert.AreEqual(input, result);
    }
}
