using FSharp.Compiler.CodeAnalysis;
using FSharp.Compiler.EditorServices;
using FSharp.Compiler.Symbols;
using FSharp.Compiler.Tokenization;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using System.Reflection;
using Verso.Abstractions;
using Verso.FSharp.Helpers;
using Verso.FSharp.NuGet;

using FcsDiagnostic = FSharp.Compiler.Diagnostics.FSharpDiagnostic;

namespace Verso.FSharp.Kernel;

/// <summary>
/// F# Interactive language kernel for Verso notebooks.
/// Powered by FSharp.Compiler.Service (<c>FsiEvaluationSession</c>).
/// </summary>
[VersoExtension]
public sealed class FSharpKernel : ILanguageKernel, IExtensionSettings
{
    private const string VirtualFileName = "/verso/notebook.fsx";

    /// <summary>
    /// FCS diagnostic codes to suppress in IntelliSense (incomplete-input noise in notebook context).
    /// </summary>
    private static readonly HashSet<int> SuppressedDiagnosticCodes = new() { 10, 588, 3118 };

    private FSharpKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private FsiSessionManager? _sessionManager;
    private VariableBridge? _variableBridge;
    private FSharpCheckerManager? _checkerManager;
    private FSharpProjectContext? _projectContext;
    private NuGetReferenceProcessor? _nugetProcessor;
    private ScriptDirectiveProcessor? _scriptDirectiveProcessor;
    private bool _variablesInjected;
    private bool _initialized;
    private bool _disposed;

    public FSharpKernel() : this(new FSharpKernelOptions()) { }

    internal FSharpKernel(FSharpKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- IExtension ---

    public string ExtensionId => "verso.fsharp.kernel";
    public string Name => "F# (Interactive)";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "F# language kernel powered by FSharp.Compiler.Service.";

    // --- ILanguageKernel ---

    public string LanguageId => "fsharp";
    public string DisplayName => "F# (Interactive)";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".fs", ".fsx" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IExtensionSettings ---

    public IReadOnlyList<SettingDefinition> SettingDefinitions { get; } = new[]
    {
        new SettingDefinition("warningLevel", "Warning Level",
            "F# compiler warning level (0\u20135).",
            SettingType.Integer, 3, "Compiler",
            new SettingConstraints(MinValue: 0, MaxValue: 5)),
        new SettingDefinition("langVersion", "Language Version",
            "F# language version for the session.",
            SettingType.StringChoice, "preview", "Compiler",
            new SettingConstraints(Choices: new[] { "default", "latest", "latestmajor", "preview", "5.0", "6.0", "7.0", "8.0", "9.0" })),
        new SettingDefinition("publishPrivateBindings", "Publish Private Bindings",
            "Whether to publish underscore-prefixed bindings to the variable store.",
            SettingType.Boolean, false, "Variables"),
        new SettingDefinition("maxCollectionDisplay", "Max Collection Display",
            "Maximum number of collection elements to display in formatted output.",
            SettingType.Integer, 100, "Display",
            new SettingConstraints(MinValue: 10, MaxValue: 10000)),
    };

    public IReadOnlyDictionary<string, object?> GetSettingValues()
    {
        var values = new Dictionary<string, object?>();
        if (_options.WarningLevel != 3) values["warningLevel"] = _options.WarningLevel;
        if (_options.LangVersion != "preview") values["langVersion"] = _options.LangVersion;
        if (_options.PublishPrivateBindings) values["publishPrivateBindings"] = true;
        if (_options.MaxCollectionDisplay != 100) values["maxCollectionDisplay"] = _options.MaxCollectionDisplay;
        return values;
    }

    public Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values)
    {
        _options = ApplyValues(_options, values);
        return Task.CompletedTask;
    }

    public Task OnSettingChangedAsync(string name, object? value)
    {
        _options = ApplyValues(_options, new Dictionary<string, object?> { [name] = value });
        return Task.CompletedTask;
    }

    private static FSharpKernelOptions ApplyValues(
        FSharpKernelOptions current, IReadOnlyDictionary<string, object?> values)
    {
        var result = current;

        if (values.TryGetValue("warningLevel", out var wl) && wl is not null)
            result = result with { WarningLevel = Math.Clamp(Convert.ToInt32(wl), 0, 5) };

        if (values.TryGetValue("langVersion", out var lv) && lv is not null)
            result = result with { LangVersion = lv.ToString()! };

        if (values.TryGetValue("publishPrivateBindings", out var ppb) && ppb is not null)
            result = result with { PublishPrivateBindings = Convert.ToBoolean(ppb) };

        if (values.TryGetValue("maxCollectionDisplay", out var mcd) && mcd is not null)
            result = result with { MaxCollectionDisplay = Math.Clamp(Convert.ToInt32(mcd), 10, 10000) };

        return result;
    }

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

        _sessionManager = new FsiSessionManager();
        _sessionManager.Initialize(_options);

        // Evaluate default open declarations silently
        var opens = _options.DefaultOpens ?? FSharpKernelOptions.DefaultOpenNamespaces;
        foreach (var ns in opens)
        {
            _sessionManager.EvalSilent($"open {ns}");
        }

        // Add Verso.Abstractions reference so IVariableStore API is available in F# cells
        var abstractionsAssembly = typeof(Verso.Abstractions.IVariableStore).Assembly.Location;
        if (!string.IsNullOrEmpty(abstractionsAssembly))
        {
            _sessionManager.EvalSilent($"#r \"{abstractionsAssembly}\"");
        }

        _variableBridge = new VariableBridge(_options);
        _variablesInjected = false;
        _executionLock = new SemaphoreSlim(1, 1);

        // Initialize IntelliSense infrastructure
        _checkerManager = new FSharpCheckerManager();
        _checkerManager.Initialize();

        _projectContext = new FSharpProjectContext(
            opens,
            _sessionManager.ResolvedArgs);

        // Initialize NuGet and script directive processors
        _nugetProcessor = new NuGetReferenceProcessor();
        _nugetProcessor.ProbeNuGetSupport(_sessionManager);
        _scriptDirectiveProcessor = new ScriptDirectiveProcessor();

        _initialized = true;

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            // Inject variables on first execution
            if (!_variablesInjected)
            {
                _variableBridge!.InjectVariables(_sessionManager!, context.Variables);
                _variablesInjected = true;
            }

            var outputs = new List<CellOutput>();
            var processedCode = code;

            // --- NuGet: check magic command results ---
            var magicPaths = _nugetProcessor!.CheckMagicCommandResults(context.Variables);
            foreach (var path in magicPaths)
            {
                _sessionManager!.EvalSilent($"#r \"{path}\"");
                _projectContext?.AddReference(path);
            }

            // --- NuGet: process inline #r "nuget:" directives ---
            var nugetResult = await _nugetProcessor.ProcessAsync(
                processedCode, _sessionManager!, context.CancellationToken).ConfigureAwait(false);
            processedCode = nugetResult.ProcessedCode;

            foreach (var path in nugetResult.NewAssemblyPaths)
            {
                _projectContext?.AddReference(path);
            }

            if (nugetResult.ResolvedPackages.Count > 0)
            {
                var html = FormatInstalledPackagesHtml(nugetResult.ResolvedPackages);
                await context.WriteOutputAsync(new CellOutput("text/html", html)).ConfigureAwait(false);
            }

            // --- Script directives: #r, #load, #I, #nowarn, #time ---
            processedCode = _scriptDirectiveProcessor!.ProcessDirectives(processedCode, context.NotebookMetadata);
            foreach (var path in _scriptDirectiveProcessor.ResolvedAssemblyPaths)
            {
                _projectContext?.AddReference(path);
            }

            // Add #load file contents to IntelliSense context
            foreach (var loadedPath in _scriptDirectiveProcessor.LoadedFilePaths)
            {
                try
                {
                    var loadedSource = File.ReadAllText(loadedPath);
                    _projectContext?.AppendExecutedCode(loadedSource);
                }
                catch { /* best effort — FSI will report its own error */ }
            }

            // --- Snapshot assemblies for FSI-native NuGet detection ---
            HashSet<string>? preEvalAssemblies = null;
            if (_nugetProcessor.UsesFsiNuGet && NuGetReferenceProcessor.ContainsNuGetDirectives(code))
            {
                preEvalAssemblies = new HashSet<string>(
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                        .Select(a => a.Location),
                    StringComparer.OrdinalIgnoreCase);
            }

            var result = _sessionManager!.EvalInteraction(processedCode, context.CancellationToken);

            // --- Detect newly loaded assemblies (FSI-native NuGet path) ---
            if (preEvalAssemblies is not null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location)
                        && !preEvalAssemblies.Contains(asm.Location))
                    {
                        _projectContext?.AddReference(asm.Location);
                    }
                }
            }

            // 1. FSI output (val bindings, type annotations, etc.)
            if (!string.IsNullOrEmpty(result.FsiOutput))
            {
                var fsiCell = new CellOutput("text/plain", result.FsiOutput);
                await context.WriteOutputAsync(fsiCell).ConfigureAwait(false);
                outputs.Add(fsiCell);
            }

            // 2. Console.Out capture
            if (!string.IsNullOrEmpty(result.ConsoleOutput))
            {
                var consoleCell = new CellOutput("text/plain", result.ConsoleOutput);
                await context.WriteOutputAsync(consoleCell).ConfigureAwait(false);
                outputs.Add(consoleCell);
            }

            // 3. Console.Error capture (as error output)
            if (!string.IsNullOrEmpty(result.ConsoleError))
            {
                var errCell = new CellOutput("text/plain", result.ConsoleError, IsError: true, ErrorName: "stderr");
                await context.WriteOutputAsync(errCell).ConfigureAwait(false);
                outputs.Add(errCell);
            }

            // 4. Compilation errors
            if (result.HasCompilationErrors)
            {
                var errorOutput = new CellOutput(
                    "text/plain",
                    result.CompilationErrorText ?? "Compilation error",
                    IsError: true,
                    ErrorName: "CompilationError");
                outputs.Add(errorOutput);
                return outputs;
            }

            // 5. Runtime exception (Choice2Of2)
            if (result.ResultValue is Exception ex)
            {
                var errorOutput = FormatException(ex);
                outputs.Add(errorOutput);
                return outputs;
            }

            // 6. Result value (if any, and not unit)
            if (result.ResultValue is not null)
            {
                // Attempt to resolve async values
                var resolved = await FSharpValueFormatter.ResolveAsyncValue(
                    result.ResultValue, context.CancellationToken).ConfigureAwait(false);

                if (resolved is not null)
                {
                    var formatted = FSharpValueFormatter.FormatValue(resolved);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        var valueCell = new CellOutput("text/plain", formatted);
                        await context.WriteOutputAsync(valueCell).ConfigureAwait(false);
                        outputs.Add(valueCell);
                    }
                }
            }

            // 7. Publish variables to the shared store
            _variableBridge!.PublishVariables(_sessionManager!, context.Variables);

            // 8. Record executed source and pre-warm IntelliSense cache
            _projectContext?.AppendExecutedCode(code);
            if (_projectContext is not null && _checkerManager is not null)
            {
                var (sourceText, _, options) = _projectContext.BuildDocument("");
                _checkerManager.TriggerBackgroundCheck(VirtualFileName, sourceText, options);
            }

            return outputs;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new[] { FormatException(ex) };
        }
        finally
        {
            _executionLock.Release();
        }
    }

    // --- IntelliSense ---

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();

        if (!_initialized || _projectContext is null || _checkerManager is null)
            return Array.Empty<Completion>();

        try
        {
            var (sourceText, prefixLineCount, options) = _projectContext.BuildDocument(code);

            var (line, column) = OffsetToLineColumn(code, cursorPosition);
            // FCS uses 1-based lines
            int adjustedLine = prefixLineCount + line + 1;

            var result = await _checkerManager.ParseAndCheckAsync(VirtualFileName, sourceText, options)
                .ConfigureAwait(false);
            if (result is null) return Array.Empty<Completion>();

            var (parseResults, checkResults) = result.Value;

            // Get the line text at the adjusted position in the combined source
            var sourceLines = sourceText.Split('\n');
            var lineIndex = adjustedLine - 1; // 0-based index into source lines
            if (lineIndex < 0 || lineIndex >= sourceLines.Length)
                return Array.Empty<Completion>();
            var lineText = sourceLines[lineIndex];

            // Get partial name info for completion
            var partialName = QuickParse.GetPartialLongNameEx(lineText, column - 1);

            var declInfo = checkResults.GetDeclarationListInfo(
                FSharpOption<FSharpParseFileResults>.Some(parseResults),
                adjustedLine,
                lineText,
                partialName,
                FSharpOption<FSharpFunc<Unit, FSharpList<AssemblySymbol>>>.None,
                (FSharpOption<Tuple<global::FSharp.Compiler.Text.Position, FSharpOption<CompletionContext>?>>?)null);

            var completions = new List<Completion>();
            foreach (var item in declInfo.Items)
            {
                completions.Add(new Completion(
                    DisplayText: item.NameInList,
                    InsertText: item.NameInCode,
                    Kind: GlyphMapper.MapGlyph(item.Glyph)));
            }

            return completions;
        }
        catch
        {
            return Array.Empty<Completion>();
        }
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        ThrowIfDisposed();

        if (!_initialized || _projectContext is null || _checkerManager is null)
            return Array.Empty<Diagnostic>();

        try
        {
            var (sourceText, prefixLineCount, options) = _projectContext.BuildDocument(code);
            int cellLineCount = CountLines(code);

            var result = await _checkerManager.ParseAndCheckAsync(VirtualFileName, sourceText, options)
                .ConfigureAwait(false);
            if (result is null) return Array.Empty<Diagnostic>();

            var (parseResults, checkResults) = result.Value;

            // Collect diagnostics from both parse and check results
            var allDiagnostics = new List<FcsDiagnostic>();
            allDiagnostics.AddRange(parseResults.Diagnostics);
            allDiagnostics.AddRange(checkResults.Diagnostics);

            var seen = new HashSet<(string Message, int StartLine, int StartColumn)>();
            var diagnostics = new List<Diagnostic>();

            foreach (var diag in allDiagnostics)
            {
                // Skip suppressed codes (incomplete-input noise)
                if (SuppressedDiagnosticCodes.Contains(diag.ErrorNumber))
                    continue;

                // Skip warnings suppressed by #nowarn directives
                if (_scriptDirectiveProcessor is not null
                    && _scriptDirectiveProcessor.SuppressedWarnings.Contains(diag.ErrorNumber))
                    continue;

                var mapped = DiagnosticMapper.MapDiagnostic(diag, prefixLineCount, cellLineCount);
                if (mapped is null) continue;

                // Deduplicate by (message, startLine, startColumn)
                var key = (mapped.Message, mapped.StartLine, mapped.StartColumn);
                if (!seen.Add(key)) continue;

                diagnostics.Add(mapped);
            }

            return diagnostics;
        }
        catch
        {
            return Array.Empty<Diagnostic>();
        }
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();

        if (!_initialized || _projectContext is null || _checkerManager is null)
            return null;

        if (string.IsNullOrWhiteSpace(code) || cursorPosition < 0 || cursorPosition >= code.Length)
            return null;

        try
        {
            var (sourceText, prefixLineCount, options) = _projectContext.BuildDocument(code);

            var (line, column) = OffsetToLineColumn(code, cursorPosition);
            // FCS uses 1-based lines
            int adjustedLine = prefixLineCount + line + 1;

            var result = await _checkerManager.ParseAndCheckAsync(VirtualFileName, sourceText, options)
                .ConfigureAwait(false);
            if (result is null) return null;

            var (_, checkResults) = result.Value;

            // Get the line text at the adjusted position
            var sourceLines = sourceText.Split('\n');
            var lineIndex = adjustedLine - 1;
            if (lineIndex < 0 || lineIndex >= sourceLines.Length) return null;
            var lineText = sourceLines[lineIndex];

            // Find the identifier at the cursor position
            var identInfo = FindIdentifierAtPosition(lineText, column);
            if (identInfo is null) return null;

            var (names, colAtEnd) = identInfo.Value;

            var fsharpNames = ListModule.OfArray(names);
            var toolTip = checkResults.GetToolTip(
                adjustedLine, colAtEnd, lineText, fsharpNames, FSharpTokenTag.Identifier,
                FSharpOption<int>.None);

            var content = FormatToolTip(toolTip);
            if (string.IsNullOrWhiteSpace(content)) return null;

            // Calculate cell-local range for the identifier
            var identStart = FindIdentifierStart(lineText, column);
            var identEnd = FindIdentifierEnd(lineText, column);
            var range = (StartLine: line, StartColumn: identStart, EndLine: line, EndColumn: identEnd);

            return new HoverInfo(content, Range: range);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _initialized = false;

        _checkerManager?.Dispose();
        _checkerManager = null;
        _projectContext?.Reset();
        _projectContext = null;

        _sessionManager?.Dispose();
        _sessionManager = null;
        _variableBridge?.Reset();
        _variableBridge = null;
        _nugetProcessor = null;
        _scriptDirectiveProcessor = null;
        _variablesInjected = false;
        _executionLock.Dispose();

        return ValueTask.CompletedTask;
    }

    // --- Private helpers ---

    /// <summary>
    /// Converts a character offset in text to a (line, column) pair, both 0-based.
    /// </summary>
    private static (int Line, int Column) OffsetToLineColumn(string text, int offset)
    {
        int line = 0;
        int col = 0;
        int clampedOffset = Math.Min(offset, text.Length);

        for (int i = 0; i < clampedOffset; i++)
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

    /// <summary>
    /// Counts the number of lines in a string (minimum 1).
    /// </summary>
    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        int count = 1;
        foreach (char c in text)
        {
            if (c == '\n') count++;
        }
        return count;
    }

    /// <summary>
    /// Finds the qualified identifier at a column position in a line of text.
    /// Returns the name parts and the column at the end of the identifier.
    /// </summary>
    private static (string[] Names, int ColAtEnd)? FindIdentifierAtPosition(string lineText, int column)
    {
        if (string.IsNullOrEmpty(lineText) || column < 0 || column >= lineText.Length)
            return null;

        if (!IsIdentChar(lineText[column]))
            return null;

        // Find the end of the current identifier
        int end = column;
        while (end < lineText.Length && IsIdentChar(lineText[end]))
            end++;

        // Find the start of the (potentially qualified) identifier
        int start = column;
        while (start > 0)
        {
            char c = lineText[start - 1];
            if (IsIdentChar(c) || c == '.')
                start--;
            else
                break;
        }

        if (start == end) return null;

        var text = lineText.Substring(start, end - start);
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        return (parts, end - 1);
    }

    private static int FindIdentifierStart(string lineText, int column)
    {
        int start = column;
        while (start > 0 && IsIdentChar(lineText[start - 1]))
            start--;
        return start;
    }

    private static int FindIdentifierEnd(string lineText, int column)
    {
        int end = column;
        while (end < lineText.Length && IsIdentChar(lineText[end]))
            end++;
        return end;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'';

    /// <summary>
    /// Formats a <see cref="ToolTipText"/> into a plain-text string for hover display.
    /// </summary>
    private static string FormatToolTip(ToolTipText toolTip)
    {
        var parts = new List<string>();

        foreach (var element in toolTip.Item)
        {
            if (element is ToolTipElement.Group group)
            {
                int overloadIndex = 0;
                foreach (var data in group.elements)
                {
                    overloadIndex++;
                    var mainDesc = string.Join("", data.MainDescription.Select(t => t.Text));
                    if (!string.IsNullOrWhiteSpace(mainDesc))
                    {
                        if (group.elements.Length > 1)
                            parts.Add($"({overloadIndex}/{group.elements.Length}) {mainDesc}");
                        else
                            parts.Add(mainDesc);
                    }

                    // Extract XML doc summary if available
                    var xmlDoc = FormatXmlDoc(data.XmlDoc);
                    if (!string.IsNullOrWhiteSpace(xmlDoc))
                    {
                        parts.Add(xmlDoc);
                    }
                }
            }
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Extracts a summary string from FSharpXmlDoc.
    /// </summary>
    private static string FormatXmlDoc(FSharpXmlDoc xmlDoc)
    {
        if (xmlDoc is FSharpXmlDoc.FromXmlText fromXml)
        {
            var xml = fromXml.Item.GetXmlText();
            return ExtractXmlSummary(xml);
        }
        return "";
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

    private static CellOutput FormatException(Exception ex)
    {
        // StackOverflow / OutOfMemory: simplified message suggesting kernel restart
        if (ex is StackOverflowException)
        {
            return new CellOutput(
                "text/plain",
                "Stack overflow. The computation exceeded the stack size limit. Consider restarting the kernel.",
                IsError: true,
                ErrorName: "StackOverflowException");
        }

        if (ex is OutOfMemoryException)
        {
            return new CellOutput(
                "text/plain",
                "Out of memory. The computation exceeded available memory. Consider restarting the kernel.",
                IsError: true,
                ErrorName: "OutOfMemoryException");
        }

        // MatchFailureException: include the unmatched value
        var exTypeName = ex.GetType().Name;
        if (exTypeName == "MatchFailureException")
        {
            return new CellOutput(
                "text/plain",
                $"MatchFailureException: {ex.Message}",
                IsError: true,
                ErrorName: "MatchFailureException",
                ErrorStackTrace: ex.StackTrace);
        }

        // General exception formatting with inner exception chain
        var message = $"{ex.GetType().FullName}: {ex.Message}";
        var inner = ex.InnerException;
        while (inner is not null)
        {
            message += $"{Environment.NewLine}  ---> {inner.GetType().FullName}: {inner.Message}";
            inner = inner.InnerException;
        }

        return new CellOutput(
            "text/plain",
            message,
            IsError: true,
            ErrorName: ex.GetType().Name,
            ErrorStackTrace: ex.StackTrace);
    }

    private static string FormatInstalledPackagesHtml(List<FSharpNuGetResolveResult> packages)
    {
        var items = string.Join("",
            packages.Select(p => $"<li><span>{p.PackageId}, {p.ResolvedVersion}</span></li>"));
        return $"<div><b>Installed Packages</b><ul>{items}</ul></div>";
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FSharpKernel));
    }
}
