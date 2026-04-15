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
    private bool _parametersInjected;
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
    public string? Author => "Datafication";
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
        _variableBridge?.UpdateOptions(_options);
        return Task.CompletedTask;
    }

    public Task OnSettingChangedAsync(string name, object? value)
    {
        _options = ApplyValues(_options, new Dictionary<string, object?> { [name] = value });
        _variableBridge?.UpdateOptions(_options);
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
        _parametersInjected = false;
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

            // Inject parameter bindings as top-level let declarations on first execution
            if (!_parametersInjected)
            {
                _parametersInjected = true;
                var preamble = BuildParameterPreamble(context);
                if (preamble is not null)
                {
                    _sessionManager!.EvalSilent(preamble);
                }
            }

            // Inject shared variables from the store so other kernels' outputs
            // are accessible by name in F# cells.
            _variableBridge!.InjectFromStore(_sessionManager!, context.Variables);

            var outputs = new List<CellOutput>();
            var processedCode = code;

            // --- NuGet: check magic command results ---
            var magicPaths = _nugetProcessor!.CheckMagicCommandResults(context.Variables);
            foreach (var path in magicPaths)
            {
                _sessionManager!.EvalSilent($"#r \"{path}\"");
                _projectContext?.AddReference(path);
                var dir = Path.GetDirectoryName(path);
                if (dir is not null)
                    _sessionManager.AddNuGetAssemblyDirectory(dir);
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

            // Parse FSI output to detect trailing expression.
            // FSI creates a "val it:" binding when the cell ends with an expression.
            // Binding-only cells (let declarations without a trailing expression) produce
            // no output, matching Polyglot Notebooks behavior.
            // Unit-returning expressions (printfn, Console.Write, etc.) are also suppressed.
            var (_, hasTrailingExpression, itIsUnit) = ParseFsiOutput(result.FsiOutput);

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

            // 6. Result value (trailing expression, skip unit)
            if (result.ResultValue is not null and not Unit && !itIsUnit)
            {
                // Attempt to resolve async values
                var resolved = await FSharpValueFormatter.ResolveAsyncValue(
                    result.ResultValue, context.CancellationToken).ConfigureAwait(false);

                if (resolved is not null)
                {
                    CellOutput? valueCell = null;

                    if (resolved is CellOutput directOutput)
                    {
                        valueCell = directOutput;
                    }
                    else if (TryExtractCellOutput(resolved, out var extracted))
                    {
                        valueCell = extracted;
                    }
                    else
                    {
                        valueCell = await TryFormatAsync(resolved, context).ConfigureAwait(false);
                    }

                    if (valueCell is null)
                    {
                        var formatted = FSharpValueFormatter.FormatValue(resolved);
                        if (!string.IsNullOrEmpty(formatted))
                        {
                            valueCell = new CellOutput("text/plain", formatted);
                        }
                    }

                    if (valueCell is not null)
                    {
                        await context.WriteOutputAsync(valueCell).ConfigureAwait(false);
                        outputs.Add(valueCell);
                    }
                }
            }
            else if (hasTrailingExpression && result.ResultValue is null && !itIsUnit)
            {
                // Result is null at CLR level (e.g. F# None) but FSI had a trailing expression.
                // Extract the "it" value representation from FSI output.
                var itText = ExtractFsiBindingValue(result.FsiOutput, "it");
                if (itText is not null)
                {
                    var cell = new CellOutput("text/plain", itText);
                    await context.WriteOutputAsync(cell).ConfigureAwait(false);
                    outputs.Add(cell);
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
        _parametersInjected = false;
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

    private async Task<CellOutput?> TryFormatAsync(object value, IExecutionContext context)
    {
        var formatters = context.ExtensionHost.GetFormatters();
        if (formatters.Count == 0) return null;

        var fmtContext = new ExecutionFormatterContext(context);

        foreach (var formatter in formatters.OrderByDescending(f => f.Priority))
        {
            if (formatter.SupportedTypes.Any(t => t.IsInstanceOfType(value))
                && formatter.CanFormat(value, fmtContext))
            {
                // Apply kernel settings to the F# data formatter
                if (formatter is Formatters.FSharpDataFormatter fsharpFormatter)
                {
                    fsharpFormatter.MaxCollectionLimit = _options.MaxCollectionDisplay;
                }

                return await formatter.FormatAsync(value, fmtContext).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a <see cref="CellOutput"/> from an object that has the same shape
    /// but was loaded in a different assembly context (e.g. via #r in a notebook cell).
    /// </summary>
    private static bool TryExtractCellOutput(object value, out CellOutput? output)
    {
        output = null;
        var type = value.GetType();
        if (type.FullName != "Verso.Abstractions.CellOutput") return false;

        var mimeType = type.GetProperty("MimeType")?.GetValue(value) as string;
        var content = type.GetProperty("Content")?.GetValue(value) as string;
        if (mimeType is null || content is null) return false;

        var isError = type.GetProperty("IsError")?.GetValue(value) is true;
        var errorName = type.GetProperty("ErrorName")?.GetValue(value) as string;
        var errorStackTrace = type.GetProperty("ErrorStackTrace")?.GetValue(value) as string;

        output = new CellOutput(mimeType, content, isError, errorName, errorStackTrace);
        return true;
    }

    /// <summary>
    /// Adapts an <see cref="IExecutionContext"/> to <see cref="IFormatterContext"/> by adding
    /// the three formatting-specific properties.
    /// </summary>
    private sealed class ExecutionFormatterContext : IFormatterContext
    {
        private readonly IExecutionContext _inner;
        public ExecutionFormatterContext(IExecutionContext inner) => _inner = inner;

        public string MimeType => "text/html";
        public double MaxWidth => 800;
        public double MaxHeight => 600;

        public IVariableStore Variables => _inner.Variables;
        public CancellationToken CancellationToken => _inner.CancellationToken;
        public Task WriteOutputAsync(CellOutput output) => _inner.WriteOutputAsync(output);
        public IThemeContext Theme => _inner.Theme;
        public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
        public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
        public INotebookOperations Notebook => _inner.Notebook;
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

    /// <summary>
    /// Parses FSI output text for <c>val &lt;name&gt;:</c> binding declarations.
    /// Returns the list of modified binding names (excluding <c>it</c>), whether
    /// a trailing expression was detected (indicated by a <c>val it:</c> line),
    /// and whether the <c>it</c> binding is of type <c>unit</c>.
    /// </summary>
    private static (List<string> ModifiedBindings, bool HasTrailingExpression, bool ItIsUnit) ParseFsiOutput(string? fsiOutput)
    {
        var names = new List<string>();
        bool hasIt = false;
        bool itIsUnit = false;

        if (string.IsNullOrEmpty(fsiOutput))
            return (names, false, false);

        foreach (var line in fsiOutput.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("val ")) continue;

            var name = ExtractFsiBindingName(trimmed);
            if (name is null) continue;

            if (name == "it")
            {
                hasIt = true;
                // Check if type is unit: "val it: unit = ()"
                itIsUnit = trimmed.Contains(": unit");
            }
            else
            {
                names.Add(name);
            }
        }

        return (names, hasIt, itIsUnit);
    }

    /// <summary>
    /// Extracts the binding name from a <c>val &lt;name&gt;: ...</c> FSI output line.
    /// </summary>
    private static string? ExtractFsiBindingName(string valLine)
    {
        // Format: "val <name>: <type> = <value>" or "val <name> : <type> = <value>"
        var afterVal = valLine.AsSpan(4);
        var colonIdx = afterVal.IndexOf(':');
        var spaceIdx = afterVal.IndexOf(' ');
        var endIdx = colonIdx >= 0 && (spaceIdx < 0 || colonIdx < spaceIdx)
            ? colonIdx
            : (spaceIdx >= 0 ? spaceIdx : afterVal.Length);

        if (endIdx <= 0) return null;

        var name = afterVal.Slice(0, endIdx).Trim().ToString();
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Extracts the value representation from a <c>val &lt;name&gt;: &lt;type&gt; = &lt;value&gt;</c>
    /// line in FSI output. Used when the CLR value is null (e.g. F# <c>None</c>).
    /// </summary>
    private static string? ExtractFsiBindingValue(string? fsiOutput, string bindingName)
    {
        if (string.IsNullOrEmpty(fsiOutput)) return null;

        foreach (var line in fsiOutput.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("val ")) continue;

            var name = ExtractFsiBindingName(trimmed);
            if (name != bindingName) continue;

            // Find " = " and extract everything after it
            var eqIdx = trimmed.IndexOf(" = ", StringComparison.Ordinal);
            if (eqIdx < 0) return null;

            var value = trimmed.Substring(eqIdx + 3).Trim();
            return value.Length > 0 ? value : null;
        }

        return null;
    }

    /// <summary>
    /// Builds F# let bindings for notebook parameters so they are available as
    /// top-level identifiers (e.g. <c>region</c> instead of <c>Variables.Get&lt;string&gt;("region")</c>).
    /// </summary>
    private static string? BuildParameterPreamble(IExecutionContext context)
    {
        var parameters = context.NotebookMetadata?.Parameters;
        if (parameters is null || parameters.Count == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var (name, def) in parameters)
        {
            if (!IsValidFSharpIdentifier(name))
                continue;

            // Skip required parameters that have no meaningful value
            if (def.Required)
            {
                var hasValue = context.Variables.TryGet<object>(name, out var val)
                    && val is not null
                    && !IsEmptyValue(val, def.Type);
                if (!hasValue)
                    continue;
            }

            var (fsharpType, defaultLiteral) = def.Type switch
            {
                "int" => ("int64", "0L"),
                "float" => ("float", "0.0"),
                "bool" => ("bool", "false"),
                "date" => ("System.DateOnly", "System.DateOnly()"),
                "datetime" => ("System.DateTimeOffset", "System.DateTimeOffset()"),
                _ => ("string", "\"\"")
            };

            sb.AppendLine($"let {name} : {fsharpType} = match tryGetVar<{fsharpType}> \"{name}\" with | Some v -> v | None -> {defaultLiteral}");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static bool IsEmptyValue(object value, string typeId) => typeId switch
    {
        "string" => value is string s && string.IsNullOrWhiteSpace(s),
        "date" => value is DateOnly d && d == default,
        "datetime" => value is DateTimeOffset dto && dto == default,
        _ => false
    };

    /// <summary>
    /// F# keywords that cannot be used as unquoted identifiers.
    /// </summary>
    private static readonly HashSet<string> FSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "assert", "base", "begin", "class", "default",
        "delegate", "do", "done", "downcast", "downto", "elif", "else", "end",
        "exception", "extern", "false", "finally", "fixed", "for", "fun",
        "function", "global", "if", "in", "inherit", "inline", "interface",
        "internal", "lazy", "let", "match", "member", "module", "mutable",
        "namespace", "new", "not", "null", "of", "open", "or", "override",
        "private", "public", "rec", "return", "select", "static", "struct",
        "then", "to", "true", "try", "type", "upcast", "use", "val", "void",
        "when", "while", "with", "yield"
    };

    private static bool IsValidFSharpIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (FSharpKeywords.Contains(name))
            return false;
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_' && name[i] != '\'')
                return false;
        }
        return true;
    }
}
