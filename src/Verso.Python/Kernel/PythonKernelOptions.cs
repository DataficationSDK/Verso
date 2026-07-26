namespace Verso.Python.Kernel;

/// <summary>
/// Controls whether the fast uv tool is used to create and manage Python environments.
/// </summary>
public enum UvUsage
{
    /// <summary>Use uv when it is available, otherwise fall back to the standard-library <c>venv</c> module.</summary>
    Auto,

    /// <summary>Never use uv; always create environments with the standard-library <c>venv</c> module.</summary>
    Off,
}

/// <summary>
/// Controls whether a cell's missing imports are installed for it, and whether the user is asked
/// first.
/// </summary>
public enum AutoInstallPolicy
{
    /// <summary>
    /// Ask before installing, listing the exact distributions and the environment they go into.
    /// Declining runs the cell anyway, so it fails at the import as it would have without this.
    /// </summary>
    Prompt,

    /// <summary>
    /// Install known distributions without asking. A module whose distribution name is only a
    /// guess is reported instead of installed, because a misspelled import resolves to a
    /// plausible package name that somebody may well have published.
    /// </summary>
    Auto,

    /// <summary>Never scan and never install. The default for non-interactive runs.</summary>
    Off,
}

/// <summary>
/// Configuration options for the Python language kernel.
/// </summary>
public sealed record PythonKernelOptions
{
    /// <summary>Default ceiling on the serialized size of a single published variable.</summary>
    public const int DefaultVariablePublishLimitBytes = 8 * 1024 * 1024;

    /// <summary>Default ceiling on the serialized size of a single injected variable.</summary>
    public const int DefaultVariableInjectLimitBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Explicit path or name of the Python interpreter executable to run. When <c>null</c>, the
    /// interpreter is discovered from the environment, the workspace, and well-known locations.
    /// This is the highest-precedence selection and is where a host interpreter setting flows in.
    /// </summary>
    public string? PythonExecutable { get; init; }

    /// <summary>
    /// Whether to use the uv tool when creating managed Python environments. Defaults to
    /// <see cref="UvUsage.Auto"/>.
    /// </summary>
    public UvUsage UseUv { get; init; } = UvUsage.Auto;

    /// <summary>
    /// Path to a Python shared library. Ignored: cells run in a separate process against an
    /// interpreter executable, so there is no library for this process to load. Use
    /// <see cref="PythonExecutable"/> to name an interpreter instead.
    /// </summary>
    [Obsolete("Python runs as a separate process and loads no shared library. Use PythonExecutable to select an interpreter.")]
    public string? PythonDll { get; init; }

    /// <summary>
    /// Default Python modules to import at kernel startup.
    /// </summary>
    public IReadOnlyList<string> DefaultImports { get; init; } = new[] { "sys", "os", "io", "math", "json" };

    /// <summary>
    /// Optional Python code to execute after initialization completes.
    /// </summary>
    public string? StartupCode { get; init; }

    /// <summary>
    /// When <c>true</c>, Python locals are published to the shared <see cref="Verso.Abstractions.IVariableStore"/>
    /// after each cell execution.
    /// </summary>
    public bool PublishVariables { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, variables from the shared <see cref="Verso.Abstractions.IVariableStore"/> are injected
    /// into the Python scope before each cell execution.
    /// </summary>
    public bool InjectVariables { get; init; } = true;

    /// <summary>
    /// How long a cancelled cell is given to stop on its own after being interrupted, before the
    /// interpreter is stopped outright. Code executing inside a native call can ignore the
    /// interrupt entirely, so termination is the only remaining option once this elapses.
    /// </summary>
    public TimeSpan InterruptGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ceiling on the serialized size of a single published variable. A value over the limit is
    /// published as a short descriptor instead, so one accidental multi-gigabyte object cannot
    /// stall a cell or overwhelm the shared store.
    /// </summary>
    public int VariablePublishLimitBytes { get; init; } = DefaultVariablePublishLimitBytes;

    /// <summary>
    /// Ceiling on the serialized size of a single variable sent into the Python scope. A value
    /// over the limit arrives as an explanation instead. The whole payload is bounded at four
    /// times this, because several large but legal values still have to leave the frame inside
    /// what the transport will carry, and exceeding that reads as the subprocess having died.
    /// </summary>
    public int VariableInjectLimitBytes { get; init; } = DefaultVariableInjectLimitBytes;

    /// <summary>
    /// When <c>true</c>, a cell line beginning with <c>!</c> runs as a shell command and
    /// <c>%pip</c> runs pip against the active interpreter, matching notebook convention.
    /// </summary>
    public bool EnableShellEscapes { get; init; } = true;

    /// <summary>
    /// Whether a cell's missing imports are installed for it, and whether the user is asked
    /// first. Defaults to <see cref="AutoInstallPolicy.Prompt"/>; hosts without a way to ask
    /// select <see cref="AutoInstallPolicy.Off"/>.
    /// </summary>
    public AutoInstallPolicy AutoInstall { get; init; } = AutoInstallPolicy.Prompt;

    /// <summary>
    /// Extra module-to-distribution entries consulted ahead of the built-in table, for packages
    /// whose import name differs from their distribution name in a way Verso does not know.
    /// An entry here counts as known, so it installs without asking under
    /// <see cref="AutoInstallPolicy.Auto"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ImportMap { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Requirement strings ensured once per session before the first cell runs, under the same
    /// policy gate as an import. Populated from the notebook's saved settings.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
}
