using System.Composition.Hosting;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Verso.Abstractions;

using VersoDiagnostic = Verso.Abstractions.Diagnostic;
using VersoDiagnosticSeverity = Verso.Abstractions.DiagnosticSeverity;

namespace Verso.Kernels;

/// <summary>
/// Manages an <see cref="AdhocWorkspace"/> for intellisense operations (completions, diagnostics, hover).
/// Maintains a history of successfully executed cell sources and builds combined documents for analysis.
/// </summary>
internal sealed class RoslynWorkspaceManager : IDisposable
{
    private readonly List<string> _executedSources = new();
    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private readonly IReadOnlyList<string> _defaultImports;
    private int _documentVersion;

    public RoslynWorkspaceManager(IReadOnlyList<string> defaultImports, IEnumerable<MetadataReference> references)
    {
        _defaultImports = defaultImports;

        var assemblies = MefHostServices.DefaultAssemblies
            .Concat(new[]
            {
                typeof(CompletionService).Assembly,                                                 // Microsoft.CodeAnalysis.Features
                typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly,  // Microsoft.CodeAnalysis.CSharp.Workspaces
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")                             // CSharp-specific completion providers
            })
            .Distinct()
            .ToList();

        var host = MefHostServices.Create(assemblies);
        _workspace = new AdhocWorkspace(host);

        _projectId = ProjectId.CreateNewId("CSharpKernelProject");

        var projectInfo = ProjectInfo.Create(
            _projectId,
            VersionStamp.Default,
            "CSharpKernelProject",
            "CSharpKernelProject",
            LanguageNames.CSharp,
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script),
            metadataReferences: references);

        _workspace.AddProject(projectInfo);
    }

    /// <summary>
    /// Records a successfully executed cell source for future intellisense context.
    /// </summary>
    public void AppendExecutedCode(string code)
    {
        _executedSources.Add(code);
    }

    /// <summary>
    /// Builds a combined document from all previous cell sources plus the current code,
    /// and returns the document along with the offset where the current cell begins.
    /// </summary>
    public (Document Document, int PrefixLength) BuildDocument(string currentCode)
    {
        var prefixBuilder = new System.Text.StringBuilder();

        foreach (var import in _defaultImports)
        {
            prefixBuilder.AppendLine($"using {import};");
        }

        foreach (var source in _executedSources)
        {
            prefixBuilder.AppendLine(source);
        }

        var prefix = prefixBuilder.ToString();
        var combinedSource = prefix + currentCode;

        var documentId = DocumentId.CreateNewId(_projectId);
        var documentInfo = DocumentInfo.Create(
            documentId,
            $"Cell_{++_documentVersion}.csx",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(
                SourceText.From(combinedSource), VersionStamp.Create())));

        var solution = _workspace.CurrentSolution.AddDocument(documentInfo);
        var document = solution.GetDocument(documentId)!;

        return (document, prefix.Length);
    }

    /// <summary>
    /// Gets completions for the given code at the specified cursor position.
    /// </summary>
    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var (document, prefixLength) = BuildDocument(code);
        var adjustedPosition = prefixLength + cursorPosition;

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<Completion>();

        var completions = await completionService.GetCompletionsAsync(document, adjustedPosition)
            .ConfigureAwait(false);

        if (completions is null) return Array.Empty<Completion>();

        var results = new List<Completion>();
        foreach (var item in completions.ItemsList)
        {
            var kind = MapCompletionKind(item);
            results.Add(new Completion(
                DisplayText: item.DisplayText,
                InsertText: item.DisplayText,
                Kind: kind,
                Description: null,
                SortText: item.SortText));
        }

        return results;
    }

    /// <summary>
    /// Gets diagnostics for the given code, filtering to only those within the current cell.
    /// </summary>
    public async Task<IReadOnlyList<VersoDiagnostic>> GetDiagnosticsAsync(string code)
    {
        var (document, prefixLength) = BuildDocument(code);

        var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (semanticModel is null) return Array.Empty<VersoDiagnostic>();

        var sourceText = await document.GetTextAsync().ConfigureAwait(false);
        var prefixLines = sourceText.Lines.GetLineFromPosition(prefixLength).LineNumber;

        var results = new List<VersoDiagnostic>();
        foreach (var diag in semanticModel.GetDiagnostics())
        {
            if (diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden) continue;

            var span = diag.Location.GetLineSpan();
            if (!span.IsValid) continue;

            var startLine = span.StartLinePosition.Line;
            var endLine = span.EndLinePosition.Line;

            // Filter to diagnostics within the current cell's span
            if (startLine < prefixLines) continue;

            var severity = diag.Severity switch
            {
                Microsoft.CodeAnalysis.DiagnosticSeverity.Info => VersoDiagnosticSeverity.Info,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => VersoDiagnosticSeverity.Warning,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error => VersoDiagnosticSeverity.Error,
                _ => VersoDiagnosticSeverity.Hidden
            };

            results.Add(new VersoDiagnostic(
                Severity: severity,
                Message: diag.GetMessage(),
                StartLine: startLine - prefixLines,
                StartColumn: span.StartLinePosition.Character,
                EndLine: endLine - prefixLines,
                EndColumn: span.EndLinePosition.Character,
                Code: diag.Id));
        }

        return results;
    }

    /// <summary>
    /// Gets hover information for the symbol at the specified cursor position.
    /// </summary>
    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var (document, prefixLength) = BuildDocument(code);
        var adjustedPosition = prefixLength + cursorPosition;

        var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        if (root is null) return null;

        var token = root.FindToken(adjustedPosition);
        if (token.Span.Length == 0) return null;

        var symbolInfo = semanticModel.GetSymbolInfo(token.Parent!);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(token.Parent!);

        if (symbol is null)
        {
            // Try type info for expressions
            var typeInfo = semanticModel.GetTypeInfo(token.Parent!);
            if (typeInfo.Type is not null)
            {
                symbol = typeInfo.Type;
            }
        }

        if (symbol is null) return null;

        var displayString = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var xmlDocs = symbol.GetDocumentationCommentXml();
        var description = displayString;

        if (!string.IsNullOrWhiteSpace(xmlDocs))
        {
            var summary = ExtractXmlSummary(xmlDocs);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                description = $"{displayString}\n{summary}";
            }
        }

        var sourceText = await document.GetTextAsync().ConfigureAwait(false);
        var prefixLines = sourceText.Lines.GetLineFromPosition(prefixLength).LineNumber;

        var tokenSpan = token.Span;
        var tokenLineSpan = sourceText.Lines.GetLinePositionSpan(tokenSpan);

        var range = (
            StartLine: tokenLineSpan.Start.Line - prefixLines,
            StartColumn: tokenLineSpan.Start.Character,
            EndLine: tokenLineSpan.End.Line - prefixLines,
            EndColumn: tokenLineSpan.End.Character);

        return new HoverInfo(description, Range: range);
    }

    private static string MapCompletionKind(CompletionItem item)
    {
        if (item.Tags.Length == 0) return "Text";

        var tag = item.Tags[0];
        return tag switch
        {
            "Method" or "ExtensionMethod" => "Method",
            "Property" => "Property",
            "Field" => "Field",
            "Local" or "Parameter" => "Variable",
            "Class" or "Record" => "Class",
            "Struct" or "Structure" => "Struct",
            "Interface" => "Interface",
            "Enum" => "Enum",
            "EnumMember" => "EnumMember",
            "Namespace" => "Namespace",
            "Keyword" => "Keyword",
            "Event" => "Event",
            "Delegate" => "Delegate",
            "Constant" => "Constant",
            _ => "Text"
        };
    }

    private static string ExtractXmlSummary(string xml)
    {
        try
        {
            var startTag = "<summary>";
            var endTag = "</summary>";
            var start = xml.IndexOf(startTag, StringComparison.Ordinal);
            var end = xml.IndexOf(endTag, StringComparison.Ordinal);
            if (start < 0 || end < 0) return "";
            start += startTag.Length;
            return xml.Substring(start, end - start).Trim();
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }
}
