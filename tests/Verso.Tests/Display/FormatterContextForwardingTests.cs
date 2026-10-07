using System.Globalization;
using Verso.Display;
using Verso.Execution;
using Verso.Testing.Stubs;

namespace Verso.Tests.Display;


[TestClass]
public sealed class FormatterContextForwardingTests
{
    [TestMethod]
    public async Task DisplayFormatterContext_ForwardsEveryOptionalMember()
    {
        await using var channels = new OutputChannelHost();
        var inner = new RichExecutionContext(channels);

        await AssertForwards(new DisplayFormatterContext(inner), inner);
    }

    [TestMethod]
    public async Task HintedFormatterContext_ForwardsEveryOptionalMember()
    {
        await using var channels = new OutputChannelHost();
        var inner = new RichExecutionContext(channels);

        var hinted = new HintedFormatterContext(new DisplayFormatterContext(inner), "image/png");

        Assert.AreEqual("image/png", hinted.MimeType);
        await AssertForwards(hinted, inner);
    }

    // A context compiled against the interface before CellId existed reports no cell => static output
    [TestMethod]
    public void FormatterContext_CellId_IsNullByDefault()
    {
        IFormatterContext context = new MinimalFormatterContext();
        Assert.IsNull(context.CellId);
    }

    private static async Task AssertForwards(IFormatterContext context, RichExecutionContext inner)
    {
        Assert.AreEqual(inner.CellId, context.CellId);
        Assert.AreSame(inner.OutputChannels, context.OutputChannels);
        Assert.AreEqual(inner.ActiveLayoutId, context.ActiveLayoutId);
        Assert.AreSame(inner.CollapsedSections, context.CollapsedSections);
        Assert.AreEqual(inner.UICulture, context.UICulture);

        var output = CellOutput.Plain("updated");
        await context.UpdateOutputAsync("block-1", output);
        Assert.AreEqual(1, inner.UpdatedOutputs.Count);
        Assert.AreEqual("block-1", inner.UpdatedOutputs[0].OutputBlockId);
        Assert.AreSame(output, inner.UpdatedOutputs[0].Output);

        await context.RequestFileDownloadAsync("a.csv", "text/csv", new byte[] { 1 });
        Assert.AreEqual("a.csv", inner.DownloadedFile);
    }

    // Test class, which implements every member of IExecutionContext, so forwarding can actually be tested
    private sealed class RichExecutionContext : IExecutionContext
    {
        private readonly StubExecutionContext _stub = new();

        public RichExecutionContext(IOutputChannelHost channels) => OutputChannels = channels;

        public Guid CellId { get; } = Guid.NewGuid();
        public int ExecutionCount => 1;
        public IOutputChannelHost? OutputChannels { get; }
        public string? ActiveLayoutId => "com.test.layout";
        public IReadOnlySet<Guid> CollapsedSections { get; } = new HashSet<Guid> { Guid.NewGuid() };
        public CultureInfo UICulture { get; } = CultureInfo.GetCultureInfo("ja");

        public List<(string OutputBlockId, CellOutput Output)> UpdatedOutputs { get; } = new();
        public string? DownloadedFile { get; private set; }

        public Task UpdateOutputAsync(string outputBlockId, CellOutput output)
        {
            UpdatedOutputs.Add((outputBlockId, output));
            return Task.CompletedTask;
        }

        public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
        {
            DownloadedFile = fileName;
            return Task.CompletedTask;
        }

        public IVariableStore Variables => _stub.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _stub.Theme;
        public LayoutCapabilities LayoutCapabilities => LayoutCapabilities.None;
        public IExtensionHostContext ExtensionHost => _stub.ExtensionHost;
        public INotebookMetadata NotebookMetadata => _stub.NotebookMetadata;
        public INotebookOperations Notebook => _stub.Notebook;
        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;
        public Task DisplayAsync(CellOutput output) => Task.CompletedTask;
    }

    /// Minimal implementation of IFormatterContext
    private sealed class MinimalFormatterContext : IFormatterContext
    {
        private readonly StubExecutionContext _stub = new();

        public string MimeType => "text/html";
        public double MaxWidth => 800;
        public double MaxHeight => 600;
        public IVariableStore Variables => _stub.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _stub.Theme;
        public LayoutCapabilities LayoutCapabilities => LayoutCapabilities.None;
        public IExtensionHostContext ExtensionHost => _stub.ExtensionHost;
        public INotebookMetadata NotebookMetadata => _stub.NotebookMetadata;
        public INotebookOperations Notebook => _stub.Notebook;
        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;
    }
}
