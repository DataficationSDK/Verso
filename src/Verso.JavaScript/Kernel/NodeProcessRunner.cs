using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// Manages a persistent Node.js child process that executes JavaScript code
/// via an NDJSON protocol over stdin/stdout.
/// </summary>
internal sealed class NodeProcessRunner : IJavaScriptRunner
{
    private readonly string _nodeExe;
    private readonly JavaScriptKernelOptions _options;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private TaskCompletionSource<bool>? _readyTcs;
    private Task? _readLoop;
    private string? _bridgePath;
    private string? _nodeModulesPath;
    private bool _alive;
    private bool _disposed;

    public NodeProcessRunner(string nodeExe, JavaScriptKernelOptions options)
    {
        _nodeExe = nodeExe;
        _options = options;
    }

    public bool IsAlive => _alive && _process is { HasExited: false };

    public async Task InitializeAsync(CancellationToken ct)
    {
        _bridgePath = Path.Combine(Path.GetTempPath(), $"verso-js-bridge-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(_bridgePath, NodeBridgeScript.BridgeSource, ct);

        var psi = new ProcessStartInfo(_nodeExe, $"--no-warnings \"{_bridgePath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_nodeModulesPath is not null)
            psi.EnvironmentVariables["NODE_PATH"] = _nodeModulesPath;

        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Node.js process.");

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = false;
        _stdout = _process.StandardOutput;

        _readLoop = Task.Run(() => ReadLoopAsync(CancellationToken.None), CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await _readyTcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Node.js bridge did not send ready signal within 10 seconds.");
        }

        _alive = true;
    }

    public async Task<JavaScriptRunResult> ExecuteAsync(string code, CancellationToken ct)
    {
        var response = await SendCommandAsync(new
        {
            type = "execute",
            id = Guid.NewGuid().ToString("N"),
            code,
        }, ct);

        return new JavaScriptRunResult(
            Stdout: GetStringOrNull(response, "stdout"),
            Stderr: GetStringOrNull(response, "stderr"),
            LastExpressionJson: GetStringOrNull(response, "lastExpr"),
            UserGlobals: GetStringArray(response, "globals"),
            HasError: response.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null,
            ErrorMessage: response.TryGetProperty("error", out var e1) && e1.ValueKind == JsonValueKind.Object
                ? GetStringOrNull(e1, "message") : null,
            ErrorStack: response.TryGetProperty("error", out var e2) && e2.ValueKind == JsonValueKind.Object
                ? GetStringOrNull(e2, "stack") : null
        );
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetVariablesAsync(
        IReadOnlyList<string> names, CancellationToken ct)
    {
        var response = await SendCommandAsync(new
        {
            type = "getVariables",
            id = Guid.NewGuid().ToString("N"),
            names,
        }, ct);

        var result = new Dictionary<string, string?>();
        if (response.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in vars.EnumerateObject())
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
        }
        return result;
    }

    public async Task SetVariablesAsync(IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        await SendCommandAsync(new
        {
            type = "setVariables",
            id = Guid.NewGuid().ToString("N"),
            variables,
        }, ct);
    }

    public async Task<TranspileResult> TranspileAsync(string code, CancellationToken ct)
    {
        var response = await SendCommandAsync(new
        {
            type = "transpile",
            id = Guid.NewGuid().ToString("N"),
            code,
        }, ct);

        var error = GetStringOrNull(response, "error");
        var transpiled = GetStringOrNull(response, "code");
        return new TranspileResult(transpiled, error);
    }

    public async Task AddModulePathAsync(string path, CancellationToken ct)
    {
        _nodeModulesPath = path;
        if (IsAlive)
        {
            await SendCommandAsync(new
            {
                type = "addModulePath",
                id = Guid.NewGuid().ToString("N"),
                path,
            }, ct);
        }
    }

    public void UpdateNodeModulesPath(string path) => _nodeModulesPath = path;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _alive = false;

        // Send shutdown
        if (_process is { HasExited: false } && _stdin is not null)
        {
            try
            {
                await _stdin.WriteLineAsync(JsonSerializer.Serialize(new { type = "shutdown" }));
                await _stdin.FlushAsync();
            }
            catch { }
        }

        // Wait for exit
        if (_process is { HasExited: false })
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _process.WaitForExitAsync(cts.Token);
            }
            catch
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }
        }

        _process?.Dispose();
        _process = null;
        _stdin = null;
        _stdout = null;

        // Clean up temp bridge file
        if (_bridgePath is not null)
        {
            try { File.Delete(_bridgePath); } catch { }
            _bridgePath = null;
        }

        // Fail all pending requests
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
    }

    private async Task<JsonElement> SendCommandAsync(object command, CancellationToken ct)
    {
        if (!IsAlive)
            throw new InvalidOperationException("Node.js process is not running.");

        var json = JsonSerializer.Serialize(command);

        // Extract the id field
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetString()!;

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));

        await _stdin!.WriteLineAsync(json);
        await _stdin.FlushAsync();

        return await tcs.Task;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stdout is not null)
            {
                var line = await _stdout.ReadLineAsync(ct);
                if (line is null) break;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var type = root.GetProperty("type").GetString();

                    if (type == "ready")
                    {
                        _readyTcs?.TrySetResult(true);
                        continue;
                    }

                    if (root.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString();
                        if (id is not null && _pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(root.Clone());
                    }
                }
                catch { }
            }
        }
        catch { }

        _alive = false;

        foreach (var tcs in _pending.Values)
            tcs.TrySetException(new IOException("Node.js process terminated unexpectedly."));
        _pending.Clear();
    }

    private static string? GetStringOrNull(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return null;
        if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        var s = val.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static IReadOnlyList<string>? GetStringArray(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val) || val.ValueKind != JsonValueKind.Array) return null;
        return val.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToList();
    }
}
