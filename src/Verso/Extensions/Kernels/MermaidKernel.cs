using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Extensions.Utilities;

namespace Verso.Extensions.Kernels;

/// <summary>
/// Language kernel for Mermaid diagram cells. Applies <c>@variable</c> substitution and
/// returns the result as <c>text/x-verso-mermaid</c> for specialized rendering on each surface.
/// Accessed through <see cref="CellTypes.MermaidCellType"/>; not independently registered.
/// </summary>
public sealed class MermaidKernel : ILanguageKernel
{
    private IVariableStore? _lastVariableStore;

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.mermaid";
    public string Name => "Mermaid Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes Mermaid diagram cells with @variable substitution.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILanguageKernel ---

    public string LanguageId => "mermaid";
    public string DisplayName => "Mermaid";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".mmd", ".mermaid" };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        _lastVariableStore = context.Variables;

        var result = VariableSubstitution.Apply(code, context.Variables);
        var outputs = new List<CellOutput>
        {
            new CellOutput("text/x-verso-mermaid", result)
        };

        return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>();
        var partial = ExtractPartialWord(code, cursorPosition);

        // Diagram type keyword completions
        foreach (var kw in DiagramKeywords)
        {
            if (MatchesPrefix(kw, partial))
            {
                completions.Add(new Completion(
                    kw, kw, "Keyword", null, $"1_{kw}"));
            }
        }

        // @variable completions
        if (_lastVariableStore is not null)
        {
            foreach (var v in _lastVariableStore.GetAll())
            {
                if (v.Name.StartsWith("__verso_", StringComparison.Ordinal))
                    continue;

                var varName = $"@{v.Name}";
                if (MatchesPrefix(varName, partial) || MatchesPrefix(v.Name, partial))
                {
                    completions.Add(new Completion(
                        varName,
                        varName,
                        "Variable",
                        $"{v.Type.Name}: {TruncateValue(v.Value)}",
                        $"0_{v.Name}"));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<Completion>>(completions);
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();

        if (_lastVariableStore is null)
            return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);

        var unresolved = VariableSubstitution.FindUnresolved(code, _lastVariableStore);
        foreach (var (name, offset, length) in unresolved)
        {
            var (startLine, startCol) = OffsetToLineCol(code, offset);
            var (endLine, endCol) = OffsetToLineCol(code, offset + length);

            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                $"Unresolved variable '@{name}'. No matching variable found in the variable store.",
                startLine, startCol, endLine, endCol));
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var (word, wordStart, wordEnd) = ExtractWordAtCursor(code, cursorPosition);
        if (string.IsNullOrEmpty(word) || !word.StartsWith('@') || _lastVariableStore is null)
            return Task.FromResult<HoverInfo?>(null);

        var varName = word.Substring(1);
        var allVars = _lastVariableStore.GetAll();
        var descriptor = allVars.FirstOrDefault(v =>
            string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
            return Task.FromResult<HoverInfo?>(null);

        var (startLine, startCol) = OffsetToLineCol(code, wordStart);
        var (endLine, endCol) = OffsetToLineCol(code, wordEnd);

        var content = $"Variable @{descriptor.Name}\nType: {descriptor.Type.Name}\nValue: {TruncateValue(descriptor.Value)}";
        return Task.FromResult<HoverInfo?>(new HoverInfo(content, "text/plain",
            (startLine, startCol, endLine, endCol)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- Private helpers ---

    private static string ExtractPartialWord(string code, int cursorPosition)
    {
        if (cursorPosition <= 0 || cursorPosition > code.Length)
            return "";

        int start = cursorPosition - 1;
        while (start >= 0 && IsWordChar(code[start]))
            start--;

        start++;
        return code.Substring(start, cursorPosition - start);
    }

    private static (string Word, int Start, int End) ExtractWordAtCursor(string code, int cursorPosition)
    {
        if (cursorPosition < 0 || cursorPosition > code.Length || code.Length == 0)
            return ("", 0, 0);

        int pos = cursorPosition < code.Length ? cursorPosition : cursorPosition - 1;
        if (pos < 0 || (!IsWordChar(code[pos]) && code[pos] != '@'))
        {
            pos = cursorPosition - 1;
            if (pos < 0 || (!IsWordChar(code[pos]) && code[pos] != '@'))
                return ("", 0, 0);
        }

        int start = pos;
        int end = pos;

        while (start > 0 && (IsWordChar(code[start - 1]) || code[start - 1] == '@'))
            start--;

        while (end < code.Length - 1 && IsWordChar(code[end + 1]))
            end++;

        return (code.Substring(start, end - start + 1), start, end + 1);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '@';

    private static bool MatchesPrefix(string candidate, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return true;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Line, int Column) OffsetToLineCol(string text, int offset)
    {
        int line = 0;
        int col = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }
        return (line, col);
    }

    private static string TruncateValue(object? value, int maxLength = 100)
    {
        if (value is null) return "null";
        var str = value.ToString() ?? "null";
        return str.Length > maxLength ? str.Substring(0, maxLength) + "..." : str;
    }

    // --- Mermaid diagram type keywords ---

    internal static readonly string[] DiagramKeywords = new[]
    {
        "graph", "flowchart", "sequenceDiagram", "classDiagram", "stateDiagram",
        "stateDiagram-v2", "erDiagram", "journey", "gantt", "pie", "quadrantChart",
        "requirementDiagram", "gitgraph", "mindmap", "timeline", "sankey-beta",
        "xychart-beta", "block-beta",
        "subgraph", "end",
        "TD", "TB", "BT", "RL", "LR",
        "participant", "actor", "activate", "deactivate", "note", "loop", "alt", "opt", "par",
        "class", "direction"
    };
}
