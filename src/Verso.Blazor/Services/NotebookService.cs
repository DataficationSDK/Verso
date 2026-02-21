using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;
using Verso.Serializers;

namespace Verso.Blazor.Services;

/// <summary>
/// Scoped service that manages the Scaffold and ExtensionHost lifecycle for a Blazor circuit.
/// Single integration point between Blazor components and the Verso engine.
/// </summary>
public sealed class NotebookService : IAsyncDisposable
{
    private Scaffold? _scaffold;
    private ExtensionHost? _extensionHost;
    private string? _filePath;

    public Scaffold? Scaffold => _scaffold;
    public ExtensionHost? ExtensionHost => _extensionHost;
    public bool IsLoaded => _scaffold is not null;
    public string? FilePath => _filePath;

    /// <summary>Raised after a cell finishes execution.</summary>
    public event Action? OnCellExecuted;

    /// <summary>Raised when the notebook structure changes (add, remove, move, new, open).</summary>
    public event Action? OnNotebookChanged;

    /// <summary>Raised when the active layout changes.</summary>
    public event Action? OnLayoutChanged;

    /// <summary>Raised when the active theme changes.</summary>
    public event Action? OnThemeChanged;

    /// <summary>Raised when an extension is enabled or disabled.</summary>
    public event Action? OnExtensionStatusChanged;

    /// <summary>Raised when variables in the store change.</summary>
    public event Action? OnVariablesChanged;

    /// <summary>Raised when an extension setting changes.</summary>
    public event Action? OnSettingsChanged;

    /// <summary>Open and deserialize a notebook file (.verso, .ipynb, etc.).</summary>
    public async Task OpenAsync(string filePath)
    {
        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        var content = await File.ReadAllTextAsync(filePath);

        // Select the right serializer based on file extension
        var serializer = _extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(filePath))
            ?? (INotebookSerializer)new VersoSerializer();

        var notebook = await serializer.DeserializeAsync(content);

        _scaffold = new Scaffold(notebook, _extensionHost, filePath);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        await RestoreLayoutMetadataAsync();
        await RestoreSettingsAsync();
        _filePath = filePath;
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    /// <summary>Open a notebook from in-memory content (e.g. from a file browser upload).</summary>
    public async Task OpenFromContentAsync(string fileName, string content)
    {
        // Blazor Server runs locally — try to find the actual file on disk so that
        // relative imports (e.g. #!import ./helpers.verso) resolve correctly.
        var resolvedPath = await TryResolveFilePathAsync(fileName, content);
        if (resolvedPath is not null)
        {
            await OpenAsync(resolvedPath);
            return;
        }

        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        var serializer = _extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(fileName))
            ?? (INotebookSerializer)new VersoSerializer();

        var notebook = await serializer.DeserializeAsync(content);

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        await RestoreLayoutMetadataAsync();
        await RestoreSettingsAsync();
        _filePath = null; // No on-disk path — opened from browser upload
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    /// <summary>
    /// Searches the local filesystem for a file matching the given name and content.
    /// Starts from the last-opened directory (if any), then falls back to CWD.
    /// Returns the full path if found, or null.
    /// </summary>
    private async Task<string?> TryResolveFilePathAsync(string fileName, string content)
    {
        var searchRoots = new List<string>();

        // Prefer the directory of the last-opened file (most likely location)
        if (_filePath is not null)
        {
            var lastDir = Path.GetDirectoryName(_filePath);
            if (lastDir is not null && Directory.Exists(lastDir))
                searchRoots.Add(lastDir);
        }

        // Fall back to the current working directory
        var cwd = Directory.GetCurrentDirectory();
        if (!searchRoots.Contains(cwd, StringComparer.Ordinal))
            searchRoots.Add(cwd);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            IgnoreInaccessible = true
        };

        foreach (var root in searchRoots)
        {
            foreach (var candidate in Directory.EnumerateFiles(root, fileName, options))
            {
                try
                {
                    var diskContent = await File.ReadAllTextAsync(candidate);
                    if (string.Equals(diskContent, content, StringComparison.Ordinal))
                        return Path.GetFullPath(candidate);
                }
                catch (IOException)
                {
                    // File locked or inaccessible — skip
                }
            }
        }

        return null;
    }

    /// <summary>Serialize current notebook to a .verso file.</summary>
    public async Task SaveAsync(string filePath)
    {
        if (_scaffold is null) return;

        // Flush layout metadata (grid positions, etc.) into the notebook model
        if (_scaffold.LayoutManager is { } lm)
            await lm.SaveMetadataAsync(_scaffold.Notebook);

        // Flush extension settings into the notebook model
        if (_scaffold.SettingsManager is { } sm)
            await sm.SaveSettingsAsync(_scaffold.Notebook);

        _scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
        var serializer = new VersoSerializer();
        var json = await serializer.SerializeAsync(_scaffold.Notebook);
        await File.WriteAllTextAsync(filePath, json);
        _filePath = filePath;
    }

    /// <summary>Create a new empty notebook with one default code cell.</summary>
    public async Task NewNotebookAsync()
    {
        await DisposeCurrentAsync();

        var notebook = new NotebookModel
        {
            Title = "Untitled",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            DefaultKernelId = "csharp"
        };

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        _scaffold.AddCell("code", "csharp");
        _filePath = null;
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    /// <summary>Execute a single cell by ID. The engine routes to kernel or renderer per cell type.</summary>
    public async Task<ExecutionResult> ExecuteCellAsync(Guid cellId)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var result = await _scaffold.ExecuteCellAsync(cellId);
        OnCellExecuted?.Invoke();
        return result;
    }

    /// <summary>Execute all cells in order.</summary>
    public async Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync()
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var results = await _scaffold.ExecuteAllAsync();
        OnCellExecuted?.Invoke();
        return results;
    }

    /// <summary>Add a new cell at the end.</summary>
    public CellModel AddCell(string type = "code", string? language = null)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        // Check ICellType registry to determine if this type has a kernel.
        // Non-executable cell types (no kernel) don't get a language assigned.
        var effectiveLanguage = language;
        if (effectiveLanguage is null)
        {
            var cellType = _extensionHost?.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, type, StringComparison.OrdinalIgnoreCase));

            if (cellType is not null)
                effectiveLanguage = cellType.Kernel?.LanguageId;
            else if (HasKernelOrNoRenderer(type))
                effectiveLanguage = _scaffold.DefaultKernelId ?? "csharp";
        }

        var cell = _scaffold.AddCell(type, effectiveLanguage);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    /// <summary>
    /// Returns true if the cell type has no matching renderer (assumed to be a code cell
    /// that should get a default language).
    /// </summary>
    private bool HasKernelOrNoRenderer(string type)
    {
        var hasRenderer = _extensionHost?.GetRenderers()
            .Any(r => string.Equals(r.CellTypeId, type, StringComparison.OrdinalIgnoreCase)) ?? false;
        return !hasRenderer;
    }

    /// <summary>Insert a new cell at the specified index.</summary>
    public CellModel InsertCell(int index, string type = "code", string? language = null)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var effectiveLanguage = language;
        if (effectiveLanguage is null)
        {
            var cellType = _extensionHost?.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, type, StringComparison.OrdinalIgnoreCase));

            if (cellType is not null)
                effectiveLanguage = cellType.Kernel?.LanguageId;
            else if (HasKernelOrNoRenderer(type))
                effectiveLanguage = _scaffold.DefaultKernelId ?? "csharp";
        }

        var cell = _scaffold.InsertCell(index, type, effectiveLanguage);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    /// <summary>Remove a cell by ID.</summary>
    public bool RemoveCell(Guid cellId)
    {
        if (_scaffold is null) return false;

        var result = _scaffold.RemoveCell(cellId);
        if (result) OnNotebookChanged?.Invoke();
        return result;
    }

    /// <summary>Change the type (and resolved language) of an existing cell.</summary>
    public void ChangeCellType(Guid cellId, string newType)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return;

        if (string.Equals(cell.Type, newType, StringComparison.OrdinalIgnoreCase))
            return;

        // Resolve effective language using the same logic as AddCell
        string? effectiveLanguage = null;
        var cellType = _extensionHost?.GetCellTypes()
            .FirstOrDefault(t => string.Equals(t.CellTypeId, newType, StringComparison.OrdinalIgnoreCase));

        if (cellType is not null)
            effectiveLanguage = cellType.Kernel?.LanguageId;
        else if (HasKernelOrNoRenderer(newType))
            effectiveLanguage = _scaffold.DefaultKernelId ?? "csharp";

        cell.Type = newType;
        cell.Language = effectiveLanguage;
        cell.Outputs.Clear();

        OnNotebookChanged?.Invoke();
    }

    /// <summary>Move a cell from one position to another.</summary>
    public void MoveCellAsync(int fromIndex, int toIndex)
    {
        if (_scaffold is null) return;

        _scaffold.MoveCell(fromIndex, toIndex);
        OnNotebookChanged?.Invoke();
    }

    /// <summary>Update the source of a cell.</summary>
    public void UpdateCellSource(Guid cellId, string source)
    {
        _scaffold?.UpdateCellSource(cellId, source);
    }

    /// <summary>Clear all cell outputs.</summary>
    public void ClearAllOutputs()
    {
        _scaffold?.ClearAllOutputs();
        OnCellExecuted?.Invoke();
    }

    /// <summary>Switch the active layout engine by layout ID.</summary>
    public void SwitchLayout(string layoutId)
    {
        if (_scaffold?.LayoutManager is null) return;

        _scaffold.LayoutManager.SetActiveLayout(layoutId);
        _scaffold.Notebook.ActiveLayoutId = layoutId;
        OnLayoutChanged?.Invoke();
    }

    /// <summary>Switch the active theme by theme ID.</summary>
    public void SwitchTheme(string themeId)
    {
        if (_scaffold?.ThemeEngine is null) return;

        _scaffold.ThemeEngine.SetActiveTheme(themeId);
        _scaffold.Notebook.PreferredThemeId = themeId;
        OnThemeChanged?.Invoke();
    }

    /// <summary>Restart the active kernel.</summary>
    public async Task RestartKernelAsync()
    {
        if (_scaffold is null) return;
        await _scaffold.RestartKernelAsync();
    }

    /// <summary>Enable an extension by ID.</summary>
    public async Task EnableExtensionAsync(string extensionId)
    {
        if (_extensionHost is null) return;
        await _extensionHost.EnableExtensionAsync(extensionId);
    }

    /// <summary>Disable an extension by ID.</summary>
    public async Task DisableExtensionAsync(string extensionId)
    {
        if (_extensionHost is null) return;
        await _extensionHost.DisableExtensionAsync(extensionId);
    }

    /// <summary>Update a single extension setting.</summary>
    public async Task UpdateSettingAsync(string extensionId, string settingName, object? value)
    {
        if (_scaffold?.SettingsManager is null) return;
        await _scaffold.SettingsManager.UpdateSettingAsync(extensionId, settingName, value);
    }

    /// <summary>
    /// Restores saved layout metadata (e.g. dashboard grid positions) from the notebook model
    /// into the matching layout engines.
    /// </summary>
    private async Task RestoreLayoutMetadataAsync()
    {
        if (_scaffold?.LayoutManager is not { } lm) return;
        if (_scaffold.Notebook.Layouts.Count == 0) return;

        var context = new BlazorToolbarActionContext(_scaffold, Array.Empty<Guid>());
        await lm.RestoreMetadataAsync(_scaffold.Notebook, context);
    }

    /// <summary>
    /// Ensures the scaffold has a default active layout and theme so the toolbar
    /// displays the correct names from the very first render.
    /// </summary>
    private void EnsureDefaults()
    {
        if (_scaffold is null || _extensionHost is null) return;

        // Default layout: prefer the first non-custom-renderer layout (i.e. "notebook")
        if (_scaffold.LayoutManager is { ActiveLayout: null } lm)
        {
            var defaultLayout = lm.AvailableLayouts
                .FirstOrDefault(l => !l.RequiresCustomRenderer)
                ?? lm.AvailableLayouts.FirstOrDefault();
            if (defaultLayout is not null)
                lm.SetActiveLayout(defaultLayout.LayoutId);
        }

        // Default theme: prefer light, fall back to first available
        if (_scaffold.ThemeEngine is { ActiveTheme: null } te)
        {
            var themes = _extensionHost.GetThemes();
            var defaultTheme = themes.FirstOrDefault(t => t.ThemeKind == ThemeKind.Light)
                ?? themes.FirstOrDefault();
            if (defaultTheme is not null)
                te.SetActiveTheme(defaultTheme.ThemeId);
        }
    }

    /// <summary>
    /// Restores persisted extension settings from the notebook model into matching extensions.
    /// </summary>
    private async Task RestoreSettingsAsync()
    {
        if (_scaffold?.SettingsManager is not { } sm) return;
        if (_scaffold.Notebook.ExtensionSettings.Count == 0) return;

        await sm.RestoreSettingsAsync(_scaffold.Notebook);
    }

    private void SubscribeToEngineEvents()
    {
        if (_extensionHost is not null)
            _extensionHost.OnExtensionStatusChanged += HandleExtensionStatusChanged;

        if (_scaffold?.Variables is VariableStore vs)
            vs.OnVariablesChanged += HandleVariablesChanged;

        if (_scaffold?.SettingsManager is { } sm)
            sm.OnSettingsChanged += HandleSettingsChanged;
    }

    private void UnsubscribeFromEngineEvents()
    {
        if (_extensionHost is not null)
            _extensionHost.OnExtensionStatusChanged -= HandleExtensionStatusChanged;

        if (_scaffold?.Variables is VariableStore vs)
            vs.OnVariablesChanged -= HandleVariablesChanged;

        if (_scaffold?.SettingsManager is { } sm)
            sm.OnSettingsChanged -= HandleSettingsChanged;
    }

    private void HandleExtensionStatusChanged(string extensionId, ExtensionStatus status)
    {
        OnExtensionStatusChanged?.Invoke();
    }

    private void HandleVariablesChanged()
    {
        OnVariablesChanged?.Invoke();
    }

    private void HandleSettingsChanged(string extensionId, string settingName, object? value)
    {
        OnSettingsChanged?.Invoke();
    }

    private async Task DisposeCurrentAsync()
    {
        UnsubscribeFromEngineEvents();
        if (_scaffold is not null)
        {
            await _scaffold.DisposeAsync();
            _scaffold = null;
        }
        _extensionHost = null;
        _filePath = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentAsync();
    }
}
