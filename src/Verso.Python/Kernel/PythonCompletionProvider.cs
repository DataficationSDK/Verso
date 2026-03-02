using Python.Runtime;
using Verso.Abstractions;
using Verso.Python.Helpers;

namespace Verso.Python.Kernel;

/// <summary>
/// Provides Python IntelliSense (completions, diagnostics, hover) using jedi as the
/// primary provider and rlcompleter as a fallback. All Python calls are dispatched to
/// a thread-pool thread under the GIL.
/// </summary>
internal sealed class PythonCompletionProvider : IDisposable
{
    private readonly PythonScopeManager _scope;
    private readonly List<string> _executedSources = new();
    private bool _jediAvailable;
    private bool _disposed;

    internal PythonCompletionProvider(PythonScopeManager scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    internal bool JediAvailable => _jediAvailable;

    /// <summary>
    /// Appends successfully executed cell code for cross-cell context.
    /// </summary>
    internal void AppendExecutedCode(string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
            _executedSources.Add(code);
    }

    /// <summary>
    /// Probes whether jedi is importable under the current Python environment.
    /// </summary>
    internal Task ProbeJediAsync()
    {
        return Task.Run(() =>
        {
            using (Py.GIL())
            {
                try
                {
                    _scope.Exec("import jedi as _verso_jedi_probe");
                    _scope.Exec("del _verso_jedi_probe");
                    _jediAvailable = true;
                }
                catch (PythonException)
                {
                    _jediAvailable = false;
                }
            }
        });
    }

    /// <summary>
    /// Returns completions at the given cursor offset using jedi (primary) or rlcompleter (fallback).
    /// </summary>
    internal Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        return Task.Run(() =>
        {
            using (Py.GIL())
            {
                return _jediAvailable
                    ? GetJediCompletions(code, cursorPosition)
                    : GetRlCompleterCompletions(code, cursorPosition);
            }
        });
    }

    /// <summary>
    /// Returns syntax diagnostics using jedi. Returns empty if jedi is unavailable.
    /// </summary>
    internal Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        if (!_jediAvailable)
            return Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

        return Task.Run(() =>
        {
            using (Py.GIL())
            {
                return GetJediDiagnostics(code);
            }
        });
    }

    /// <summary>
    /// Returns hover information at the given cursor offset using jedi. Returns null if jedi is unavailable.
    /// </summary>
    internal Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        if (!_jediAvailable)
            return Task.FromResult<HoverInfo?>(null);

        return Task.Run(() =>
        {
            using (Py.GIL())
            {
                return GetJediHoverInfo(code, cursorPosition);
            }
        });
    }

    /// <summary>
    /// Clears accumulated cross-cell source history.
    /// </summary>
    internal void Reset()
    {
        _executedSources.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _executedSources.Clear();
    }

    // ------------------------------------------------------------------
    // Cross-cell context
    // ------------------------------------------------------------------

    private (string CombinedSource, int PrefixLineCount) BuildCombinedSource(string currentCode)
        => PythonPositionHelpers.BuildCombinedSource(_executedSources, currentCode);

    private static (int Line, int Column) OffsetToLineColumn(string text, int offset)
        => PythonPositionHelpers.OffsetToLineColumn(text, offset);

    // ------------------------------------------------------------------
    // Jedi completions
    // ------------------------------------------------------------------

    private IReadOnlyList<Completion> GetJediCompletions(string code, int cursorPosition)
    {
        var (combined, prefixLines) = BuildCombinedSource(code);
        var (cellLine, cellCol) = OffsetToLineColumn(code, cursorPosition);

        // jedi uses 1-based lines, 0-based columns
        var jediLine = prefixLines + cellLine + 1;
        var jediCol = cellCol;

        try
        {
            using var jedi = Py.Import("jedi");
            using var builtins = Py.Import("builtins");

            // Build namespaces = [dict(vars(scope))]
            using var varsDict = _scope.Eval("dict(vars())");
            using var nsList = new PyList();
            nsList.Append(varsDict);

            // jedi.Interpreter(source, namespaces=nsList).complete(line, column)
            using var source = new PyString(combined);
            using var interpreter = jedi.InvokeMethod("Interpreter", source, new PyDict());
            // Set namespaces via keyword — use the overload with kwargs
            using var interpWithNs = jedi.GetAttr("Interpreter").Invoke(
                new PyTuple(new PyObject[] { source }),
                Py.kw("namespaces", nsList));
            using var lineObj = new PyInt(jediLine);
            using var colObj = new PyInt(jediCol);
            using var completions = interpWithNs.InvokeMethod("complete", lineObj, colObj);
            using var completionList = new PyList(completions);

            var results = new List<Completion>((int)completionList.Length());

            for (var i = 0; i < completionList.Length(); i++)
            {
                using var item = completionList[i];
                var name = item.GetAttr("name").ToString() ?? "";
                var complete = item.GetAttr("complete").ToString() ?? "";
                var type = item.GetAttr("type").ToString() ?? "";

                string? description = null;
                try
                {
                    using var docObj = item.InvokeMethod("docstring");
                    var doc = docObj.ToString();
                    if (!string.IsNullOrEmpty(doc))
                    {
                        // Take first line of docstring as description
                        var firstLine = doc.Split('\n')[0].Trim();
                        if (!string.IsNullOrEmpty(firstLine))
                            description = firstLine;
                    }
                }
                catch { /* docstring not always available */ }

                results.Add(new Completion(
                    DisplayText: name,
                    InsertText: name,
                    Kind: MapJediType(type),
                    Description: description));
            }

            return results;
        }
        catch (PythonException)
        {
            // Fall back to rlcompleter on jedi failure
            return GetRlCompleterCompletions(code, cursorPosition);
        }
    }

    // ------------------------------------------------------------------
    // rlcompleter fallback
    // ------------------------------------------------------------------

    private IReadOnlyList<Completion> GetRlCompleterCompletions(string code, int cursorPosition)
    {
        try
        {
            // Extract the token being completed by walking backwards from cursor
            var pos = Math.Min(cursorPosition, code.Length);
            var start = pos;
            while (start > 0 && IsIdentifierOrDot(code[start - 1]))
                start--;

            var token = code[start..pos];
            if (string.IsNullOrEmpty(token))
                return Array.Empty<Completion>();

            // rlcompleter.Completer(dict(vars())).complete(token, state)
            using var rlcompleter = Py.Import("rlcompleter");
            using var varsDict = _scope.Eval("dict(vars())");
            using var completer = rlcompleter.GetAttr("Completer").Invoke(varsDict);

            var results = new List<Completion>();
            using var tokenPy = new PyString(token);

            for (var state = 0; state < 500; state++) // safety cap
            {
                using var stateObj = new PyInt(state);
                using var result = completer.InvokeMethod("complete", tokenPy, stateObj);

                if (result.IsNone())
                    break;

                var text = result.ToString();
                if (string.IsNullOrEmpty(text))
                    break;

                // rlcompleter returns the full match; editor replaces the typed prefix with InsertText
                var displayText = text.TrimEnd('('); // strip trailing paren if present

                results.Add(new Completion(
                    DisplayText: displayText,
                    InsertText: displayText,
                    Kind: "Text"));
            }

            return results;
        }
        catch (PythonException)
        {
            return Array.Empty<Completion>();
        }
    }

    private static bool IsIdentifierOrDot(char c)
        => PythonPositionHelpers.IsIdentifierOrDot(c);

    // ------------------------------------------------------------------
    // Jedi diagnostics
    // ------------------------------------------------------------------

    private IReadOnlyList<Diagnostic> GetJediDiagnostics(string code)
    {
        var (combined, prefixLines) = BuildCombinedSource(code);

        try
        {
            using var jedi = Py.Import("jedi");
            using var source = new PyString(combined);
            using var script = jedi.GetAttr("Script").Invoke(source);
            using var errors = script.InvokeMethod("get_syntax_errors");
            using var errorList = new PyList(errors);

            var results = new List<Diagnostic>();

            for (var i = 0; i < errorList.Length(); i++)
            {
                using var error = errorList[i];

                // jedi syntax errors have .line (1-based), .column (0-based)
                var errorLine = error.GetAttr("line").As<int>();      // 1-based
                var errorCol = error.GetAttr("column").As<int>();     // 0-based

                // Convert to 0-based cell-relative
                var cellLine = errorLine - 1 - prefixLines;

                // Filter out errors in the prefix region
                if (cellLine < 0)
                    continue;

                string message;
                try
                {
                    using var msgObj = error.InvokeMethod("get_message");
                    message = msgObj.ToString() ?? "Syntax error";
                }
                catch
                {
                    message = "Syntax error";
                }

                // End position: same line, column + 1 (highlight at least one character)
                var untilLine = cellLine;
                var untilCol = errorCol + 1;

                // jedi may expose until_line / until_column
                try
                {
                    var rawUntilLine = error.GetAttr("until_line").As<int>(); // 1-based
                    var rawUntilCol = error.GetAttr("until_column").As<int>(); // 0-based
                    untilLine = rawUntilLine - 1 - prefixLines;
                    untilCol = rawUntilCol;
                }
                catch { /* use defaults */ }

                results.Add(new Diagnostic(
                    Severity: DiagnosticSeverity.Error,
                    Message: message,
                    StartLine: cellLine,
                    StartColumn: errorCol,
                    EndLine: Math.Max(untilLine, cellLine),
                    EndColumn: untilCol,
                    Code: "SyntaxError"));
            }

            return results;
        }
        catch (PythonException)
        {
            return Array.Empty<Diagnostic>();
        }
    }

    // ------------------------------------------------------------------
    // Jedi hover
    // ------------------------------------------------------------------

    private HoverInfo? GetJediHoverInfo(string code, int cursorPosition)
    {
        var (combined, prefixLines) = BuildCombinedSource(code);
        var (cellLine, cellCol) = OffsetToLineColumn(code, cursorPosition);

        var jediLine = prefixLines + cellLine + 1;
        var jediCol = cellCol;

        try
        {
            using var jedi = Py.Import("jedi");
            using var source = new PyString(combined);
            using var varsDict = _scope.Eval("dict(vars())");
            using var nsList = new PyList();
            nsList.Append(varsDict);

            using var interpreter = jedi.GetAttr("Interpreter").Invoke(
                new PyTuple(new PyObject[] { source }),
                Py.kw("namespaces", nsList));
            using var lineObj = new PyInt(jediLine);
            using var colObj = new PyInt(jediCol);
            using var names = interpreter.InvokeMethod("infer", lineObj, colObj);
            using var nameList = new PyList(names);

            if (nameList.Length() == 0)
                return null;

            using var first = nameList[0];
            var fullName = first.GetAttr("full_name").ToString() ?? "";
            var type = first.GetAttr("type").ToString() ?? "";

            string? docstring = null;
            try
            {
                using var docObj = first.InvokeMethod("docstring");
                docstring = docObj.ToString();
            }
            catch { /* not always available */ }

            // Build hover content
            var signature = !string.IsNullOrEmpty(fullName) ? fullName : type;
            var content = !string.IsNullOrEmpty(docstring)
                ? $"{signature}\n\n{docstring}"
                : signature;

            if (string.IsNullOrEmpty(content))
                return null;

            // Compute identifier range at cursor position
            var range = ComputeIdentifierRange(code, cursorPosition, cellLine);

            return new HoverInfo(content, Range: range);
        }
        catch (PythonException)
        {
            return null;
        }
    }

    private static (int StartLine, int StartColumn, int EndLine, int EndColumn)? ComputeIdentifierRange(
        string code, int cursorPosition, int line)
        => PythonPositionHelpers.ComputeIdentifierRange(code, cursorPosition, line);

    private static string MapJediType(string jediType)
        => JediTypeMapper.Map(jediType);
}
