using Verso.Abstractions;
using Verso.Extensions;
using Verso.Kernels;

namespace Verso.Tests.MagicCommands;

/// <summary>
/// Drives real files through the real pipeline, so that an import written inside an imported
/// file is actually read back and acted on.
/// </summary>
/// <remarks>
/// The stub notebook operations used elsewhere record the code they are handed and return, which
/// means a nested directive is never re-read and nothing about nesting is under test. These go
/// through <see cref="Verso.Scaffold"/> with the built-in commands loaded and a real kernel, which
/// is the only arrangement where a nested import resolves a path at all.
/// </remarks>
[TestClass]
public sealed class NestedImportIntegrationTests
{
    private DirectoryInfo _root = null!;

    [TestInitialize]
    public void Setup() => _root = Directory.CreateTempSubdirectory("verso_nested_import_");

    [TestCleanup]
    public void Cleanup() => _root.Delete(recursive: true);

    /// <summary>
    /// Builds a notebook at <paramref name="notebookPath"/> whose cells are run in order, and
    /// returns what the last one produced.
    /// </summary>
    private static async Task<IReadOnlyList<CellOutput>> RunAsync(
        string notebookPath, params string[] cellSources)
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var notebook = new NotebookModel { DefaultKernelId = "csharp" };
        await using var scaffold = new Verso.Scaffold(notebook, host, notebookPath);
        scaffold.RegisterKernel(new CSharpKernel());

        CellModel? last = null;
        foreach (var source in cellSources)
        {
            last = scaffold.AddCell(source: source);
            await scaffold.ExecuteCellAsync(last.Id);
        }

        return last?.Outputs.ToList() ?? new List<CellOutput>();
    }

    private static void AssertProduced(IReadOnlyList<CellOutput> outputs, string expected)
    {
        Assert.IsTrue(
            outputs.Any(o => o.Content.Contains(expected, StringComparison.Ordinal)),
            $"Expected an output containing '{expected}'. Got: " +
            string.Join(" | ", outputs.Select(o => $"[{(o.IsError ? "error" : "ok")}] {o.Content}")));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_ResolvesBesideTheImportingFile()
    {
        var helpers = Directory.CreateDirectory(Path.Combine(_root.FullName, "helpers"));
        await File.WriteAllTextAsync(Path.Combine(helpers.FullName, "Inner.cs"),
            "public static class Inner { public static string V => \"inner\"; }");
        await File.WriteAllTextAsync(Path.Combine(helpers.FullName, "Outer.cs"),
            "#!import Inner.cs\npublic static class Outer { public static string V => \"outer sees \" + Inner.V; }");

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import helpers/Outer.cs",
            "Outer.V");

        AssertProduced(outputs, "outer sees inner");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_UnderADirectoryWhoseNameHasASpace()
    {
        // The defect this guards: the resolved path used to be written back into the directive
        // text, and the parser that reads it back ends a path at the first space.
        //
        // The notebook sits inside the directory whose name has the space, so every path anyone
        // writes is short and space-free. Only the resolved path holds one, which is the whole
        // point: nobody has to write anything unusual to hit this.
        //
        // Inner.cs sits beside Outer.cs and nowhere else, so resolving against the notebook
        // cannot reach it. Without that, falling back to the notebook would quietly find the
        // file anyway and this would pass whether the resolved path survived or not.
        var workspace = Directory.CreateDirectory(Path.Combine(_root.FullName, "my helpers"));
        var lib = Directory.CreateDirectory(Path.Combine(workspace.FullName, "lib"));
        await File.WriteAllTextAsync(Path.Combine(lib.FullName, "Inner.cs"),
            "public static class Inner { public static string V => \"inner\"; }");
        await File.WriteAllTextAsync(Path.Combine(lib.FullName, "Outer.cs"),
            "#!import Inner.cs\npublic static class Outer { public static string V => \"outer sees \" + Inner.V; }");

        var outputs = await RunAsync(
            Path.Combine(workspace.FullName, "notebook.verso"),
            "#!import lib/Outer.cs",
            "Outer.V");

        AssertProduced(outputs, "outer sees inner");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_FallsBackToTheNotebookWhenNothingSitsBeside()
    {
        var shared = Directory.CreateDirectory(Path.Combine(_root.FullName, "shared"));
        await File.WriteAllTextAsync(Path.Combine(shared.FullName, "Common.cs"),
            "public static class Common { public static string V => \"common\"; }");

        var helpers = Directory.CreateDirectory(Path.Combine(_root.FullName, "helpers"));
        // No shared/Common.cs sits beside this file, so the path keeps meaning what it meant
        // before an importing directory was consulted at all.
        await File.WriteAllTextAsync(Path.Combine(helpers.FullName, "Uses.cs"),
            "#!import shared/Common.cs\npublic static class Uses { public static string V => \"uses \" + Common.V; }");

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import helpers/Uses.cs",
            "Uses.V");

        AssertProduced(outputs, "uses common");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_BesideWinsOverTheNotebookForTheSameName()
    {
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Same.cs"),
            "public static class Same { public static string V => \"notebook copy\"; }");

        var helpers = Directory.CreateDirectory(Path.Combine(_root.FullName, "helpers"));
        await File.WriteAllTextAsync(Path.Combine(helpers.FullName, "Same.cs"),
            "public static class Same { public static string V => \"neighbour copy\"; }");
        await File.WriteAllTextAsync(Path.Combine(helpers.FullName, "Picks.cs"),
            "#!import Same.cs\npublic static class Picks { public static string V => Same.V; }");

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import helpers/Picks.cs",
            "Picks.V");

        AssertProduced(outputs, "neighbour copy");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_TwoFilesNamingEachOther_Terminates()
    {
        // Without the guard this recurses until the kernel gives out, which took minutes rather
        // than failing. The timeout is the assertion that matters here.
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "First.cs"),
            "#!import Second.cs\npublic static class First { public static string V => \"first\"; }");
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Second.cs"),
            "#!import First.cs\npublic static class Second { public static string V => \"second\"; }");

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import First.cs",
            "\"survived\"");

        AssertProduced(outputs, "survived");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task NestedImport_ThreeDeepAcrossDirectories()
    {
        var one = Directory.CreateDirectory(Path.Combine(_root.FullName, "one"));
        var two = Directory.CreateDirectory(Path.Combine(one.FullName, "two"));

        await File.WriteAllTextAsync(Path.Combine(two.FullName, "L3.cs"),
            "public static class L3 { public static string V => \"L3\"; }");
        await File.WriteAllTextAsync(Path.Combine(two.FullName, "L2.cs"),
            "#!import L3.cs\npublic static class L2 { public static string V => \"L2>\" + L3.V; }");
        // Resolved beside L1, which is one level up from L2 and L3.
        await File.WriteAllTextAsync(Path.Combine(one.FullName, "L1.cs"),
            "#!import two/L2.cs\npublic static class L1 { public static string V => \"L1>\" + L2.V; }");

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import one/L1.cs",
            "L1.V");

        AssertProduced(outputs, "L1>L2>L3");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ImportInsideAnImportedNotebook_ResolvesBesideThatNotebook()
    {
        // The importing directory is set before either kind of import is dispatched, so a cell
        // inside an imported notebook reads its own neighbours too.
        var sub = Directory.CreateDirectory(Path.Combine(_root.FullName, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sub.FullName, "Helper.cs"),
            "public static class Helper { public static string V => \"helper\"; }");
        await File.WriteAllTextAsync(Path.Combine(sub.FullName, "inner.verso"),
            """
            {
              "verso": "1.0",
              "metadata": { "defaultKernel": "csharp" },
              "cells": [
                { "id": "a1000000-0000-0000-0000-000000000001", "type": "code",
                  "language": "csharp", "source": "#!import Helper.cs" }
              ]
            }
            """);

        var outputs = await RunAsync(
            Path.Combine(_root.FullName, "notebook.verso"),
            "#!import sub/inner.verso",
            "Helper.V");

        AssertProduced(outputs, "helper");
    }
}
