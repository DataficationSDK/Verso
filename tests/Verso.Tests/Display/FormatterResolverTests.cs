using Verso.Abstractions;
using Verso.Display;
using Verso.Stubs;
using Verso.Testing.Stubs;

namespace Verso.Tests.Display;

[TestClass]
public sealed class FormatterResolverTests
{
    [TestMethod]
    public async Task TryFormatAsync_HigherPriorityFormatterWins()
    {
        var context = CreateContext(
            new TestFormatter("low", priority: 10),
            new TestFormatter("high", priority: 20));

        var output = await FormatterResolver.TryFormatAsync("value", context, includeFallback: true);

        Assert.IsNotNull(output);
        Assert.AreEqual("high", output.Content);
    }

    [TestMethod]
    public async Task TryFormatAsync_ExcludesFallbackFormatterWhenRequested()
    {
        var context = CreateContext(new TestFormatter("fallback", isFallback: true));

        var output = await FormatterResolver.TryFormatAsync("value", context, includeFallback: false);

        Assert.IsNull(output);
    }

    [TestMethod]
    public async Task TryFormatAsync_IncludesFallbackFormatterForExplicitDisplay()
    {
        var context = CreateContext(new TestFormatter("fallback", isFallback: true));

        var output = await FormatterResolver.TryFormatAsync("value", context, includeFallback: true);

        Assert.IsNotNull(output);
        Assert.AreEqual("fallback", output.Content);
    }

    [TestMethod]
    public async Task TryFormatAsync_ContinuesWhenCanFormatThrows()
    {
        var context = CreateContext(
            new TestFormatter("broken", priority: 20, throwFromCanFormat: true),
            new TestFormatter("healthy", priority: 10));

        var output = await FormatterResolver.TryFormatAsync("value", context, includeFallback: true);

        Assert.IsNotNull(output);
        Assert.AreEqual("healthy", output.Content);
    }

    [TestMethod]
    public async Task TryFormatAsync_ContinuesWhenFormatAsyncThrows()
    {
        var context = CreateContext(
            new TestFormatter("broken", priority: 20, throwFromFormat: true),
            new TestFormatter("healthy", priority: 10));

        var output = await FormatterResolver.TryFormatAsync("value", context, includeFallback: true);

        Assert.IsNotNull(output);
        Assert.AreEqual("healthy", output.Content);
    }

    [TestMethod]
    public async Task TryFormatAsync_DoesNotRetryFormatterThatReturnsWrongMimeType()
    {
        var broken = new TestFormatter(
            "wrong",
            priority: 20,
            mimeType: "text/html",
            supportedMimeTypes: new[] { "image/svg+xml", "text/plain" });
        var healthy = new TestFormatter(
            "plain",
            priority: 10,
            mimeType: "text/plain",
            supportedMimeTypes: new[] { "text/plain" });
        var context = CreateContext(broken, healthy);

        var output = await FormatterResolver.TryFormatAsync(
            "value",
            context,
            includeFallback: true,
            acceptableMimeTypes: new[] { "image/svg+xml", "text/plain" });

        Assert.IsNotNull(output);
        Assert.AreEqual("text/plain", output.MimeType);
        Assert.AreEqual("plain", output.Content);
        Assert.AreEqual(1, broken.FormatCallCount);
        Assert.AreEqual(1, healthy.FormatCallCount);
    }

    [TestMethod]
    public async Task TryFormatAsync_PropagatesRequestedCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext(new TestFormatter("cancelled", throwCancellationFromFormat: true));
        context.CancellationToken = cts.Token;

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => FormatterResolver.TryFormatAsync("value", context, includeFallback: true));
    }

    [TestMethod]
    public async Task TryFormatAsync_CellOutputDoesNotBypassRegisteredFormatters()
    {
        var context = CreateContext();

        var output = await FormatterResolver.TryFormatAsync(
            CellOutput.Html("<strong>value</strong>"),
            context,
            includeFallback: false,
            acceptableMimeTypes: new[] { "image/png" });

        Assert.IsNull(output);
    }

    private static StubFormatterContext CreateContext(params IDataFormatter[] formatters)
    {
        return new StubFormatterContext
        {
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                getFormatters: () => formatters)
        };
    }

    private sealed class TestFormatter : IDataFormatter
    {
        private readonly string _content;
        private readonly bool _throwFromCanFormat;
        private readonly bool _throwFromFormat;
        private readonly bool _throwCancellationFromFormat;
        private readonly string _mimeType;
        private readonly HashSet<string>? _supportedMimeTypes;

        public TestFormatter(
            string content,
            int priority = 10,
            bool isFallback = false,
            bool throwFromCanFormat = false,
            bool throwFromFormat = false,
            bool throwCancellationFromFormat = false,
            string mimeType = "text/plain",
            IEnumerable<string>? supportedMimeTypes = null)
        {
            _content = content;
            Priority = priority;
            IsFallback = isFallback;
            _throwFromCanFormat = throwFromCanFormat;
            _throwFromFormat = throwFromFormat;
            _throwCancellationFromFormat = throwCancellationFromFormat;
            _mimeType = mimeType;
            _supportedMimeTypes = supportedMimeTypes is null
                ? null
                : new HashSet<string>(supportedMimeTypes, StringComparer.OrdinalIgnoreCase);
        }

        public string ExtensionId => $"com.test.formatter.{_content}";
        public string Name => _content;
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(string) };
        public int Priority { get; }
        public bool IsFallback { get; }
        public int FormatCallCount { get; private set; }
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public bool CanFormat(object value, IFormatterContext context)
        {
            if (_throwFromCanFormat)
                throw new InvalidOperationException("probe failed");

            return value is string &&
                   (_supportedMimeTypes is null || _supportedMimeTypes.Contains(context.MimeType));
        }

        public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
        {
            FormatCallCount++;

            if (_throwCancellationFromFormat)
                throw new OperationCanceledException(context.CancellationToken);
            if (_throwFromFormat)
                throw new InvalidOperationException("format failed");

            return Task.FromResult(new CellOutput(_mimeType, _content));
        }
    }
}
