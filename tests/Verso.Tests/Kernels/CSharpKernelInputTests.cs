using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
[DoNotParallelize] // Console.In is process-wide
public sealed class CSharpKernelInputTests
{
    private CSharpKernel _kernel = null!;
    private readonly List<(string Prompt, bool IsPassword)> _requests = new();

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new CSharpKernel();
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task ConsoleReadLine_ReturnsHostInput()
    {
        var context = CreateContext("Ada");

        var outputs = await _kernel.ExecuteAsync("Console.ReadLine()", context);

        Assert.IsFalse(outputs.Any(o => o.IsError), outputs.FirstOrDefault(o => o.IsError)?.Content);
        Assert.AreEqual("Ada", outputs[^1].Content);
    }

    [TestMethod]
    public async Task ConsoleReadLine_UsesPrecedingWriteAsPrompt_AndEchoesAnswer()
    {
        var context = CreateContext("Ada", "36");

        var outputs = await _kernel.ExecuteAsync(
            """
            Console.Write("Name: ");
            var name = Console.ReadLine();
            Console.WriteLine("How old are you?");
            var age = int.Parse(Console.ReadLine()!);
            Console.WriteLine($"{name} is {age}");
            """,
            context);

        Assert.IsFalse(outputs.Any(o => o.IsError), outputs.FirstOrDefault(o => o.IsError)?.Content);
        CollectionAssert.AreEqual(
            new[] { ("Name:", false), ("How old are you?", false) },
            _requests);

        var transcript = outputs[0].Content.ReplaceLineEndings("\n");
        Assert.AreEqual("Name: Ada\nHow old are you?\n36\nAda is 36\n", transcript);
    }

    [TestMethod]
    public async Task ConsoleRead_ReadsCharactersFromOneRequestedLine()
    {
        var context = CreateContext("hi");

        var outputs = await _kernel.ExecuteAsync(
            "$\"{(char)Console.Read()}{(char)Console.Read()}{Console.Read()}\"", context);

        Assert.AreEqual("hi10", outputs[^1].Content);
        Assert.AreEqual(1, _requests.Count);
    }

    [TestMethod]
    public async Task ConsoleReadLine_ReturnsNull_WhenPromptIsCancelled()
    {
        var outputs = await _kernel.ExecuteAsync("Console.ReadLine() is null", CreateContext());

        Assert.AreEqual("True", outputs[^1].Content);
    }

    [TestMethod]
    public async Task ConsoleReadLine_ReadsStandardInput_WhenHostHasNoInput()
    {
        var before = Console.In;
        Console.SetIn(new StringReader("piped\n"));
        try
        {
            var outputs = await _kernel.ExecuteAsync("Console.ReadLine()", CreateContext(answers: null));

            Assert.AreEqual("piped", outputs[^1].Content);
        }
        finally
        {
            Console.SetIn(before);
        }
    }

    [TestMethod]
    public async Task ConsoleIn_IsRestoredAfterExecution()
    {
        var before = Console.In;

        await _kernel.ExecuteAsync("Console.ReadLine()", CreateContext("x"));

        Assert.AreSame(before, Console.In);
    }

    [TestMethod]
    public async Task GetInputAsync_And_GetPasswordAsync_PassPromptAndMasking()
    {
        var context = CreateContext("Ada", "secret");

        var outputs = await _kernel.ExecuteAsync(
            """
            var user = await GetInputAsync("User:");
            var pass = await GetPasswordAsync("Password:");
            user + "/" + pass
            """,
            context);

        Assert.IsFalse(outputs.Any(o => o.IsError), outputs.FirstOrDefault(o => o.IsError)?.Content);
        Assert.AreEqual("Ada/secret", outputs[^1].Content);
        CollectionAssert.AreEqual(new[] { ("User:", false), ("Password:", true) }, _requests);
    }

    /// <param name="answers">Lines handed back in order, then <c>null</c> as a cancelled prompt would
    /// give. Pass <c>null</c> for a host with no interactive input at all.</param>
    private Verso.Contexts.ExecutionContext CreateContext(params string[]? answers)
    {
        var variables = new VariableStore();
        var theme = new StubThemeContext();
        var extensionHost = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        var metadata = new NotebookMetadataContext(new NotebookModel());

        Func<string, bool, CancellationToken, Task<string?>>? requestInput = null;
        if (answers is not null)
        {
            var queue = new Queue<string>(answers);
            requestInput = (prompt, isPassword, _) =>
            {
                _requests.Add((prompt, isPassword));
                return Task.FromResult(queue.TryDequeue(out var answer) ? answer : null);
            };
        }

        return new Verso.Contexts.ExecutionContext(
            Guid.NewGuid(), 1, variables, CancellationToken.None,
            theme, LayoutCapabilities.None, extensionHost, metadata,
            new Verso.Stubs.StubNotebookOperations(),
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask,
            requestInput: requestInput);
    }
}
