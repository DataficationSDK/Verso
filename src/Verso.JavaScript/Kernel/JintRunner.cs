using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// In-process JavaScript runner using the Jint interpreter. Serves as a fallback
/// when Node.js is not available.
/// </summary>
/// <remarks>
/// Limitations compared to Node.js mode:
/// - No require() or import (no module system)
/// - No async/await at top level (synchronous execution)
/// - No npm packages
/// - Memory (128 MB) and timeout (30s) limits enforced
/// </remarks>
internal sealed class JintRunner : IJavaScriptRunner
{
    private Engine? _engine;
    private readonly JavaScriptKernelOptions _options;
    private readonly StringBuilder _stdoutBuf = new();
    private readonly StringBuilder _stderrBuf = new();
    private HashSet<string>? _initialGlobals;
    private bool _disposed;

    public JintRunner(JavaScriptKernelOptions options)
    {
        _options = options;
    }

    public bool IsAlive => _engine is not null && !_disposed;

    public Task InitializeAsync(CancellationToken ct)
    {
        _engine = new Engine(opts =>
        {
            opts.LimitMemory(128 * 1024 * 1024);
            opts.TimeoutInterval(TimeSpan.FromSeconds(30));
            opts.Strict(false);
        });

        // Shim console
        _engine.SetValue("console", new JintConsoleShim(_stdoutBuf, _stderrBuf));

        // Snapshot initial globals
        _initialGlobals = _engine.Global.GetOwnProperties()
            .Select(p => p.Key.ToString())
            .ToHashSet();
        _initialGlobals.Add("console");

        return Task.CompletedTask;
    }

    public Task<JavaScriptRunResult> ExecuteAsync(string code, CancellationToken ct)
    {
        if (_engine is null)
            throw new InvalidOperationException("Jint engine not initialized.");

        return Task.Run(() =>
        {
            _stdoutBuf.Clear();
            _stderrBuf.Clear();

            string? lastExpr = null;
            bool hasError = false;
            string? errorMessage = null;
            string? errorStack = null;

            try
            {
                // Wrap in a function scope so const/let/class can be re-declared
                // on cell re-execution. Promote declarations to globalThis for
                // cross-cell access.
                var declNames = ExtractTopLevelDeclarations(code);
                if (declNames.Count > 0)
                {
                    var promotion = string.Join("\n", declNames.Select(n =>
                        $"globalThis[\"{n}\"] = typeof {n} !== 'undefined' ? {n} : undefined;"));
                    var wrapped = $"(function() {{\n{code}\n{promotion}\n}})()";
                    _engine.Execute(wrapped);
                }
                else
                {
                    _engine.Execute(code);
                }
                lastExpr = TryEvalLastExpression(code);
            }
            catch (JavaScriptException jse)
            {
                hasError = true;
                errorMessage = jse.Message;
                errorStack = jse.JavaScriptStackTrace;
            }
            catch (TimeoutException)
            {
                hasError = true;
                errorMessage = "Execution timed out (30 second limit).";
            }
            catch (MemoryLimitExceededException)
            {
                hasError = true;
                errorMessage = "Memory limit exceeded (128 MB).";
            }
            catch (Exception ex)
            {
                hasError = true;
                errorMessage = ex.Message;
                errorStack = ex.StackTrace;
            }

            var stdout = _stdoutBuf.Length > 0 ? _stdoutBuf.ToString() : null;
            var stderr = _stderrBuf.Length > 0 ? _stderrBuf.ToString() : null;
            var globals = GetUserGlobals();

            return new JavaScriptRunResult(stdout, stderr, lastExpr, globals, hasError, errorMessage, errorStack);
        }, ct);
    }

    public Task<IReadOnlyDictionary<string, string?>> GetVariablesAsync(
        IReadOnlyList<string> names, CancellationToken ct)
    {
        if (_engine is null)
            throw new InvalidOperationException("Jint engine not initialized.");

        var result = new Dictionary<string, string?>();
        foreach (var name in names)
        {
            try
            {
                var val = _engine.GetValue(name);
                if (val.IsUndefined() || val.IsNull())
                {
                    result[name] = null;
                }
                else
                {
                    var clr = val.ToObject();
                    result[name] = clr is not null ? JsonSerializer.Serialize(clr) : null;
                }
            }
            catch
            {
                result[name] = null;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<string, string?>>(result);
    }

    public Task SetVariablesAsync(IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        if (_engine is null)
            throw new InvalidOperationException("Jint engine not initialized.");

        foreach (var (name, jsonValue) in variables)
        {
            try
            {
                var obj = JsonSerializer.Deserialize<object>(jsonValue);
                _engine.SetValue(name, obj);
            }
            catch
            {
                _engine.SetValue(name, jsonValue);
            }
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _engine?.Dispose();
        _engine = null;
        return ValueTask.CompletedTask;
    }

    private static readonly Regex DeclPattern = new(
        @"^(?:const|let|var|class|(?:async\s+)?function\*?)\s+([a-zA-Z_$][a-zA-Z0-9_$]*)",
        RegexOptions.Compiled);

    private static List<string> ExtractTopLevelDeclarations(string code)
    {
        var declNames = new List<string>();
        var lines = code.Split('\n');
        var braceDepth = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Check BEFORE updating brace depth so opening-brace lines are top-level
            if (braceDepth == 0)
            {
                var match = DeclPattern.Match(trimmed);
                if (match.Success)
                    declNames.Add(match.Groups[1].Value);
            }
            foreach (var ch in trimmed)
            {
                if (ch == '{') braceDepth++;
                else if (ch == '}') braceDepth--;
            }
        }
        return declNames;
    }

    private string? TryEvalLastExpression(string code)
    {
        var lines = code.Split('\n');
        string? lastLine = null;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (!string.IsNullOrEmpty(t) && !t.StartsWith("//") && !t.StartsWith("*"))
            {
                lastLine = t;
                break;
            }
        }

        if (lastLine is null) return null;

        string[] stmtPrefixes =
        [
            "const ", "let ", "var ", "function ", "class ",
            "if (", "if(", "for (", "for(", "while (", "while(",
            "switch (", "switch(", "try ", "try{",
            "return ", "throw ", "import ", "export ",
        ];
        if (stmtPrefixes.Any(p => lastLine.StartsWith(p))) return null;
        if (lastLine.EndsWith('{') || lastLine.EndsWith('}')) return null;

        try
        {
            var result = _engine!.Evaluate(lastLine);
            if (result.IsUndefined() || result.IsNull()) return null;
            if (result.Type == Types.Object && result.AsObject() is Jint.Native.Function.Function) return null;

            var clr = result.ToObject();
            return clr is not null ? JsonSerializer.Serialize(clr, new JsonSerializerOptions { WriteIndented = true }) : null;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetUserGlobals()
    {
        if (_engine is null || _initialGlobals is null) return [];

        var globals = new List<string>();
        foreach (var prop in _engine.Global.GetOwnProperties())
        {
            var key = prop.Key.ToString();
            if (_initialGlobals.Contains(key)) continue;
            if (key.StartsWith("_verso")) continue;

            var val = prop.Value.Value;
            if (val.IsUndefined() || (val.Type == Types.Object && val.AsObject() is Jint.Native.Function.Function)) continue;

            globals.Add(key);
        }
        return globals;
    }
}

/// <summary>
/// Minimal console shim exposed to Jint as the <c>console</c> global.
/// </summary>
internal sealed class JintConsoleShim
{
    private readonly StringBuilder _stdout;
    private readonly StringBuilder _stderr;

    public JintConsoleShim(StringBuilder stdout, StringBuilder stderr)
    {
        _stdout = stdout;
        _stderr = stderr;
    }

    public void log(params object[] args)
    {
        if (_stdout.Length > 0) _stdout.AppendLine();
        _stdout.Append(string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }

    public void info(params object[] args) => log(args);

    public void error(params object[] args)
    {
        if (_stderr.Length > 0) _stderr.AppendLine();
        _stderr.Append(string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }

    public void warn(params object[] args) => error(args);
}
