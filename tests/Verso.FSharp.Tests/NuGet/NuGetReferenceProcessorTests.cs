using Verso.Abstractions;
using Verso.Contexts;
using Verso.FSharp.NuGet;

namespace Verso.FSharp.Tests.NuGet;

[TestClass]
public class NuGetReferenceProcessorTests
{
    [TestMethod]
    public void ContainsNuGetDirectives_WithDirective_ReturnsTrue()
    {
        var code = "#r \"nuget: Newtonsoft.Json, 13.0.3\"\nlet x = 42";
        Assert.IsTrue(NuGetReferenceProcessor.ContainsNuGetDirectives(code));
    }

    [TestMethod]
    public void ContainsNuGetDirectives_WithoutDirective_ReturnsFalse()
    {
        var code = "let x = 42\nprintfn \"%d\" x";
        Assert.IsFalse(NuGetReferenceProcessor.ContainsNuGetDirectives(code));
    }

    [TestMethod]
    public void ContainsNuGetDirectives_WithRegularRef_ReturnsFalse()
    {
        var code = "#r \"MyLib.dll\"\nlet x = 42";
        Assert.IsFalse(NuGetReferenceProcessor.ContainsNuGetDirectives(code));
    }

    [TestMethod]
    public void ContainsNuGetDirectives_MultipleDirectives_ReturnsTrue()
    {
        var code = "#r \"nuget: PackageA\"\n#r \"nuget: PackageB, 1.0.0\"\nlet x = 42";
        Assert.IsTrue(NuGetReferenceProcessor.ContainsNuGetDirectives(code));
    }

    [TestMethod]
    public void CheckMagicCommandResults_WithPaths_ReturnsNewPaths()
    {
        var processor = new NuGetReferenceProcessor();
        var variables = new VariableStore();
        variables.Set(NuGetReferenceProcessor.AssemblyStoreKey,
            new List<string> { "/path/to/assembly1.dll", "/path/to/assembly2.dll" });

        var paths = processor.CheckMagicCommandResults(variables);

        Assert.AreEqual(2, paths.Count);
        Assert.AreEqual("/path/to/assembly1.dll", paths[0]);
        Assert.AreEqual("/path/to/assembly2.dll", paths[1]);
    }

    [TestMethod]
    public void CheckMagicCommandResults_RemovesKeyAfterProcessing()
    {
        var processor = new NuGetReferenceProcessor();
        var variables = new VariableStore();
        variables.Set(NuGetReferenceProcessor.AssemblyStoreKey,
            new List<string> { "/path/to/assembly.dll" });

        processor.CheckMagicCommandResults(variables);

        Assert.IsFalse(variables.TryGet<List<string>>(NuGetReferenceProcessor.AssemblyStoreKey, out _));
    }

    [TestMethod]
    public void CheckMagicCommandResults_DeduplicatesPaths()
    {
        var processor = new NuGetReferenceProcessor();
        var variables = new VariableStore();

        // First call
        variables.Set(NuGetReferenceProcessor.AssemblyStoreKey,
            new List<string> { "/path/to/assembly.dll" });
        processor.CheckMagicCommandResults(variables);

        // Second call with same path
        variables.Set(NuGetReferenceProcessor.AssemblyStoreKey,
            new List<string> { "/path/to/assembly.dll" });
        var paths = processor.CheckMagicCommandResults(variables);

        Assert.AreEqual(0, paths.Count, "Duplicate paths should be filtered out");
    }

    [TestMethod]
    public void CheckMagicCommandResults_NoPaths_ReturnsEmpty()
    {
        var processor = new NuGetReferenceProcessor();
        var variables = new VariableStore();

        var paths = processor.CheckMagicCommandResults(variables);

        Assert.AreEqual(0, paths.Count);
    }
}
