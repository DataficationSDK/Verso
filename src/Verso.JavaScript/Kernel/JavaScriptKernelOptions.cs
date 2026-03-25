namespace Verso.JavaScript.Kernel;

/// <summary>
/// Configuration options for the JavaScript kernel.
/// </summary>
public sealed record JavaScriptKernelOptions
{
    /// <summary>
    /// Explicit path to the Node.js executable. When null, auto-detection is used.
    /// </summary>
    public string? NodeExecutablePath { get; init; }

    /// <summary>
    /// When true, forces use of Jint even if Node.js is available.
    /// </summary>
    public bool ForceJint { get; init; }

    /// <summary>
    /// When true, auto-restart the Node.js subprocess after a crash.
    /// </summary>
    public bool AutoRestartOnCrash { get; init; } = true;

    /// <summary>
    /// When true, IVariableStore variables are injected into the JS scope before each cell.
    /// </summary>
    public bool InjectVariables { get; init; } = true;

    /// <summary>
    /// When true, user-defined globals are published to IVariableStore after each cell.
    /// </summary>
    public bool PublishVariables { get; init; } = true;

    /// <summary>
    /// Optional JavaScript code to execute after initialization.
    /// </summary>
    public string? StartupCode { get; init; }
}
