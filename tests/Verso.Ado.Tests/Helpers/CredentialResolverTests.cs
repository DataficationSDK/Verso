using Verso.Ado.Helpers;

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
