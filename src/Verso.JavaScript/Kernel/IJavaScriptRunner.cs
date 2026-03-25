namespace Verso.JavaScript.Kernel;

/// <summary>
/// Common interface for JavaScript execution backends (Node.js subprocess or Jint in-process).
/// </summary>
internal interface IJavaScriptRunner : IAsyncDisposable
{
    /// <summary>
    /// Initialize the runtime. For Node.js this spawns the subprocess; for Jint this creates the engine.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Execute a code snippet and return the structured result.
    /// </summary>
    Task<JavaScriptRunResult> ExecuteAsync(string code, CancellationToken ct);

    /// <summary>
    /// Read named variables from the JS global scope, returned as JSON-serialized strings.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> GetVariablesAsync(IReadOnlyList<string> names, CancellationToken ct);

    /// <summary>
    /// Inject named variables into the JS global scope from JSON-serialized values.
    /// </summary>
    Task SetVariablesAsync(IReadOnlyDictionary<string, string> variables, CancellationToken ct);

    /// <summary>
    /// Transpile TypeScript code to JavaScript. Returns the transpiled code or an error message.
    /// Only supported by Node.js runner when the typescript module is installed.
    /// </summary>
    Task<TranspileResult> TranspileAsync(string code, CancellationToken ct) =>
        Task.FromResult(new TranspileResult(null, "TypeScript transpilation requires Node.js with the typescript module installed."));

    /// <summary>
    /// True if the backend is still operational. For Node.js, false after a process crash.
    /// </summary>
    bool IsAlive { get; }
}

/// <summary>
/// Structured result from a JavaScript code execution.
/// </summary>
/// <summary>
/// Result of a TypeScript transpilation.
/// </summary>
internal sealed record TranspileResult(string? Code, string? Error)
{
    public bool Success => Code is not null && Error is null;
}

/// <summary>
/// Structured result from a JavaScript code execution.
/// </summary>
internal sealed record JavaScriptRunResult(
    string? Stdout,
    string? Stderr,
    string? LastExpressionJson,
    IReadOnlyList<string>? UserGlobals,
    bool HasError,
    string? ErrorMessage,
    string? ErrorStack);
