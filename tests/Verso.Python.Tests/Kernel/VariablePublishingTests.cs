using Verso.Python.Kernel;
using Verso.Python.Tests.Host;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

/// <summary>
/// Regression coverage for publishing Python variables into the cross-language store. Self-
/// referential or very deeply nested containers must not recurse without bound during
/// conversion, because a stack overflow is a fatal, uncatchable .NET exception that would take
/// down the whole kernel host rather than failing a single cell.
/// </summary>
[TestClass]
public sealed class VariablePublishingTests
{
    /// <summary>
    /// Both runtimes publish variables. The out-of-process one needs an interpreter to publish
    /// from, so a machine without Python skips rather than fails.
    /// </summary>
    private static void RequireRuntime()
    {
        if (EmbeddedRuntimeOnly.HostProcessEnabled)
            PythonProbe.Require();
    }

    [TestMethod]
    public async Task Publish_SelfReferentialGlobals_DoesNotCrash()
    {
        RequireRuntime();

        var kernel = new PythonKernel();
        await kernel.InitializeAsync();
        var context = new StubExecutionContext();

        // globals() contains a reference to itself; publishing it used to recurse forever.
        var outputs = await kernel.ExecuteAsync("g = globals()", context);

        Assert.IsFalse(outputs.Any(o => o.IsError));

        await kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task Publish_CyclicContainers_DoNotCrash_AndNormalDataStillPublishes()
    {
        RequireRuntime();

        var kernel = new PythonKernel();
        await kernel.InitializeAsync();
        var context = new StubExecutionContext();

        var code =
            "selfdict = {}\n" +
            "selfdict['self'] = selfdict\n" +
            "cyclist = [1, 2]\n" +
            "cyclist.append(cyclist)\n" +
            "nums = [10, 20, 30]\n" +
            "info = {'name': 'verso', 'tags': ['a', 'b']}";
        var outputs = await kernel.ExecuteAsync(code, context);

        Assert.IsFalse(outputs.Any(o => o.IsError));

        // Cycles are pruned, but non-cyclic data published in the same cell is unaffected.
        Assert.IsTrue(context.Variables.TryGet<List<object>>("nums", out var nums));
        Assert.AreEqual(3, nums!.Count);
        Assert.IsTrue(context.Variables.TryGet<Dictionary<string, object>>("info", out var info));
        Assert.AreEqual("verso", info!["name"]);

        await kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task Publish_DeeplyNestedAcyclicList_DoesNotCrash()
    {
        RequireRuntime();

        var kernel = new PythonKernel();
        await kernel.InitializeAsync();
        var context = new StubExecutionContext();

        // Deep but acyclic: every node is distinct, so only the depth backstop bounds it.
        var code =
            "deep = cur = []\n" +
            "for _n in range(2000):\n" +
            "    nxt = []\n" +
            "    cur.append(nxt)\n" +
            "    cur = nxt";
        var outputs = await kernel.ExecuteAsync(code, context);

        Assert.IsFalse(outputs.Any(o => o.IsError));

        await kernel.DisposeAsync();
    }
}
