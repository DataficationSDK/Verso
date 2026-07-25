using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Exercises variable sharing against a real Python subprocess: values reaching the scope before a
/// cell runs, values coming back into the shared store afterwards, and the rules that keep the
/// exchange from overwriting someone else's work or moving more data than it should.
/// </summary>
[TestClass]
public sealed class VariableExchangeTests
{
    private static PythonHostSession CreateSession(PythonKernelOptions? options = null)
    {
        var python = PythonProbe.Require();
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        options = (options ?? new PythonKernelOptions()) with { PythonExecutable = python };
        return new PythonHostSession(options, locator: locator);
    }

    private static async Task<PythonHostSession> StartedSessionAsync(PythonKernelOptions? options = null)
    {
        var session = CreateSession(options);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    // --- publish ---

    [TestMethod]
    public async Task Publish_PutsScalarsInTheStore()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await session.ExecuteAsync("count = 6\nname = 'verso'\nratio = 1.5\nready = True", context);

        Assert.AreEqual(6L, context.Variables.Get<object>("count"));
        Assert.AreEqual("verso", context.Variables.Get<object>("name"));
        Assert.AreEqual(1.5d, context.Variables.Get<object>("ratio"));
        Assert.AreEqual(true, context.Variables.Get<object>("ready"));
    }

    [TestMethod]
    public async Task Publish_PreservesNestedContainers()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await session.ExecuteAsync(
            "cities = [{'name': 'Tokyo', 'population': 13960000}]", context);

        Assert.IsTrue(context.Variables.TryGet<List<object>>("cities", out var cities));
        Assert.AreEqual(1, cities!.Count);

        var first = (Dictionary<string, object>)cities[0]!;
        Assert.AreEqual("Tokyo", first["name"]);
        Assert.AreEqual(13960000L, first["population"]);
    }

    [TestMethod]
    public async Task Publish_SkipsPrivateNamesModulesAndCallables()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await session.ExecuteAsync(
            "import json\n_private = 1\ndef helper():\n    return 2\nshown = 3", context);

        Assert.AreEqual(3L, context.Variables.Get<object>("shown"));
        Assert.IsFalse(context.Variables.TryGet<object>("_private", out _));
        Assert.IsFalse(context.Variables.TryGet<object>("json", out _));
        Assert.IsFalse(context.Variables.TryGet<object>("helper", out _));
    }

    [TestMethod]
    public async Task Publish_HappensEvenWhenTheCellFails()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        // The assignment ran, so the value is real and belongs in the store.
        await session.ExecuteAsync("partial = 7\nraise ValueError('stop')", context);

        Assert.AreEqual(7L, context.Variables.Get<object>("partial"));
    }

    [TestMethod]
    public async Task Publish_CanBeTurnedOff()
    {
        await using var session = await StartedSessionAsync(
            new PythonKernelOptions { PublishVariables = false });
        var context = new StubExecutionContext();

        await session.ExecuteAsync("secret = 1", context);

        Assert.IsFalse(context.Variables.TryGet<object>("secret", out _));
    }

    // --- inject ---

    [TestMethod]
    public async Task Inject_MakesStoreValuesAvailableToTheCell()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("greeting", "Hello from C#!");
        context.Variables.Set("magic_number", 42);

        var outputs = await session.ExecuteAsync("f'{greeting} {magic_number}'", context);

        Assert.AreEqual("'Hello from C#! 42'", outputs[^1].Content);
    }

    [TestMethod]
    public async Task Inject_CarriesNestedContainers()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("rows", new List<object>
        {
            new Dictionary<string, object> { ["city"] = "Paris", ["population"] = 2161000L },
        });

        var outputs = await session.ExecuteAsync("rows[0]['city']", context);

        Assert.AreEqual("'Paris'", outputs[^1].Content);
    }

    [TestMethod]
    public async Task Inject_SkipsValuesJsonCannotDescribe()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("callback", new Action(() => { }));
        context.Variables.Set("usable", 5);

        var outputs = await session.ExecuteAsync("'callback' in dir()", context);

        Assert.AreEqual("False", outputs[^1].Content);
        Assert.AreEqual(5L, context.Variables.Get<object>("usable"));
    }

    [TestMethod]
    public async Task Inject_DoesNotResendAnUnchangedValue()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("shared", 1);

        // The cell rebinds the name. Re-injecting the unchanged store value on the next run
        // would silently undo that, which is the behaviour the content hash prevents.
        await session.ExecuteAsync("shared = shared + 100", context);
        var outputs = await session.ExecuteAsync("shared", context);

        Assert.AreEqual("101", outputs[^1].Content);
    }

    [TestMethod]
    public async Task Inject_ResendsAValueThatChanged()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("shared", 1);

        await session.ExecuteAsync("shared", context);
        context.Variables.Set("shared", 2);
        var outputs = await session.ExecuteAsync("shared", context);

        Assert.AreEqual("2", outputs[^1].Content);
    }

    // --- protection and limits ---

    [TestMethod]
    public async Task Publish_LeavesAValueAnotherWriterChangedDuringTheRun()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("contended", 1);

        // Written by something other than this cell while it was running.
        var code = "import time\ncontended = 2\ntime.sleep(0.5)";
        var execution = session.ExecuteAsync(code, context);
        await Task.Delay(150);
        context.Variables.Set("contended", 99);
        await execution;

        Assert.AreEqual(99, context.Variables.Get<object>("contended"));
    }

    [TestMethod]
    public async Task Publish_DescribesAValueTooLargeToShare()
    {
        await using var session = await StartedSessionAsync(
            new PythonKernelOptions { VariablePublishLimitBytes = 1024 });
        var context = new StubExecutionContext();

        var outputs = await session.ExecuteAsync("blob = 'x' * 100000", context);

        var shared = context.Variables.Get<object>("blob") as string;
        Assert.IsNotNull(shared);
        StringAssert.Contains(shared!, "not shared");
        Assert.IsTrue(
            outputs.Any(o => o.Content.Contains("too large to share")),
            "The cell should explain once why a variable was not shared.");
    }

    [TestMethod]
    public async Task Publish_ExplainsTheLimitOnlyOnce()
    {
        await using var session = await StartedSessionAsync(
            new PythonKernelOptions { VariablePublishLimitBytes = 1024 });
        var context = new StubExecutionContext();

        await session.ExecuteAsync("blob = 'x' * 100000", context);
        var second = await session.ExecuteAsync("other = 'y' * 100000", context);

        Assert.IsFalse(second.Any(o => o.Content.Contains("too large to share")));
    }
}
