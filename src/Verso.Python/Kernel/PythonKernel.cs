using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Resources;

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

    /// <summary>The notebook setting deciding how much of an install a cell shows.</summary>
    internal const string HideInstallOutputSetting = "hideInstallOutput";

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
    public string? Description => Strings.Kernel_Description;

    // --- ILanguageKernel ---

    public string LanguageId => "python";
    public string DisplayName => "Python";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".py" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IExtensionSettings ---

    /// <remarks>
    /// Built on each read rather than held, so the settings panel shows the words in the
    /// language the reader asked for rather than the one the kernel first loaded in.
    /// </remarks>
    public IReadOnlyList<SettingDefinition> SettingDefinitions => new[]
    {
        new SettingDefinition(DependenciesSetting,
            Strings.Setting_Dependencies_Label,
            Strings.Setting_Dependencies_Description,
            SettingType.StringList, null, "Packages"),
        new SettingDefinition(HideInstallOutputSetting,
            Strings.Setting_HideInstallOutput_Label,
            Strings.Setting_HideInstallOutput_Description,
            SettingType.Boolean, true, "Packages", Order: 1),
    };

    /// <summary>
    /// Whether a cell names every distribution an install brought in. Read by the <c>#!pip</c>
    /// magic command, which reaches the setting through the kernel it is installing for.
    /// </summary>
    internal bool ShowInstallOutput => !_options.HideInstallOutput;

    public IReadOnlyDictionary<string, object?> GetSettingValues()
    {
        var values = new Dictionary<string, object?>();
        if (_options.Dependencies.Count > 0)
            values[DependenciesSetting] = _options.Dependencies.ToArray();

        // Reported only when it differs from the default, so a notebook whose author never
        // touched it does not carry the key.
        if (!_options.HideInstallOutput)
            values[HideInstallOutputSetting] = false;

        return values;
    }

    public Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue(DependenciesSetting, out var dependencies))
            SetDependencies(ReadStringList(dependencies));

        if (values.TryGetValue(HideInstallOutputSetting, out var hide))
            SetHideInstallOutput(hide);

        return Task.CompletedTask;
    }

    public Task OnSettingChangedAsync(string name, object? value)
    {
        if (string.Equals(name, DependenciesSetting, StringComparison.Ordinal))
            SetDependencies(ReadStringList(value));

        if (string.Equals(name, HideInstallOutputSetting, StringComparison.Ordinal))
            SetHideInstallOutput(value);

        return Task.CompletedTask;
    }

    private void SetDependencies(IReadOnlyList<string> dependencies)
    {
        _options = _options with { Dependencies = dependencies };

        // A running session holds its own copy so a settings change that lands after
        // initialization still takes effect on the next cell.
        _session?.AutoInstall.UpdateOptions(_options);
    }

    private void SetHideInstallOutput(object? value)
    {
        _options = _options with { HideInstallOutput = ReadBool(value, _options.HideInstallOutput) };
        _session?.AutoInstall.UpdateOptions(_options);
    }

    /// <summary>
    /// Read a boolean setting, which arrives as a <see cref="bool"/> from the settings UI and from
    /// a notebook file, and as text from a surface that has only string values. A value that is
    /// neither leaves the setting as it was rather than guessing at what was meant.
    /// </summary>
    private static bool ReadBool(object? value, bool fallback) => value switch
    {
        bool flag => flag,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => fallback,
    };

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
