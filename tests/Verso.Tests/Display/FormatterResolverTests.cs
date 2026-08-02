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
    public async Task TryFormatAsync_ContinuesWhenOutputHasDifferentRequiredMimeType()
    {
        var context = CreateContext(
            new TestFormatter("wrong", priority: 20, mimeType: "text/html"),
            new TestFormatter("svg", priority: 10, mimeType: "image/svg+xml"));

        var output = await FormatterResolver.TryFormatAsync(
            "value",
            context,
            includeFallback: true,
            requiredMimeType: "image/svg+xml");

        Assert.IsNotNull(output);
        Assert.AreEqual("image/svg+xml", output.MimeType);
        Assert.AreEqual("svg", output.Content);
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
        private readonly string _mimeType;

        public TestFormatter(
            string content,
            int priority = 10,
            bool isFallback = false,
            bool throwFromCanFormat = false,
            string mimeType = "text/plain")
        {
            _content = content;
            Priority = priority;
            IsFallback = isFallback;
            _throwFromCanFormat = throwFromCanFormat;
            _mimeType = mimeType;
        }

        public string ExtensionId => $"com.test.formatter.{_content}";
        public string Name => _content;
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(string) };
        public int Priority { get; }
        public bool IsFallback { get; }
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public bool CanFormat(object value, IFormatterContext context)
        {
            if (_throwFromCanFormat)
                throw new InvalidOperationException("probe failed");

            return value is string;
        }

        public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
            => Task.FromResult(new CellOutput(_mimeType, _content));
    }
}
