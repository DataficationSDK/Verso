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
/// Each successfully executed cell becomes a script submission chained to the one before it, which is
/// how the scripting engine runs them. A cell that declares a name an earlier cell already declared
/// shadows it, so running a cell and then editing it does not leave the name declared twice.
/// </summary>
internal sealed class RoslynWorkspaceManager : IDisposable
{
    private readonly object _gate = new();
    private readonly AdhocWorkspace _workspace;
    private readonly CSharpCompilationOptions _compilationOptions;
    private readonly CSharpParseOptions _parseOptions;
    private readonly List<MetadataReference> _references;
    private ProjectId? _lastSubmissionId;
    private int _submissionCount;

    public RoslynWorkspaceManager(IReadOnlyList<string> defaultImports, IEnumerable<MetadataReference> references)
    {
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

        _references = references.ToList();
        _parseOptions = new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script);
        _compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            usings: defaultImports);
    }

    /// <summary>
    /// Adds metadata references from assembly paths so that future intellisense operations
    /// can resolve types from those assemblies.
    /// </summary>
    public void AddReferences(IEnumerable<string> assemblyPaths)
    {
        var refs = assemblyPaths
            .Where(File.Exists)
            .Select(p => MetadataReference.CreateFromFile(p))
            .ToArray();

        if (refs.Length == 0) return;

        lock (_gate)
        {
            _references.AddRange(refs);
        }
    }

    /// <summary>
    /// Records a successfully executed cell source for future intellisense context.
    /// </summary>
    public void AppendExecutedCode(string code)
    {
        lock (_gate)
        {
            var submission = CreateSubmission(code, out _);
            _workspace.AddProject(submission);
            _lastSubmissionId = submission.Id;
        }
    }

    /// <summary>
    /// Builds a document holding only the current cell, as a submission that follows every
    /// executed cell. Positions in the document are positions in the cell.
    /// </summary>
    private Document BuildDocument(string currentCode)
    {
        lock (_gate)
        {
            // The submission is added to a fork of the solution and never applied, so an
            // in-progress cell leaves nothing behind in the workspace.
            var submission = CreateSubmission(currentCode, out var documentId);
            return _workspace.CurrentSolution.AddProject(submission).GetDocument(documentId)!;
        }
    }

    private ProjectInfo CreateSubmission(string code, out DocumentId documentId)
    {
        var name = $"Submission#{++_submissionCount}";
        var projectId = ProjectId.CreateNewId(name);
        documentId = DocumentId.CreateNewId(projectId, name);

        var document = DocumentInfo.Create(
            documentId,
            $"{name}.csx",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Create())));

        return ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            compilationOptions: _compilationOptions.WithScriptClassName(name),
            parseOptions: _parseOptions,
            documents: new[] { document },
            projectReferences: _lastSubmissionId is null
                ? null
                : new[] { new ProjectReference(_lastSubmissionId) },
            metadataReferences: _references.ToArray(),
            isSubmission: true);
    }

    /// <summary>
    /// Gets completions for the given code at the specified cursor position.
    /// </summary>
    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var document = BuildDocument(code);

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<Completion>();

        var completions = await completionService.GetCompletionsAsync(document, cursorPosition)
            .ConfigureAwait(false);

        if (completions is null) return Array.Empty<Completion>();

        var results = new List<Completion>();
        foreach (var item in completions.ItemsList)
        {
            // The front ends insert the display text and nothing else. An item that needs a wider
            // edit, such as an extension method that must also add its using directive, would
            // leave code that does not compile.
            if (item.IsComplexTextEdit) continue;

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
    /// Gets diagnostics for the given code.
    /// </summary>
    public async Task<IReadOnlyList<VersoDiagnostic>> GetDiagnosticsAsync(string code)
    {
        var document = BuildDocument(code);

        var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (semanticModel is null) return Array.Empty<VersoDiagnostic>();

        var results = new List<VersoDiagnostic>();
        foreach (var diag in semanticModel.GetDiagnostics())
        {
            if (diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden) continue;

            var span = diag.Location.GetLineSpan();
            if (!span.IsValid) continue;

            var startLine = span.StartLinePosition.Line;
            var endLine = span.EndLinePosition.Line;

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
                StartLine: startLine,
                StartColumn: span.StartLinePosition.Character,
                EndLine: endLine,
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
        var document = BuildDocument(code);

        var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        if (root is null) return null;

        var token = root.FindToken(cursorPosition);
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

        var tokenSpan = token.Span;
        var tokenLineSpan = sourceText.Lines.GetLinePositionSpan(tokenSpan);

        var range = (
            StartLine: tokenLineSpan.Start.Line,
            StartColumn: tokenLineSpan.Start.Character,
            EndLine: tokenLineSpan.End.Line,
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
