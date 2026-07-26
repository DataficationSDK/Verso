using System.Data;
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
    public async Task Inject_LeavesInterpreterOwnedNamesAlone()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        // Binding this one would take away every builtin the cell has. Verso's own session keys
        // share the prefix, and the magic command writes one of them to this very store.
        context.Variables.Set("__builtins__", "clobbered");
        context.Variables.Set("__verso_python_interpreter", "/usr/bin/python3");
        context.Variables.Set("usable", 5);

        var outputs = await session.ExecuteAsync("(len(__builtins__.__dict__) > 0, usable)", context);

        Assert.AreEqual("(True, 5)", outputs[^1].Content);
    }

    [TestMethod]
    public async Task Inject_KeepsASingleUnderscoreName()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        // Only a double underscore means the interpreter or Verso owns the name. A single one is a
        // convention Python applies to itself, and other languages use it for ordinary variables,
        // so a C# cell binding _rowCount is sharing a value rather than hiding one.
        context.Variables.Set("_rowCount", 42);

        var outputs = await session.ExecuteAsync("_rowCount", context);

        Assert.AreEqual("42", outputs[^1].Content);
    }

    [TestMethod]
    public async Task Inject_BindsAnExplanationForValuesJsonCannotDescribe()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("callback", new Action(() => { }));
        context.Variables.Set("usable", 5);

        // The name exists, so the cell is not left with a bare NameError beside a variables pane
        // that lists the variable. Printing it explains itself.
        var outputs = await session.ExecuteAsync("'callback' in dir()", context);

        Assert.IsTrue(outputs.Any(o => o.Content.Trim() == "True"));

        // The usable value is untouched by the cell, so it keeps the type it was stored with
        // rather than the one a JSON round trip would have left it as.
        Assert.AreEqual(5, context.Variables.Get<int>("usable"));
        Assert.IsTrue(
            outputs.Any(o => o.Content.Contains("callback was not shared")),
            "The cell should say which variable could not be shared and why.");
    }

    [TestMethod]
    public async Task Inject_ExplainsAnUnshareableValueOnlyOnce()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("callback", new Action(() => { }));

        await session.ExecuteAsync("1", context);
        var second = await session.ExecuteAsync("2", context);

        Assert.IsFalse(
            second.Any(o => o.Content.Contains("callback was not shared")),
            "A name whose answer has not changed should not be narrated on every cell.");
    }

    [TestMethod]
    public async Task Inject_UsingAnUnshareableValueRaisesWithTheReason()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("callback", new Action(() => { }));

        var outputs = await session.ExecuteAsync(
            "try:\n    callback.anything\nexcept Exception as e:\n    print(e)", context);

        Assert.IsTrue(
            outputs.Any(o => o.Content.Contains("callback") && o.Content.Contains("function")),
            "Using the value should raise an error naming the variable and the reason.");
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

    /// <summary>
    /// The shape that went missing: an array of records carrying a date. A C# record stands in for
    /// the F# one that was reported, because both reach Python by the same route, through their
    /// public properties.
    /// </summary>
    private sealed record Measurement(DateTime Time, double Price, double Volume);

    [TestMethod]
    public async Task Inject_CarriesRecordsWithDatesForFieldAccess()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("readings", new[]
        {
            new Measurement(new DateTime(2026, 4, 16, 8, 0, 0), 2.0, 100.0),
            new Measurement(new DateTime(2026, 4, 16, 8, 10, 30), 3.0, 10.0),
        });

        var outputs = await session.ExecuteAsync(
            "r = readings[1]\n"
            + "print(r.Time.Year, r.Time.Minute, r.Price, r['Volume'])", context);

        Assert.AreEqual("2026 10 3.0 10.0", outputs[^1].Content.Trim());
    }

    [TestMethod]
    public async Task Inject_CarriesADateAsARealDateTime()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("stamp", new DateTime(2026, 4, 16, 8, 0, 0, 250));

        var outputs = await session.ExecuteAsync(
            "import datetime\n"
            + "print(isinstance(stamp, datetime.datetime), stamp.Millisecond, stamp.microsecond)",
            context);

        // Millisecond is the .NET spelling and range, kept so cells written against the previous
        // kernel still run; microsecond is Python's own and both describe the same instant.
        Assert.AreEqual("True 250 250000", outputs[^1].Content.Trim());
    }

    [TestMethod]
    public async Task Inject_CarriesAQueryResultAsRows()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("When", typeof(DateTime));
        table.Rows.Add(1, new DateTime(2026, 4, 16, 8, 0, 0));
        table.Rows.Add(2, DBNull.Value);
        context.Variables.Set("lastSqlResult", table);

        var outputs = await session.ExecuteAsync(
            "print(len(lastSqlResult), lastSqlResult[0]['Id'], lastSqlResult[0].When.Year, "
            + "lastSqlResult[1]['When'])", context);

        Assert.AreEqual("2 1 2026 None", outputs[^1].Content.Trim());
    }

    [TestMethod]
    public async Task Publish_LeavesAnUntouchedValueAsItWasStored()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        context.Variables.Set("count", 7);

        // Python reports its whole scope, so a cell that never mentions the name still returns it.
        // Storing what came back would leave an int readable only as a long.
        await session.ExecuteAsync("unrelated = 1", context);

        Assert.AreEqual(7, context.Variables.Get<int>("count"));
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
    public async Task Publish_ExplainsTheLimitOncePerName()
    {
        await using var session = await StartedSessionAsync(
            new PythonKernelOptions { VariablePublishLimitBytes = 1024 });
        var context = new StubExecutionContext();

        await session.ExecuteAsync("blob = 'x' * 100000", context);

        // A different variable running into the same limit is its own news. Reporting only the
        // first one meant every later one went unmentioned for the rest of the session.
        var second = await session.ExecuteAsync("other = 'y' * 100000", context);
        Assert.IsTrue(second.Any(o => o.Content.Contains("other")));

        // The same variable again is not, so a cell that keeps referring to it stays readable.
        var third = await session.ExecuteAsync("blob", context);
        Assert.IsFalse(third.Any(o => o.Content.Contains("too large to share")));
    }
}
