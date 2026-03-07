using Verso.Http.Models;
using Verso.Http.Parsing;
using Verso.Testing.Stubs;

namespace Verso.Http.Tests.Parsing;

[TestClass]
public sealed class HttpVariableResolverTests
{
    [TestMethod]
    public void Resolve_FileVariable_Substituted()
    {
        var vars = new List<ParsedVariable>
        {
            new("host", "api.example.com", 0)
        };
        var resolver = new HttpVariableResolver(vars, null);

        Assert.AreEqual("https://api.example.com/v1", resolver.Resolve("https://{{host}}/v1"));
    }

    [TestMethod]
    public void Resolve_SelfReferencingVariables_Resolved()
    {
        var vars = new List<ParsedVariable>
        {
            new("hostname", "api.example.com", 0),
            new("baseUrl", "https://{{hostname}}/v1", 1),
        };
        var resolver = new HttpVariableResolver(vars, null);

        Assert.AreEqual("https://api.example.com/v1/users", resolver.Resolve("{{baseUrl}}/users"));
    }

    [TestMethod]
    public void Resolve_VariableStore_Fallback()
    {
        var ctx = new StubMagicCommandContext();
        ctx.Variables.Set("apiKey", "secret123");

        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), ctx.Variables);

        Assert.AreEqual("Bearer secret123", resolver.Resolve("Bearer {{apiKey}}"));
    }

    [TestMethod]
    public void Resolve_FileVariablePrecedence_OverStoreVariable()
    {
        var ctx = new StubMagicCommandContext();
        ctx.Variables.Set("host", "store-host.com");

        var vars = new List<ParsedVariable> { new("host", "file-host.com", 0) };
        var resolver = new HttpVariableResolver(vars, ctx.Variables);

        Assert.AreEqual("https://file-host.com", resolver.Resolve("https://{{host}}"));
    }

    [TestMethod]
    public void Resolve_Guid_GeneratesValidGuid()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        var result = resolver.Resolve("{{$guid}}");

        Assert.IsTrue(Guid.TryParse(result, out _));
    }

    [TestMethod]
    public void Resolve_RandomInt_GeneratesNumber()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        var result = resolver.Resolve("{{$randomInt 1 100}}");

        Assert.IsTrue(int.TryParse(result, out var n));
        Assert.IsTrue(n >= 1 && n < 100);
    }

    [TestMethod]
    public void Resolve_Timestamp_GeneratesUnixTime()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        var result = resolver.Resolve("{{$timestamp}}");

        Assert.IsTrue(long.TryParse(result, out var ts));
        Assert.IsTrue(ts > 1_700_000_000); // After ~2023
    }

    [TestMethod]
    public void Resolve_ProcessEnv_ReadsEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("VERSO_HTTP_TEST_VAR", "test_value");
        try
        {
            var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
            var result = resolver.Resolve("{{$processEnv VERSO_HTTP_TEST_VAR}}");

            Assert.AreEqual("test_value", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERSO_HTTP_TEST_VAR", null);
        }
    }

    [TestMethod]
    public void Resolve_UnknownVariable_LeftAsIs()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        var result = resolver.Resolve("{{unknownVar}}");

        Assert.AreEqual("{{unknownVar}}", result);
    }

    [TestMethod]
    public void Resolve_MultipleVariables_AllResolved()
    {
        var vars = new List<ParsedVariable>
        {
            new("host", "api.example.com", 0),
            new("version", "v2", 1),
        };
        var resolver = new HttpVariableResolver(vars, null);

        Assert.AreEqual("https://api.example.com/v2/users",
            resolver.Resolve("https://{{host}}/{{version}}/users"));
    }

    [TestMethod]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        Assert.AreEqual("", resolver.Resolve(""));
    }

    [TestMethod]
    public void Resolve_NullInput_ReturnsNull()
    {
        var resolver = new HttpVariableResolver(Array.Empty<ParsedVariable>(), null);
        Assert.IsNull(resolver.Resolve(null!));
    }
}
