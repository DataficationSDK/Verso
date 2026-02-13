using System.Reflection;
using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Extensions;

/// <summary>
/// Discovers, validates, loads, and manages the lifecycle of all Verso extensions.
/// Implements <see cref="IExtensionHostContext"/> for read-only querying and
/// <see cref="IAsyncDisposable"/> for teardown.
/// </summary>
public sealed class ExtensionHost : IExtensionHostContext, IAsyncDisposable
{
    private static readonly Regex SemVerRegex = new(
        @"^\d+\.\d+\.\d+(-[a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*)?(\+[a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*)?$",
        RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly List<IExtension> _extensions = new();
    private readonly List<ILanguageKernel> _kernels = new();
    private readonly List<ICellRenderer> _renderers = new();
    private readonly List<IDataFormatter> _formatters = new();
    private readonly List<ICellType> _cellTypes = new();
    private readonly List<INotebookSerializer> _serializers = new();
    private readonly List<ITheme> _themes = new();
    private readonly List<ILayoutEngine> _layouts = new();
    private readonly List<IToolbarAction> _toolbarActions = new();
    private readonly List<IMagicCommand> _magicCommands = new();
    private readonly List<ExtensionLoadContext> _loadContexts = new();
    private bool _disposed;

    // --- IExtensionHostContext ---

    public IReadOnlyList<IExtension> GetLoadedExtensions()
    {
        lock (_lock) { return _extensions.ToList(); }
    }

    public IReadOnlyList<ILanguageKernel> GetKernels()
    {
        lock (_lock) { return _kernels.ToList(); }
    }

    public IReadOnlyList<ICellRenderer> GetRenderers()
    {
        lock (_lock) { return _renderers.ToList(); }
    }

    public IReadOnlyList<IDataFormatter> GetFormatters()
    {
        lock (_lock) { return _formatters.ToList(); }
    }

    public IReadOnlyList<ICellType> GetCellTypes()
    {
        lock (_lock) { return _cellTypes.ToList(); }
    }

    public IReadOnlyList<INotebookSerializer> GetSerializers()
    {
        lock (_lock) { return _serializers.ToList(); }
    }

    // --- Additional typed queries ---

    public IReadOnlyList<ITheme> GetThemes()
    {
        lock (_lock) { return _themes.ToList(); }
    }

    public IReadOnlyList<ILayoutEngine> GetLayouts()
    {
        lock (_lock) { return _layouts.ToList(); }
    }

    public IReadOnlyList<IToolbarAction> GetToolbarActions()
    {
        lock (_lock) { return _toolbarActions.ToList(); }
    }

    public IReadOnlyList<IMagicCommand> GetMagicCommands()
    {
        lock (_lock) { return _magicCommands.ToList(); }
    }

    // --- Discovery & Loading ---

    /// <summary>
    /// Scans the Verso assembly for types marked with <see cref="VersoExtensionAttribute"/>,
    /// instantiates them, validates, and auto-registers by capability interface.
    /// </summary>
    public async Task LoadBuiltInExtensionsAsync()
    {
        ThrowIfDisposed();

        var assembly = typeof(ExtensionHost).Assembly;
        var extensionTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.GetCustomAttribute<VersoExtensionAttribute>() is not null &&
                        typeof(IExtension).IsAssignableFrom(t));

        foreach (var type in extensionTypes)
        {
            var extension = (IExtension)Activator.CreateInstance(type)!;
            await LoadExtensionAsync(extension).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Scans all .dll files in the specified directory, loading each in an isolated context.
    /// </summary>
    /// <param name="path">Directory containing extension assemblies.</param>
    public async Task LoadFromDirectoryAsync(string path)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Extension directory not found: {path}");

        foreach (var dll in Directory.GetFiles(path, "*.dll"))
        {
            await LoadFromAssemblyAsync(dll).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads extensions from a single assembly using an isolated <see cref="ExtensionLoadContext"/>.
    /// </summary>
    /// <param name="path">Full path to the extension assembly (.dll).</param>
    public async Task LoadFromAssemblyAsync(string path)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Extension assembly not found: {path}", path);

        var loadContext = new ExtensionLoadContext(path);
        lock (_lock) { _loadContexts.Add(loadContext); }

        var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(path));
        var extensionTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.GetCustomAttribute<VersoExtensionAttribute>() is not null &&
                        typeof(IExtension).IsAssignableFrom(t));

        foreach (var type in extensionTypes)
        {
            var extension = (IExtension)Activator.CreateInstance(type)!;
            await LoadExtensionAsync(extension).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads a single extension instance. Validates, calls <see cref="IExtension.OnLoadedAsync"/>,
    /// and auto-registers by implemented capability interfaces.
    /// </summary>
    /// <param name="extension">The extension instance to load.</param>
    public async Task LoadExtensionAsync(IExtension extension)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(extension);

        var errors = ValidateExtension(extension);
        if (errors.Count > 0)
            throw new ExtensionLoadException(errors);

        await extension.OnLoadedAsync(this).ConfigureAwait(false);

        lock (_lock)
        {
            _extensions.Add(extension);
            AutoRegister(extension);
        }
    }

    /// <summary>
    /// Validates an extension against all rules. Returns an empty list if valid.
    /// </summary>
    public IReadOnlyList<ExtensionValidationError> ValidateExtension(IExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var errors = new List<ExtensionValidationError>();
        var id = extension.ExtensionId;

        // ID checks
        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add(new ExtensionValidationError(null, "MISSING_ID",
                "Extension ID is required and cannot be empty."));
        }
        else
        {
            lock (_lock)
            {
                if (_extensions.Any(e => string.Equals(e.ExtensionId, id, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(new ExtensionValidationError(id, "DUPLICATE_ID",
                        $"An extension with ID '{id}' is already loaded."));
                }
            }
        }

        // Name check
        if (string.IsNullOrWhiteSpace(extension.Name))
        {
            errors.Add(new ExtensionValidationError(id, "MISSING_NAME",
                "Extension name is required and cannot be empty."));
        }

        // Version check
        if (string.IsNullOrWhiteSpace(extension.Version))
        {
            errors.Add(new ExtensionValidationError(id, "MISSING_VERSION",
                "Extension version is required."));
        }
        else if (!SemVerRegex.IsMatch(extension.Version))
        {
            errors.Add(new ExtensionValidationError(id, "INVALID_VERSION",
                $"Extension version '{extension.Version}' is not valid semver."));
        }

        // Capability interface check — must implement at least one beyond IExtension
        if (!HasCapabilityInterface(extension))
        {
            errors.Add(new ExtensionValidationError(id, "NO_CAPABILITY",
                "Extension must implement at least one capability interface " +
                "(e.g. ILanguageKernel, ICellRenderer, IDataFormatter)."));
        }

        return errors;
    }

    // --- Unload / Dispose ---

    /// <summary>
    /// Unloads all extensions in reverse load order: calls <see cref="IExtension.OnUnloadedAsync"/>,
    /// disposes <see cref="IAsyncDisposable"/> extensions, then unloads isolated contexts.
    /// </summary>
    public async Task UnloadAllAsync()
    {
        List<IExtension> snapshot;
        lock (_lock) { snapshot = _extensions.ToList(); }

        // Reverse order teardown
        for (int i = snapshot.Count - 1; i >= 0; i--)
        {
            var ext = snapshot[i];
            await ext.OnUnloadedAsync().ConfigureAwait(false);

            if (ext is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (ext is IDisposable disposable)
                disposable.Dispose();
        }

        lock (_lock)
        {
            _extensions.Clear();
            _kernels.Clear();
            _renderers.Clear();
            _formatters.Clear();
            _cellTypes.Clear();
            _serializers.Clear();
            _themes.Clear();
            _layouts.Clear();
            _toolbarActions.Clear();
            _magicCommands.Clear();

            foreach (var ctx in _loadContexts)
                ctx.Unload();
            _loadContexts.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await UnloadAllAsync().ConfigureAwait(false);
    }

    // --- Private helpers ---

    private void AutoRegister(IExtension extension)
    {
        if (extension is ILanguageKernel kernel)
            _kernels.Add(kernel);
        if (extension is ICellRenderer renderer)
            _renderers.Add(renderer);
        if (extension is IDataFormatter formatter)
            _formatters.Add(formatter);
        if (extension is ICellType cellType)
            _cellTypes.Add(cellType);
        if (extension is INotebookSerializer serializer)
            _serializers.Add(serializer);
        if (extension is ITheme theme)
            _themes.Add(theme);
        if (extension is ILayoutEngine layout)
            _layouts.Add(layout);
        if (extension is IToolbarAction action)
            _toolbarActions.Add(action);
        if (extension is IMagicCommand magic)
            _magicCommands.Add(magic);
    }

    private static bool HasCapabilityInterface(IExtension extension)
    {
        return extension is ILanguageKernel
            or ICellRenderer
            or IDataFormatter
            or ICellType
            or INotebookSerializer
            or ITheme
            or ILayoutEngine
            or IToolbarAction
            or IMagicCommand;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExtensionHost));
    }
}
