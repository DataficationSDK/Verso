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

    /// <summary>
    /// When true, an npm install reports which packages it added and at which versions rather
    /// than relaying npm's own narration and its funding and audit footers. Defaults to true:
    /// installing one package brings in its dependencies, and that block is saved into the
    /// notebook alongside the output the cell was actually run for. A failed install always
    /// reports in full, as does anything npm's audit found.
    /// </summary>
    public bool HideInstallOutput { get; init; } = true;
}
