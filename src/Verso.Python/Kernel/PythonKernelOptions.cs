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
/// Configuration options for the Python language kernel.
/// </summary>
public sealed record PythonKernelOptions
{
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
    /// Path to the Python shared library (e.g. "python3.12" or full path).
    /// When <c>null</c>, the engine manager auto-detects the system Python.
    /// </summary>
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
}
