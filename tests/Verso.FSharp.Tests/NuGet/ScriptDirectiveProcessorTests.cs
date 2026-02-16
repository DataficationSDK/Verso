using Verso.Abstractions;
using Verso.FSharp.NuGet;

namespace Verso.FSharp.Tests.NuGet;

[TestClass]
public class ScriptDirectiveProcessorTests
{
    [TestMethod]
    public void ProcessDirectives_NowarnAddsToSuppressedWarnings()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "#nowarn \"40\"\nlet x = 42";

        processor.ProcessDirectives(code, null);

        Assert.IsTrue(processor.SuppressedWarnings.Contains(40));
    }

    [TestMethod]
    public void ProcessDirectives_MultipleNowarn_TracksAll()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "#nowarn \"40\"\n#nowarn \"58\"\nlet x = 42";

        processor.ProcessDirectives(code, null);

        Assert.IsTrue(processor.SuppressedWarnings.Contains(40));
        Assert.IsTrue(processor.SuppressedWarnings.Contains(58));
    }

    [TestMethod]
    public void ProcessDirectives_Time_PassesThrough()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "#time \"on\"\nlet x = 42";

        var result = processor.ProcessDirectives(code, null);

        Assert.IsTrue(result.Contains("#time \"on\""));
    }

    [TestMethod]
    public void ProcessDirectives_NoDirectives_ReturnsUnchanged()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "let x = 42\nprintfn \"%d\" x";

        var result = processor.ProcessDirectives(code, null);

        Assert.AreEqual(code, result);
    }

    [TestMethod]
    public void ResolvePath_AbsolutePath_ReturnsAsIs()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "test.dll");
        var result = ScriptDirectiveProcessor.ResolvePath(absolutePath, null);
        Assert.AreEqual(absolutePath, result);
    }

    [TestMethod]
    public void ResolvePath_RelativePathWithNotebook_ResolvesAgainstNotebookDir()
    {
        var notebookDir = Path.GetTempPath();
        var metadata = new StubNotebookMetadata(Path.Combine(notebookDir, "notebook.dib"));

        var result = ScriptDirectiveProcessor.ResolvePath("libs/test.dll", metadata);

        var expected = Path.GetFullPath(Path.Combine(notebookDir, "libs/test.dll"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_RelativePathWithoutNotebook_ResolvesAgainstBaseDir()
    {
        var result = ScriptDirectiveProcessor.ResolvePath("test.dll", null);

        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test.dll"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ProcessDirectives_AssemblyRef_TracksResolvedPath()
    {
        var processor = new ScriptDirectiveProcessor();
        // Use a path to an assembly that exists
        var existingDll = typeof(object).Assembly.Location;
        var code = $"#r \"{existingDll}\"\nlet x = 42";

        processor.ProcessDirectives(code, null);

        Assert.IsTrue(processor.ResolvedAssemblyPaths.Contains(existingDll),
            "Expected the existing assembly path to be tracked");
    }

    [TestMethod]
    public void ProcessDirectives_NuGetRef_IsNotProcessed()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "#r \"nuget: Newtonsoft.Json\"\nlet x = 42";

        var result = processor.ProcessDirectives(code, null);

        // NuGet refs should NOT be processed by the script directive processor
        Assert.IsTrue(result.Contains("#r \"nuget: Newtonsoft.Json\""),
            "NuGet references should pass through unchanged");
    }

    [TestMethod]
    public void ProcessDirectives_Load_TracksLoadedFilePath()
    {
        var processor = new ScriptDirectiveProcessor();
        // Create a temp .fsx file to load
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.fsx");
        File.WriteAllText(tempFile, "let loaded = 42");
        try
        {
            var code = $"#load \"{tempFile}\"\nlet x = loaded";
            processor.ProcessDirectives(code, null);

            Assert.AreEqual(1, processor.LoadedFilePaths.Count,
                "Expected one loaded file path");
            Assert.AreEqual(tempFile, processor.LoadedFilePaths[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void ProcessDirectives_Load_NonExistentFile_NotTracked()
    {
        var processor = new ScriptDirectiveProcessor();
        var code = "#load \"nonexistent_file.fsx\"\nlet x = 42";

        processor.ProcessDirectives(code, null);

        Assert.AreEqual(0, processor.LoadedFilePaths.Count,
            "Non-existent files should not be tracked");
    }

    /// <summary>
    /// Minimal stub for INotebookMetadata used in tests.
    /// </summary>
    private sealed class StubNotebookMetadata : INotebookMetadata
    {
        public StubNotebookMetadata(string? filePath) => FilePath = filePath;
        public string? Title => null;
        public string? DefaultKernelId => null;
        public string? FilePath { get; }
    }
}
