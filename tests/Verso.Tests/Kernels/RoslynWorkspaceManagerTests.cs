using Microsoft.CodeAnalysis;
using Verso.Kernels;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class RoslynWorkspaceManagerTests
{
    private static RoslynWorkspaceManager CreateManager() =>
        new(new[] { "System" }, new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

    [TestMethod]
    public void BuildDocument_SameText_SharesOneDocument()
    {
        using var manager = CreateManager();

        var first = manager.BuildDocument("var x = 1;");
        var second = manager.BuildDocument("var x = 1;");

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void BuildDocument_DifferentText_BuildsAgain()
    {
        using var manager = CreateManager();

        var first = manager.BuildDocument("var x = 1;");
        var second = manager.BuildDocument("var x = 2;");

        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void BuildDocument_AfterRun_BuildsAgain()
    {
        using var manager = CreateManager();

        var before = manager.BuildDocument("var y = x;");
        manager.AppendExecutedCode("var x = 1;");
        var after = manager.BuildDocument("var y = x;");

        Assert.AreNotSame(before, after);
    }

    [TestMethod]
    public void BuildDocument_AfterReference_BuildsAgain()
    {
        using var manager = CreateManager();

        var before = manager.BuildDocument("var x = 1;");
        manager.AddReferences(new[] { typeof(Enumerable).Assembly.Location });
        var after = manager.BuildDocument("var x = 1;");

        Assert.AreNotSame(before, after);
    }
}
