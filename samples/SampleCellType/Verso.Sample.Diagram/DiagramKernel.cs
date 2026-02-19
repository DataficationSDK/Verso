using Verso.Abstractions;
using Verso.Sample.Diagram.Models;
using Verso.Sample.Diagram.Parsing;
using Verso.Sample.Diagram.Rendering;

namespace Verso.Sample.Diagram;

/// <summary>
/// Language kernel that parses arrow notation and renders SVG flowcharts.
/// Stores the parsed graph in the variable store for downstream consumption.
/// </summary>
[VersoExtension]
public sealed class DiagramKernel : ILanguageKernel
{
    public string ExtensionId => "com.verso.sample.diagram.kernel";
    public string Name => "Diagram Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Parses arrow notation and renders SVG flowcharts.";
    public string LanguageId => "diagram";
    public string DisplayName => "Diagram";
    public IReadOnlyList<string> FileExtensions => new[] { ".diagram" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        var outputs = new List<CellOutput>();

        // Check for syntax errors first
        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ArrowNotationParser.IsValidLine(lines[i]))
            {
                outputs.Add(new CellOutput("text/plain",
                    $"Syntax error on line {i + 1}: {lines[i].Trim()}", IsError: true));
                return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
            }
        }

        var graph = ArrowNotationParser.Parse(code);

        if (graph.Nodes.Count == 0)
        {
            outputs.Add(new CellOutput("text/plain", "No diagram elements found.", IsError: true));
            return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
        }

        // Store graph for variable sharing
        context.Variables.Set("_lastGraph", graph);

        var svg = SvgFlowchartRenderer.Render(graph);
        outputs.Add(new CellOutput("image/svg+xml", svg));

        return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>
        {
            new("-->", "-->", "Snippet", "Solid arrow"),
            new("---", "---", "Snippet", "Solid line (no arrow)"),
            new("<-->", "<-->", "Snippet", "Bidirectional arrow"),
            new("-.->", "-.->", "Snippet", "Dashed arrow"),
            new("==>", "==>", "Snippet", "Thick arrow")
        };
        return Task.FromResult<IReadOnlyList<Completion>>(completions);
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();
        var lines = code.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (!ArrowNotationParser.IsValidLine(lines[i]))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Invalid arrow notation: '{lines[i].Trim()}'. Use format: Source --> Target",
                    i, 0, i, lines[i].TrimEnd().Length));
            }
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var graph = ArrowNotationParser.Parse(code);
        if (graph.Nodes.Count == 0)
            return Task.FromResult<HoverInfo?>(null);

        return Task.FromResult<HoverInfo?>(
            new HoverInfo($"Diagram: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
