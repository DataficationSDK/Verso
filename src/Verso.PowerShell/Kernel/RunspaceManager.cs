using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Text;
using Verso.Abstractions;

namespace Verso.PowerShell.Kernel;

internal sealed record InvokeResult(
    IReadOnlyList<string> OutputLines,
    string OutputMimeType,
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
        var outputMimeType = "text/plain";
        var errorLines = new List<string>();
        var warningLines = new List<string>();
        var informationLines = new List<string>();
        Exception? exception = null;

        try
        {
            Collection<PSObject> results = ps.Invoke();

            if (results.Count > 0 && HasFormatObjects(results))
            {
                // Explicit Format-Table / Format-List: render through Out-String
                // then parse the text table into HTML for consistent display.
                using var renderer = System.Management.Automation.PowerShell.Create();
                renderer.Runspace = runspace;
                renderer.AddCommand("Out-String").AddParameter("Width", 200);
                var rendered = renderer.Invoke(results);
                // Out-String returns a single multi-line string; split into individual lines.
                var lines = rendered
                    .SelectMany(r => (r?.ToString() ?? "").Split('\n'))
                    .Select(s => s.TrimEnd())
                    .Where(s => s.Length > 0)
                    .ToList();
                if (lines.Count > 0)
                {
                    var html = ParseTextTableToHtml(lines);
                    if (html is not null)
                    {
                        outputLines.Add(html);
                        outputMimeType = "text/html";
                    }
                    else
                    {
                        // Not a parseable table (e.g. Format-List) - wrap as styled <pre>
                        var sb = new StringBuilder();
                        AppendPreStyles(sb);
                        sb.Append("<div class=\"verso-ps-result\">");
                        sb.Append("<pre class=\"verso-ps-pre\">")
                          .Append(WebUtility.HtmlEncode(string.Join(Environment.NewLine, lines)))
                          .Append("</pre>");
                        sb.Append("</div>");
                        outputLines.Add(sb.ToString());
                        outputMimeType = "text/html";
                    }
                }
            }
            else if (results.Count > 0 && HasComplexObjects(results))
            {
                // Complex objects: render as HTML table using Select-Object for
                // reliable property resolution through PowerShell's ETS.
                var html = RenderObjectsAsHtml(results, runspace);
                outputLines.Add(html);
                outputMimeType = "text/html";
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

        return new InvokeResult(outputLines, outputMimeType, errorLines, warningLines, informationLines, exception);
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

    /// <summary>
    /// Parses Out-String text table output (from Format-Table) into an HTML table.
    /// Returns null if the text doesn't match the expected header/dashes/data pattern,
    /// so the caller can fall back to &lt;pre&gt; rendering (e.g. for Format-List).
    /// </summary>
    private static string? ParseTextTableToHtml(List<string> lines)
    {
        // Format-Table via Out-String produces:
        //   Header1  Header2  Header3      (column names, space-padded)
        //   -------  -------  -------      (dashes under each column)
        //   Value1   Value2   Value3       (data rows)
        if (lines.Count < 2) return null;

        // Find the dashes line (first line that is only dashes and spaces)
        int dashesIndex = -1;
        for (int i = 0; i < Math.Min(lines.Count, 3); i++)
        {
            if (lines[i].Length > 0 && lines[i].All(c => c == '-' || c == ' '))
            {
                dashesIndex = i;
                break;
            }
        }

        if (dashesIndex < 1) return null;

        var headerLine = lines[dashesIndex - 1];
        var dashesLine = lines[dashesIndex];

        // Parse dash groups: each contiguous run of dashes marks a column header.
        var dashGroups = new List<(int Start, int End, string Name)>();
        int pos = 0;
        while (pos < dashesLine.Length)
        {
            while (pos < dashesLine.Length && dashesLine[pos] == ' ') pos++;
            if (pos >= dashesLine.Length) break;

            int start = pos;
            while (pos < dashesLine.Length && dashesLine[pos] == '-') pos++;
            int end = pos;

            var name = start < headerLine.Length
                ? headerLine.Substring(start, Math.Min(end, headerLine.Length) - start).Trim()
                : "";
            dashGroups.Add((start, end, name));
        }

        if (dashGroups.Count == 0) return null;

        // Compute extraction boundaries using the MIDPOINT of each gap between
        // dash groups. Right-aligned numeric values extend left into the gap,
        // so the dashes-start is not a reliable column boundary for data rows.
        // Using midpoints ensures values stay within their column's range.
        var extractionBounds = new List<(int Start, int End)>();
        for (int c = 0; c < dashGroups.Count; c++)
        {
            int extractStart = c == 0
                ? 0
                : (dashGroups[c - 1].End + dashGroups[c].Start) / 2;
            int extractEnd = c == dashGroups.Count - 1
                ? int.MaxValue
                : (dashGroups[c].End + dashGroups[c + 1].Start) / 2;
            extractionBounds.Add((extractStart, extractEnd));
        }

        var sb = new StringBuilder();
        AppendTableStyles(sb);
        sb.Append("<div class=\"verso-ps-result\">");
        sb.Append("<table><thead><tr>");
        foreach (var (_, _, name) in dashGroups)
            sb.Append("<th>").Append(WebUtility.HtmlEncode(name)).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        int dataRowCount = 0;
        for (int i = dashesIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            dataRowCount++;
            sb.Append("<tr>");
            for (int c = 0; c < extractionBounds.Count; c++)
            {
                sb.Append("<td>");
                var (eStart, eEnd) = extractionBounds[c];
                if (eStart < line.Length)
                {
                    int actualEnd = Math.Min(eEnd, line.Length);
                    var cellValue = line[eStart..actualEnd].Trim();
                    sb.Append(WebUtility.HtmlEncode(cellValue));
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<div class=\"verso-ps-footer\">")
          .Append(dataRowCount.ToString("N0"))
          .Append(" object(s)</div>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private static void AppendTableStyles(StringBuilder sb)
    {
        sb.Append("<style>");
        sb.Append(".verso-ps-result{");
        sb.Append("--ps-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--ps-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("--ps-border:var(--vscode-editorWidget-border,var(--verso-border-default,#e0e0e0));");
        sb.Append("--ps-header-bg:var(--vscode-editorWidget-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("--ps-hover:var(--vscode-list-hoverBackground,var(--verso-cell-hover-background,#f0f0f0));");
        sb.Append("--ps-muted:var(--vscode-descriptionForeground,var(--verso-editor-line-number,#858585));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;color:var(--ps-fg);}");
        sb.Append(".verso-ps-result table{border-collapse:collapse;width:auto;background:var(--ps-bg);color:var(--ps-fg);}");
        sb.Append(".verso-ps-result th{text-align:left;padding:6px 12px;border-bottom:2px solid var(--ps-border);background:var(--ps-header-bg);font-weight:600;}");
        sb.Append(".verso-ps-result td{padding:5px 12px;border-bottom:1px solid var(--ps-border);}");
        sb.Append(".verso-ps-result tbody tr:hover{background:var(--ps-hover);}");
        sb.Append(".verso-ps-result .verso-ps-null{color:var(--ps-muted);font-style:italic;}");
        sb.Append(".verso-ps-result .verso-ps-footer{padding:6px 0;color:var(--ps-muted);font-size:12px;}");
        sb.Append("</style>");
    }

    private static void AppendPreStyles(StringBuilder sb)
    {
        sb.Append("<style>");
        sb.Append(".verso-ps-result{");
        sb.Append("--ps-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--ps-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;color:var(--ps-fg);}");
        sb.Append(".verso-ps-pre{margin:0;white-space:pre;font-family:inherit;font-size:inherit;line-height:1.4;}");
        sb.Append("</style>");
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

    private static bool HasComplexObjects(Collection<PSObject> results)
    {
        foreach (var obj in results)
        {
            if (obj is null) continue;
            var baseObj = obj.BaseObject;
            if (baseObj is string) continue;
            if (baseObj.GetType().IsValueType) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Renders a collection of PSObjects as an HTML table using the same styling
    /// conventions as the SQL result set formatter. Uses PowerShell's
    /// <c>DefaultDisplayPropertySet</c> when available to select columns, falling
    /// back to the object's public properties.
    /// </summary>
    private static string RenderObjectsAsHtml(Collection<PSObject> results, Runspace runspace)
    {
        // Resolve display properties: use DefaultDisplayPropertySet if defined,
        // otherwise fall back to the properties of the first non-null object.
        var columnNames = GetDisplayProperties(results);
        if (columnNames.Count == 0)
        {
            // No properties to display; fall back to Out-String
            using var renderer = System.Management.Automation.PowerShell.Create();
            renderer.Runspace = runspace;
            renderer.AddCommand("Out-String").AddParameter("Width", 200);
            var rendered = renderer.Invoke(results);
            return WebUtility.HtmlEncode(string.Join(Environment.NewLine,
                rendered.Select(r => r?.ToString()?.TrimEnd()).Where(s => !string.IsNullOrEmpty(s))));
        }

        var sb = new StringBuilder();
        AppendTableStyles(sb);

        sb.Append("<div class=\"verso-ps-result\">");

        // Header
        sb.Append("<table><thead><tr>");
        foreach (var col in columnNames)
            sb.Append("<th>").Append(WebUtility.HtmlEncode(col)).Append("</th>");
        sb.Append("</tr></thead>");

        // Resolve property values through PowerShell's ETS (Extended Type System)
        // so that ScriptProperties (e.g. Process.CPU) evaluate correctly.
        using var selector = System.Management.Automation.PowerShell.Create();
        selector.Runspace = runspace;
        selector.AddCommand("Select-Object").AddParameter("Property", columnNames.ToArray());
        var resolved = selector.Invoke(results);

        // Body
        sb.Append("<tbody>");
        foreach (var obj in resolved)
        {
            if (obj is null) continue;
            sb.Append("<tr>");
            foreach (var col in columnNames)
            {
                sb.Append("<td>");
                var prop = obj.Properties[col];
                if (prop is null)
                {
                    sb.Append("<span class=\"verso-ps-null\"></span>");
                }
                else
                {
                    try
                    {
                        var val = prop.Value;
                        if (val is null)
                            sb.Append("<span class=\"verso-ps-null\">$null</span>");
                        else
                            sb.Append(WebUtility.HtmlEncode(val.ToString() ?? ""));
                    }
                    catch
                    {
                        sb.Append("<span class=\"verso-ps-null\">N/A</span>");
                    }
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        // Footer
        sb.Append("<div class=\"verso-ps-footer\">")
          .Append(resolved.Count(o => o is not null).ToString("N0"))
          .Append(" object(s)</div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Resolves the display property names for a collection of PSObjects.
    /// Checks <c>DefaultDisplayPropertySet</c> first (matches PowerShell's
    /// default table/list column selection), then falls back to the properties
    /// of the first non-null object.
    /// </summary>
    private static IReadOnlyList<string> GetDisplayProperties(Collection<PSObject> results)
    {
        foreach (var obj in results)
        {
            if (obj is null) continue;

            // Try DefaultDisplayPropertySet (defined in .ps1xml format files)
            var members = obj.Members["PSStandardMembers"];
            if (members?.Value is PSMemberSet memberSet)
            {
                var ddps = memberSet.Members["DefaultDisplayPropertySet"];
                if (ddps?.Value is PSPropertySet propSet && propSet.ReferencedPropertyNames.Count > 0)
                    return propSet.ReferencedPropertyNames.ToList();
            }

            // Fall back to all properties of the first object
            var props = obj.Properties.Select(p => p.Name).ToList();
            if (props.Count > 0) return props;
        }

        return Array.Empty<string>();
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
