using Verso.Abstractions;
using Verso.Extensions;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionValidationTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    // --- Missing / null ID ---

    [TestMethod]
    public void Validate_MissingId_ReturnsMissingIdError()
    {
        var ext = new StubKernel(extensionId: "", languageId: "test");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_ID"));
    }

    [TestMethod]
    public void Validate_WhitespaceId_ReturnsMissingIdError()
    {
        var ext = new StubKernel(extensionId: "   ", languageId: "ws");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_ID"));
    }

    // --- Duplicate ID ---

    [TestMethod]
    public async Task Validate_DuplicateId_ReturnsDuplicateIdError()
    {
        var first = new FakeLanguageKernel(languageId: "lang1");
        await _host.LoadExtensionAsync(first);

        var second = new StubKernel(extensionId: first.ExtensionId, languageId: "lang2");
        var errors = _host.ValidateExtension(second);

        Assert.IsTrue(errors.Any(e => e.ErrorCode == "DUPLICATE_ID"));
    }

    [TestMethod]
    public async Task Validate_DuplicateId_CaseInsensitive()
    {
        var first = new FakeLanguageKernel(languageId: "dup");
        await _host.LoadExtensionAsync(first);

        var second = new StubKernel(extensionId: first.ExtensionId.ToUpperInvariant(), languageId: "dup2");
        var errors = _host.ValidateExtension(second);

        Assert.IsTrue(errors.Any(e => e.ErrorCode == "DUPLICATE_ID"));
    }

    // --- Missing name ---

    [TestMethod]
    public void Validate_MissingName_ReturnsMissingNameError()
    {
        var ext = new StubKernel(extensionId: "com.test.noname", name: "", languageId: "nn");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_NAME"));
    }

    [TestMethod]
    public void Validate_WhitespaceName_ReturnsMissingNameError()
    {
        var ext = new StubKernel(extensionId: "com.test.wsname", name: "   ", languageId: "wn");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_NAME"));
    }

    // --- Invalid version formats ---

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_EmptyVersion_ReturnsMissingVersionError(string version)
    {
        var ext = new StubKernel(extensionId: "com.test.nover", version: version, languageId: "nv");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_VERSION"));
    }

    [TestMethod]
    [DataRow("1.0")]
    [DataRow("v1.0.0")]
    [DataRow("1")]
    [DataRow("abc")]
    [DataRow("1.0.0.0")]
    public void Validate_InvalidSemver_ReturnsInvalidVersionError(string version)
    {
        var ext = new StubKernel(extensionId: "com.test.badver", version: version, languageId: "bv");

        var errors = _host.ValidateExtension(ext);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "INVALID_VERSION"));
    }

    // --- Valid version formats ---

    [TestMethod]
    [DataRow("0.1.0")]
    [DataRow("1.0.0")]
    [DataRow("1.2.3-alpha")]
    [DataRow("1.2.3-alpha.1")]
    [DataRow("1.0.0+build123")]
    [DataRow("1.0.0-beta+build")]
    public void Validate_ValidSemver_NoVersionErrors(string version)
    {
        var ext = new StubKernel(extensionId: "com.test.goodver", version: version, languageId: "gv");

        var errors = _host.ValidateExtension(ext);
        Assert.IsFalse(errors.Any(e => e.ErrorCode is "MISSING_VERSION" or "INVALID_VERSION"));
    }

    // --- No capability interface ---

    [TestMethod]
    public void Validate_NoCapabilityInterface_ReturnsNoCapabilityError()
    {
        var bare = new FakeExtension(extensionId: "com.test.bare", name: "Bare", version: "1.0.0");

        var errors = _host.ValidateExtension(bare);
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "NO_CAPABILITY"));
    }

    [TestMethod]
    public void Validate_WithCapabilityInterface_NoCapabilityError()
    {
        var kernel = new FakeLanguageKernel(languageId: "cap");

        var errors = _host.ValidateExtension(kernel);
        Assert.IsFalse(errors.Any(e => e.ErrorCode == "NO_CAPABILITY"));
    }

    // --- Multiple errors reported together ---

    [TestMethod]
    public void Validate_MultipleIssues_ReportsAllErrors()
    {
        var bare = new FakeExtension(extensionId: "", name: "", version: "bad");

        var errors = _host.ValidateExtension(bare);
        Assert.IsTrue(errors.Count >= 3, $"Expected at least 3 errors, got {errors.Count}");
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_ID"));
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "MISSING_NAME"));
        Assert.IsTrue(errors.Any(e => e.ErrorCode == "NO_CAPABILITY"));
    }

    // --- ExtensionLoadException contains errors ---

    [TestMethod]
    public void LoadExtension_ValidationFailure_ThrowsExtensionLoadException()
    {
        var bare = new FakeExtension(extensionId: "", name: "", version: "bad");

        var ex = Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(bare)).Result;

        Assert.IsTrue(ex.Errors.Count >= 3);
        Assert.IsTrue(ex.Message.Contains("failed"));
    }

    // --- Helper: minimal ILanguageKernel with configurable metadata ---

    private sealed class StubKernel : ILanguageKernel
    {
        public StubKernel(
            string extensionId = "com.test.stub",
            string name = "Stub",
            string version = "1.0.0",
            string languageId = "stub")
        {
            ExtensionId = extensionId;
            Name = name;
            Version = version;
            LanguageId = languageId;
        }

        public string ExtensionId { get; }
        public string Name { get; }
        public string Version { get; }
        public string? Author => null;
        public string? Description => null;
        public string LanguageId { get; }
        public string DisplayName => Name;
        public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
            => Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());
        public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
            => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
            => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
        public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
            => Task.FromResult<HoverInfo?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
