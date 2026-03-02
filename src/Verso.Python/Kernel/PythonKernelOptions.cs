namespace Verso.Python.Kernel;

/// <summary>
/// Configuration options for the Python language kernel.
/// </summary>
public sealed record PythonKernelOptions
{
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
}
