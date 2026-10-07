using Verso.Abstractions;
using Verso.Execution;
using Verso.FSharp.Kernel;
using Verso.Stubs;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;


[TestClass]
public sealed class FormatterContextTests
{
    private FSharpKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task ResultValue_FormatterSeesTheCellAndTheOutputChannels()
    {
        await using var channels = new OutputChannelHost();
        var formatter = new CapturingFormatter();
        var context = new StubExecutionContext
        {
            OutputChannels = channels,
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                getFormatters: () => new IDataFormatter[] { formatter }),
        };

        var outputs = await _kernel.ExecuteAsync("System.Version(1, 2)", context);

        Assert.IsNotNull(formatter.Captured, "The formatter was never asked to format the value.");
        Assert.AreEqual(context.CellId, formatter.Captured.CellId);
        Assert.AreSame(channels, formatter.Captured.OutputChannels);
        Assert.IsTrue(outputs.Any(o => o.Content == "captured"));
    }

    // A context with no output channels should result in the formatter seeing none
    [TestMethod]
    public async Task ResultValue_WithoutChannels_FormatterSeesNone()
    {
        var formatter = new CapturingFormatter();
        var context = new StubExecutionContext
        {
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                getFormatters: () => new IDataFormatter[] { formatter }),
        };

        await _kernel.ExecuteAsync("System.Version(1, 2)", context);

        Assert.IsNotNull(formatter.Captured);
        Assert.IsNull(formatter.Captured.OutputChannels);
        Assert.AreEqual(context.CellId, formatter.Captured.CellId);
    }

    private sealed class CapturingFormatter : IDataFormatter
    {
        public IFormatterContext? Captured { get; private set; }

        public string ExtensionId => "com.test.fsharp.capturing-formatter";
        public string Name => "Capturing Formatter";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(Version) };
        public int Priority => 100;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public bool CanFormat(object value, IFormatterContext context) => value is Version;

        public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
        {
            Captured = context;
            return Task.FromResult(CellOutput.Plain("captured"));
        }
    }
}
