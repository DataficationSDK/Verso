using Verso.Abstractions;
using Verso.Python.Host;

namespace Verso.Python.Kernel;

/// <summary>
/// Python language kernel for Verso notebooks. Cells run in the user's own Python interpreter as a
/// separate process, so the packages, virtual environment, and version the notebook sees are the
/// ones already installed on the machine. Variables are shared with every other kernel in the
/// notebook.
/// </summary>
[VersoExtension]
public sealed class PythonKernel : ILanguageKernel, IExtensionSettings
{
    /// <summary>
    /// Selects the install policy: <c>off</c>, <c>prompt</c>, or <c>auto</c>. Delivered through the
    /// environment rather than a notebook setting, because a policy that travelled inside a
    /// notebook file would let a downloaded notebook decide what may install itself.
    /// </summary>
    internal const string AutoInstallVariable = "VERSO_PYTHON_AUTO_INSTALL";

    /// <summary>
    /// Set to <c>off</c> to install and create environments with pip and the standard library
    /// rather than uv, even when uv is on the path.
    /// </summary>
    internal const string UvVariable = "VERSO_PYTHON_UV";

    /// <summary>The notebook setting carrying the requirement list ensured on first execution.</summary>
    internal const string DependenciesSetting = "dependencies";

    private PythonKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private PythonHostSession? _session;
    private string? _sessionInterpreter;
    private bool _initialized;
    private bool _disposed;

    public PythonKernel() : this(new PythonKernelOptions()) { }

    public PythonKernel(PythonKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Kernels are constructed by the extension host with default options, so the host's own
        // choices arrive through the environment. An option set explicitly by an embedder wins.
        _options = ApplyEnvironment(_options);
    }

    private static PythonKernelOptions ApplyEnvironment(PythonKernelOptions options)
    {
        var policy = Environment.GetEnvironmentVariable(AutoInstallVariable);
        if (TryParsePolicy(policy, out var parsed))
            options = options with { AutoInstall = parsed };

        var uv = Environment.GetEnvironmentVariable(UvVariable);
        if (uv is "0" or "off" or "OFF" or "Off" or "false" or "FALSE" or "False")
            options = options with { UseUv = UvUsage.Off };

        return options;
    }

    internal static bool TryParsePolicy(string? value, out AutoInstallPolicy policy)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "off": policy = AutoInstallPolicy.Off; return true;
            case "prompt": policy = AutoInstallPolicy.Prompt; return true;
            case "auto": policy = AutoInstallPolicy.Auto; return true;
            default: policy = AutoInstallPolicy.Prompt; return false;
        }
    }

    /// <summary>
    /// Records the interpreter chosen with <c>#!python</c> for this session. Held on the kernel
    /// because a restart clears the variable store before the kernel starts again, and the
    /// selection has to outlive that.
    /// </summary>
    internal void SetSessionInterpreter(string? executable)
    {
        _sessionInterpreter = executable;
        _session?.SetSessionInterpreter(executable);
    }

    /// <summary>
    /// Resolve the interpreter this kernel executes against, binding the session to the notebook
    /// first so a magic command running in the very first cell installs into the same environment
    /// the cell below it will import from. Returns null when no interpreter could be resolved.
    /// </summary>
    internal async Task<string?> GetActiveInterpreterAsync(
        IVersoContext context, CancellationToken cancellationToken)
    {
        if (!_initialized)
            await InitializeAsync().ConfigureAwait(false);

        var session = _session;
        if (session is null)
            return null;

        await session.EnsureBoundAsync(PythonHostSession.NotebookDirectory(context), cancellationToken)
            .ConfigureAwait(false);

        return session.Interpreter?.Executable;
    }

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.python";
    public string Name => "Python";
    public string Version => "1.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Python language kernel running the interpreter installed on this machine.";

    // --- ILanguageKernel ---

    public string LanguageId => "python";
    public string DisplayName => "Python";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".py" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IExtensionSettings ---

    public IReadOnlyList<SettingDefinition> SettingDefinitions { get; } = new[]
    {
        new SettingDefinition(DependenciesSetting, "Dependencies",
            "Requirement strings installed into the notebook's Python environment before the " +
            "first cell runs, under the same consent policy as an import. One per entry, in the " +
            "form pip accepts (for example \"pandas>=2\"). An entry has to name a package: " +
            "installer options and locations such as a URL, a version control reference or a " +
            "path are refused, because a notebook does not choose where packages come from.",
            SettingType.StringList, null, "Packages"),
    };

    public IReadOnlyDictionary<string, object?> GetSettingValues()
    {
        var values = new Dictionary<string, object?>();
        if (_options.Dependencies.Count > 0)
            values[DependenciesSetting] = _options.Dependencies.ToArray();
        return values;
    }

    public Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue(DependenciesSetting, out var value))
            SetDependencies(ReadStringList(value));

        return Task.CompletedTask;
    }

    public Task OnSettingChangedAsync(string name, object? value)
    {
        if (string.Equals(name, DependenciesSetting, StringComparison.Ordinal))
            SetDependencies(ReadStringList(value));

        return Task.CompletedTask;
    }

    private void SetDependencies(IReadOnlyList<string> dependencies)
    {
        _options = _options with { Dependencies = dependencies };

        // A running session holds its own copy so a settings change that lands after
        // initialization still takes effect on the next cell.
        _session?.AutoInstall.UpdateOptions(_options);
    }

    /// <summary>
    /// Read a list setting, which arrives as a string array from the settings UI and as a
    /// deserialized JSON array from a notebook file.
    /// </summary>
    private static IReadOnlyList<string> ReadStringList(object? value)
    {
        if (value is null)
            return Array.Empty<string>();

        // A settings surface that has no list editor sends one comma-separated string, which is
        // also the most natural thing for a person to type.
        if (value is string single)
        {
            return single
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (value is not System.Collections.IEnumerable items)
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var item in items)
        {
            var text = item?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text!.Trim());
        }

        return results;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

        await InitializeHostSessionAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the out-of-process host. Kernel initialization carries no notebook context, so the
    /// interpreter is resolved against the process working directory and the first execution
    /// rebinds to the notebook's own directory if they differ. A startup failure is not thrown
    /// here: it is reported as cell output on the first execution, where the user can see it.
    /// </summary>
    private async Task InitializeHostSessionAsync()
    {
        _session = new PythonHostSession(_options, _sessionInterpreter);
        _executionLock = new SemaphoreSlim(1, 1);
        _initialized = true;

        try
        {
            await _session.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The session restarts on demand and surfaces the reason as an error output.
        }
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            return await _session!.ExecuteAsync(code, context).ConfigureAwait(false);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    // Editor assistance is answered by the Python process itself, so it sees the interpreter the
    // cells run in and the names they defined.

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        if (_session is null)
            return Array.Empty<Completion>();

        try
        {
            return await _session.GetCompletionsAsync(code, cursorPosition).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Completion>();
        }
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        if (_session is null)
            return Array.Empty<Diagnostic>();

        try
        {
            return await _session.GetDiagnosticsAsync(code).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Diagnostic>();
        }
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        if (_session is null)
            return null;

        try
        {
            return await _session.GetHoverInfoAsync(code, cursorPosition).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _initialized = false;

        if (_session is not null)
        {
            var session = _session;
            _session = null;
            return session.DisposeAsync();
        }

        _executionLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonKernel));
    }
}
