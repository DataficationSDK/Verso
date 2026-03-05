using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using Verso.Abstractions;

namespace Verso.PowerShell.Kernel;

internal sealed record InvokeResult(
    IReadOnlyList<string> OutputLines,
    IReadOnlyList<string> ErrorLines,
    IReadOnlyList<string> WarningLines,
    IReadOnlyList<string> InformationLines,
    Exception? Exception);

internal sealed class RunspaceManager : IDisposable
{
    private Runspace? _runspace;
    private bool _disposed;

    public void Initialize()
    {
        if (_runspace is not null) return;

        var iss = InitialSessionState.CreateDefault2();
        iss.ThreadOptions = PSThreadOptions.UseCurrentThread;

        if (OperatingSystem.IsWindows())
        {
            iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
        }

        _runspace = RunspaceFactory.CreateRunspace(iss);
        _runspace.Open();
    }

    public InvokeResult Invoke(string code, CancellationToken ct)
    {
        ThrowIfDisposed();
        var runspace = _runspace ?? throw new InvalidOperationException("RunspaceManager not initialized.");

        using var ps = System.Management.Automation.PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddScript(code);

        using var registration = ct.Register(() =>
        {
            try { ps.BeginStop(null, null); }
            catch { /* best effort */ }
        });

        var outputLines = new List<string>();
        var errorLines = new List<string>();
        var warningLines = new List<string>();
        var informationLines = new List<string>();
        Exception? exception = null;

        try
        {
            Collection<PSObject> results = ps.Invoke();

            // Detect format objects (from Format-Table, Format-List, etc.)
            // and render them through Out-String instead of calling ToString()
            if (results.Count > 0 && HasFormatObjects(results))
            {
                using var renderer = System.Management.Automation.PowerShell.Create();
                renderer.Runspace = runspace;
                renderer.AddCommand("Out-String");
                var rendered = renderer.Invoke(results);
                foreach (var line in rendered)
                {
                    var text = line?.ToString()?.TrimEnd();
                    if (!string.IsNullOrEmpty(text))
                        outputLines.Add(text);
                }
            }
            else
            {
                foreach (var obj in results)
                {
                    if (obj is not null)
                    {
                        outputLines.Add(obj.BaseObject is string s ? s : obj.ToString() ?? string.Empty);
                    }
                }
            }

            foreach (var err in ps.Streams.Error)
            {
                errorLines.Add(err.ToString());
            }

            foreach (var warn in ps.Streams.Warning)
            {
                warningLines.Add(warn.ToString());
            }

            foreach (var info in ps.Streams.Information)
            {
                var msg = info.MessageData?.ToString();
                if (!string.IsNullOrEmpty(msg))
                    informationLines.Add(msg);
            }
        }
        catch (RuntimeException ex)
        {
            exception = ex;
            errorLines.Add(ex.ErrorRecord?.ToString() ?? ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exception = ex;
            errorLines.Add(ex.Message);
        }

        return new InvokeResult(outputLines, errorLines, warningLines, informationLines, exception);
    }

    public void SetVariable(string name, object? value)
    {
        ThrowIfDisposed();
        var runspace = _runspace ?? throw new InvalidOperationException("RunspaceManager not initialized.");
        runspace.SessionStateProxy.SetVariable(name, value);
    }

    public IReadOnlyList<(string Name, object? Value)> GetSessionVariables()
    {
        ThrowIfDisposed();
        var runspace = _runspace ?? throw new InvalidOperationException("RunspaceManager not initialized.");

        var results = new List<(string, object?)>();

        using var ps = System.Management.Automation.PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddCommand("Get-Variable");

        try
        {
            var variables = ps.Invoke<PSVariable>();
            foreach (var v in variables)
            {
                results.Add((v.Name, v.Value is PSObject pso ? pso.BaseObject : v.Value));
            }
        }
        catch
        {
            // If Get-Variable fails, return what we have
        }

        return results;
    }

    public IReadOnlyList<Completion> GetCompletions(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        var runspace = _runspace ?? throw new InvalidOperationException("RunspaceManager not initialized.");

        using var ps = System.Management.Automation.PowerShell.Create();
        ps.Runspace = runspace;

        try
        {
            var result = CommandCompletion.CompleteInput(code, cursorPosition, null, ps);

            if (result?.CompletionMatches is null || result.CompletionMatches.Count == 0)
                return Array.Empty<Completion>();

            var completions = new List<Completion>(result.CompletionMatches.Count);
            foreach (var match in result.CompletionMatches)
            {
                completions.Add(new Completion(
                    match.ListItemText,
                    match.CompletionText,
                    Helpers.CompletionResultTypeMapper.Map(match.ResultType),
                    match.ToolTip));
            }

            return completions;
        }
        catch
        {
            return Array.Empty<Completion>();
        }
    }

    public static IReadOnlyList<Diagnostic> GetDiagnostics(string code)
    {
        Parser.ParseInput(code, out _, out var errors);

        if (errors is null || errors.Length == 0)
            return Array.Empty<Diagnostic>();

        var diagnostics = new List<Diagnostic>(errors.Length);
        foreach (var err in errors)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                err.Message,
                err.Extent.StartLineNumber - 1, // PS is 1-based, Verso is 0-based
                err.Extent.StartColumnNumber - 1,
                err.Extent.EndLineNumber - 1,
                err.Extent.EndColumnNumber - 1,
                err.ErrorId));
        }

        return diagnostics;
    }

    public static HoverInfo? GetHoverInfo(string code, int cursorPosition)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var ast = Parser.ParseInput(code, out var tokens, out _);

        // Find the token at the cursor position
        Token? targetToken = null;
        foreach (var token in tokens)
        {
            if (token.Extent.StartOffset <= cursorPosition && token.Extent.EndOffset >= cursorPosition)
            {
                targetToken = token;
                break;
            }
        }

        if (targetToken is null || targetToken.Kind == TokenKind.NewLine ||
            targetToken.Kind == TokenKind.EndOfInput)
            return null;

        // Find the AST node at the cursor position
        var visitor = new CursorAstVisitor(cursorPosition);
        ast.Visit(visitor);
        var node = visitor.FoundNode;

        string content;
        if (node is CommandAst cmdAst)
        {
            content = $"Command: {cmdAst.GetCommandName()}";
        }
        else if (node is VariableExpressionAst varAst)
        {
            content = $"Variable: ${varAst.VariablePath.UserPath}";
        }
        else if (node is MemberExpressionAst memberAst)
        {
            content = $"Member: {memberAst.Member.Extent.Text}";
        }
        else if (node is not null)
        {
            content = $"{node.GetType().Name.Replace("Ast", "")}: {targetToken.Text}";
        }
        else
        {
            content = targetToken.Text;
        }

        return new HoverInfo(
            content,
            "text/plain",
            (targetToken.Extent.StartLineNumber - 1,
             targetToken.Extent.StartColumnNumber - 1,
             targetToken.Extent.EndLineNumber - 1,
             targetToken.Extent.EndColumnNumber - 1));
    }

    private static bool HasFormatObjects(Collection<PSObject> results)
    {
        foreach (var obj in results)
        {
            var ns = obj?.BaseObject?.GetType().Namespace;
            if (ns is not null && ns.StartsWith("Microsoft.PowerShell.Commands.Internal.Format"))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_runspace is not null)
        {
            try
            {
                if (_runspace.RunspaceStateInfo.State == RunspaceState.Opened)
                    _runspace.Close();
            }
            catch { /* best effort */ }

            _runspace.Dispose();
            _runspace = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RunspaceManager));
    }

    private sealed class CursorAstVisitor : AstVisitor
    {
        private readonly int _cursorOffset;

        public CursorAstVisitor(int cursorOffset) => _cursorOffset = cursorOffset;

        public Ast? FoundNode { get; private set; }

        public override AstVisitAction DefaultVisit(Ast ast)
        {
            if (ast.Extent.StartOffset <= _cursorOffset && ast.Extent.EndOffset >= _cursorOffset)
            {
                FoundNode = ast;
                return AstVisitAction.Continue; // Keep drilling into children
            }

            return AstVisitAction.SkipChildren;
        }
    }
}
