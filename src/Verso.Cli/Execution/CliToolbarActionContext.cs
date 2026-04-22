using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Cli.Execution;

/// <summary>
/// Headless <see cref="IToolbarActionContext"/> used by <c>verso export</c> to invoke
/// an export-menu toolbar action and write its output bytes to disk.
/// </summary>
internal sealed class CliToolbarActionContext : IToolbarActionContext
{
    private readonly string? _outputPath;

    public CliToolbarActionContext(
        IReadOnlyList<CellModel> cells,
        INotebookMetadata notebookMetadata,
        IExtensionHostContext extensionHost,
        ITheme? selectedTheme,
        string? activeLayoutId,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        NotebookCells = cells ?? throw new ArgumentNullException(nameof(cells));
        NotebookMetadata = notebookMetadata ?? throw new ArgumentNullException(nameof(notebookMetadata));
        if (extensionHost is null) throw new ArgumentNullException(nameof(extensionHost));

        // When the user picks a theme, restrict GetThemes() to just that theme so
        // export actions (which resolve by ThemeKind via FirstOrDefault) pick it
        // unambiguously even if other themes share the same kind.
        ExtensionHost = selectedTheme is null
            ? extensionHost
            : new FilteredExtensionHostContext(extensionHost, selectedTheme);

        Theme = new CliThemeContext(selectedTheme?.ThemeKind ?? ThemeKind.Light);
        ActiveLayoutId = activeLayoutId;
        CancellationToken = cancellationToken;
        _outputPath = outputPath;
    }

    public IReadOnlyList<Guid> SelectedCellIds { get; } = Array.Empty<Guid>();
    public IReadOnlyList<CellModel> NotebookCells { get; }
    public string? ActiveKernelId => NotebookMetadata.DefaultKernelId;

    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; }
    public IThemeContext Theme { get; }
    public LayoutCapabilities LayoutCapabilities => LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; }
    public INotebookMetadata NotebookMetadata { get; }
    public INotebookOperations Notebook { get; } = new StubNotebookOperations();
    public string? ActiveLayoutId { get; }

    public string? WrittenPath { get; private set; }

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

    public async Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
    {
        var target = _outputPath is not null
            ? Path.GetFullPath(_outputPath)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, fileName));

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(target, data, CancellationToken).ConfigureAwait(false);
        WrittenPath = target;
    }

    public Task UpdateOutputAsync(string outputBlockId, CellOutput output) => Task.CompletedTask;

    private sealed class FilteredExtensionHostContext : IExtensionHostContext
    {
        private readonly IExtensionHostContext _inner;
        private readonly IReadOnlyList<ITheme> _themes;

        public FilteredExtensionHostContext(IExtensionHostContext inner, ITheme theme)
        {
            _inner = inner;
            _themes = new[] { theme };
        }

        public IReadOnlyList<IExtension> GetLoadedExtensions() => _inner.GetLoadedExtensions();
        public IReadOnlyList<ILanguageKernel> GetKernels() => _inner.GetKernels();
        public IReadOnlyList<ICellRenderer> GetRenderers() => _inner.GetRenderers();
        public IReadOnlyList<IDataFormatter> GetFormatters() => _inner.GetFormatters();
        public IReadOnlyList<ICellType> GetCellTypes() => _inner.GetCellTypes();
        public IReadOnlyList<INotebookSerializer> GetSerializers() => _inner.GetSerializers();
        public IReadOnlyList<ILayoutEngine> GetLayouts() => _inner.GetLayouts();
        public IReadOnlyList<ITheme> GetThemes() => _themes;
        public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => _inner.GetPostProcessors();
        public IReadOnlyList<ICellPropertyProvider> GetPropertyProviders() => _inner.GetPropertyProviders();
        public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => _inner.GetExtensionInfos();
        public Task EnableExtensionAsync(string extensionId) => _inner.EnableExtensionAsync(extensionId);
        public Task DisableExtensionAsync(string extensionId) => _inner.DisableExtensionAsync(extensionId);
    }

    private sealed class CliThemeContext : IThemeContext
    {
        private readonly StubThemeContext _inner = new();

        public CliThemeContext(ThemeKind themeKind) => ThemeKind = themeKind;

        public ThemeKind ThemeKind { get; }

        public string GetColor(string tokenName) => _inner.GetColor(tokenName);
        public FontDescriptor GetFont(string fontRole) => _inner.GetFont(fontRole);
        public double GetSpacing(string spacingName) => _inner.GetSpacing(spacingName);
        public string? GetSyntaxColor(string tokenType) => _inner.GetSyntaxColor(tokenType);
        public string? GetCustomToken(string key) => _inner.GetCustomToken(key);
    }
}
