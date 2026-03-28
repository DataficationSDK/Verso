using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Verso.Abstractions;
using Verso.MagicCommands;

using VersoDiagnostic = Verso.Abstractions.Diagnostic;

namespace Verso.Kernels;

/// <summary>
/// Built-in C# language kernel powered by Roslyn. Executes C# code in notebook cells
/// with chained state, code completions, diagnostics, and hover information.
/// </summary>
[VersoExtension]
public sealed class CSharpKernel : ILanguageKernel
{
    private static readonly Regex NuGetReferenceRegex = new(
        @"^#r\s+""nuget:\s*([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NuGetSourceRegex = new(
        @"^#i\s+""nuget:\s*([^""]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex UsingDirectiveRegex = new(
        @"^\s*using\s+(?!static\b)(?!var\b)(?!var\s)(?!\()([A-Za-z_][\w]*(?:\s*\.\s*[A-Za-z_][\w]*)*)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly CSharpKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private ScriptStateManager? _stateManager;
    private RoslynWorkspaceManager? _workspaceManager;
    private ScriptGlobals? _globals;
    private NuGetPackageResolver? _resolver;
    private bool _initialized;
    private bool _parametersInjected;
    private bool _disposed;

    public CSharpKernel() : this(new CSharpKernelOptions()) { }

    public CSharpKernel(CSharpKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.csharp";
    public string Name => "C# (Roslyn)";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "C# language kernel powered by Roslyn scripting.";

    // --- ILanguageKernel ---

    public string LanguageId => "csharp";
    public string DisplayName => "C# (Roslyn)";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".cs", ".csx" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

        var imports = _options.DefaultImports ?? CSharpKernelOptions.StandardImports;
        var references = BuildDefaultReferences();

        var scriptOptions = ScriptOptions.Default
            .AddImports(imports)
            .AddReferences(references);

        _stateManager = new ScriptStateManager(scriptOptions);
        _workspaceManager = new RoslynWorkspaceManager(imports, references.Select(r => (MetadataReference)r));
        _resolver = new NuGetPackageResolver();
        _executionLock = new SemaphoreSlim(1, 1);
        _globals = null;
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
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            // Process #i "nuget:" source directives and #r "nuget:" package directives
            var (cleanedCode, nugetResults) = await ProcessNuGetReferencesAsync(
                code, context, context.CancellationToken).ConfigureAwait(false);

            // Check for resolved packages from #!nuget magic command
            if (context.Variables.TryGet<List<NuGetResolveResult>>(NuGetMagicCommand.ResolvedPackagesStoreKey, out var magicResults)
                && magicResults is { Count: > 0 })
            {
                nugetResults.AddRange(magicResults);
                context.Variables.Remove(NuGetMagicCommand.ResolvedPackagesStoreKey);
            }

            // Add references if any
            var nugetAssemblyPaths = nugetResults.SelectMany(r => r.AssemblyPaths).ToList();
            if (nugetAssemblyPaths.Count > 0)
            {
                _stateManager!.AddReferences(nugetAssemblyPaths);
                _workspaceManager!.AddReferences(nugetAssemblyPaths);

                // Persist assembly paths to the variable store so other extensions
                // (e.g. #!sql-connect provider discovery) can load them at runtime
                var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (context.Variables.TryGet<List<string>>(NuGetMagicCommand.AssemblyStoreKey, out var prior) && prior is not null)
                    existingPaths.UnionWith(prior);
                existingPaths.UnionWith(nugetAssemblyPaths);
                context.Variables.Set(NuGetMagicCommand.AssemblyStoreKey, existingPaths.ToList());
            }

            // Write "Installed Packages" feedback
            if (nugetResults.Count > 0)
            {
                var html = FormatInstalledPackagesHtml(nugetResults);
                await context.WriteOutputAsync(new CellOutput("text/html", html)).ConfigureAwait(false);
            }

            var consoleWriter = new StringWriter();
            var consoleErrWriter = new StringWriter();
            Console.SetOut(consoleWriter);
            Console.SetError(consoleErrWriter);

            // Create globals on first execution so C# cells can access the shared variable store
            _globals ??= new ScriptGlobals(context.Variables);

            // Inject notebook parameters as top-level script variables (once)
            if (!_parametersInjected)
            {
                _parametersInjected = true;
                var preamble = BuildParameterPreamble(context);
                if (preamble is not null)
                {
                    await _stateManager!.RunAsync(preamble, _globals, context.CancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var scriptState = await _stateManager!.RunAsync(cleanedCode, _globals, context.CancellationToken)
                .ConfigureAwait(false);

            var outputs = new List<CellOutput>();

            // Capture console output (stdout)
            var consoleOutput = consoleWriter.ToString();
            if (!string.IsNullOrEmpty(consoleOutput))
            {
                var consoleCell = new CellOutput("text/plain", consoleOutput);
                await context.WriteOutputAsync(consoleCell).ConfigureAwait(false);
                outputs.Add(consoleCell);
            }

            // Capture console error output (stderr)
            var consoleError = consoleErrWriter.ToString();
            if (!string.IsNullOrEmpty(consoleError))
            {
                var errCell = new CellOutput("text/plain", consoleError, IsError: true, ErrorName: "Error");
                await context.WriteOutputAsync(errCell).ConfigureAwait(false);
                outputs.Add(errCell);
            }

            // Capture return value
            if (scriptState.ReturnValue is not null)
            {
                CellOutput returnOutput;

                if (scriptState.ReturnValue is CellOutput directOutput)
                {
                    returnOutput = directOutput;
                }
                else if (TryExtractCellOutput(scriptState.ReturnValue, out var extracted))
                {
                    // The script may construct a CellOutput from a different assembly load
                    // context, so the `is` check above fails. Fall back to duck-typing.
                    returnOutput = extracted!;
                }
                else
                {
                    returnOutput = await TryFormatAsync(scriptState.ReturnValue, context).ConfigureAwait(false)
                        ?? new CellOutput("text/plain", scriptState.ReturnValue.ToString() ?? "");
                }

                await context.WriteOutputAsync(returnOutput).ConfigureAwait(false);
                outputs.Add(returnOutput);
            }

            // Promote using directives to ScriptOptions so they persist across cells
            var usings = ExtractUsingDirectives(code);
            if (usings.Count > 0)
                _stateManager!.AddImports(usings);

            // Publish variables to the shared variable store
            var variables = _stateManager.GetVariables();
            foreach (var kvp in variables)
            {
                if (kvp.Value is not null)
                {
                    context.Variables.Set(kvp.Key, kvp.Value);
                }
            }

            // Append to workspace for future intellisense
            _workspaceManager!.AppendExecutedCode(code);

            return outputs;
        }
        catch (CompilationErrorException ex)
        {
            var message = string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString()));
            var errorOutput = new CellOutput(
                "text/plain",
                message,
                IsError: true,
                ErrorName: "CompilationError");
            return new[] { errorOutput };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Build a complete error message including inner exceptions so the
            // actual root cause is visible (e.g. TypeLoadException inside UseSqlite).
            var message = $"{ex.GetType().FullName}: {ex.Message}";
            var inner = ex.InnerException;
            while (inner is not null)
            {
                message += $"{Environment.NewLine}  ---> {inner.GetType().FullName}: {inner.Message}";
                inner = inner.InnerException;
            }

            var errorOutput = new CellOutput(
                "text/plain",
                message,
                IsError: true,
                ErrorName: ex.GetType().Name,
                ErrorStackTrace: ex.StackTrace);
            return new[] { errorOutput };
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            _executionLock.Release();
        }
    }

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return await _workspaceManager!.GetCompletionsAsync(code, cursorPosition).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VersoDiagnostic>> GetDiagnosticsAsync(string code)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return await _workspaceManager!.GetDiagnosticsAsync(code).ConfigureAwait(false);
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return await _workspaceManager!.GetHoverInfoAsync(code, cursorPosition).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
        _parametersInjected = false;

        if (_stateManager is not null)
            await _stateManager.DisposeAsync().ConfigureAwait(false);

        _stateManager = null;
        _workspaceManager?.Dispose();
        _workspaceManager = null;
        _resolver = null;
        _globals = null;
        _executionLock.Dispose();
    }

    private async Task<(string CleanedCode, List<NuGetResolveResult> Results)> ProcessNuGetReferencesAsync(
        string code, IExecutionContext context, CancellationToken ct)
    {
        // Process #i "nuget: ..." source directives first
        var sourceMatches = NuGetSourceRegex.Matches(code);
        if (sourceMatches.Count > 0)
        {
            // Get or create the session-scoped source registry
            if (!context.Variables.TryGet<NuGetSourceRegistry>(NuGetSourceRegistry.StoreKey, out var registry)
                || registry is null)
            {
                registry = new NuGetSourceRegistry();
                context.Variables.Set(NuGetSourceRegistry.StoreKey, registry);
            }

            foreach (Match match in sourceMatches)
            {
                var source = NuGetPackageResolver.ParseSourceDirective(match.Groups[1].Value);
                if (source is not null)
                {
                    _resolver!.AddSource(source);
                    registry.AddSource(source);
                }
            }

            // Strip #i lines from the code
            code = NuGetSourceRegex.Replace(code, "").Trim();
        }

        // Process #r "nuget: ..." package directives
        var matches = NuGetReferenceRegex.Matches(code);
        if (matches.Count == 0)
            return (code, new List<NuGetResolveResult>());

        var results = new List<NuGetResolveResult>();

        foreach (Match match in matches)
        {
            var directive = match.Groups[1].Value;
            var parsed = NuGetPackageResolver.ParseNuGetReference(directive);
            if (parsed is null) continue;

            var result = await _resolver!.ResolvePackageAsync(parsed.Value.PackageId, parsed.Value.Version, ct)
                .ConfigureAwait(false);
            results.Add(result);
        }

        // Remove the #r "nuget:" lines from the code
        var cleanedCode = NuGetReferenceRegex.Replace(code, "").Trim();
        return (cleanedCode, results);
    }

    /// <summary>
    /// Builds a C# code snippet that declares notebook parameters as typed script variables,
    /// reading their values from the IVariableStore. Returns null if no parameters exist.
    /// </summary>
    private static string? BuildParameterPreamble(IExecutionContext context)
    {
        var parameters = context.NotebookMetadata?.Parameters;
        if (parameters is null || parameters.Count == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var (name, def) in parameters)
        {
            // Skip parameters that aren't valid C# identifiers
            if (!IsValidCSharpIdentifier(name))
                continue;

            // Skip required parameters that have no meaningful value in the variable store.
            // Without a real value, we should not inject a synthetic default (e.g. "" or 0)
            // as that masks missing-value validation.
            if (def.Required)
            {
                var hasValue = context.Variables.TryGet<object>(name, out var val)
                    && val is not null
                    && !IsEmptyValue(val, def.Type);
                if (!hasValue)
                    continue;
            }

            var (clrType, defaultLiteral) = def.Type switch
            {
                "int" => ("long", "0L"),
                "float" => ("double", "0.0"),
                "bool" => ("bool", "false"),
                "date" => ("DateOnly", "default"),
                "datetime" => ("DateTimeOffset", "default"),
                _ => ("string", "\"\"")
            };

            // Declare as typed variable, reading from the variable store
            sb.AppendLine($"var {name} = Variables.TryGet<{clrType}>(\"{name}\", out var __{name}__val) ? __{name}__val : {defaultLiteral};");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Returns true if the value is a CLR default or empty for the given parameter type.
    /// </summary>
    private static bool IsEmptyValue(object value, string typeId) => typeId switch
    {
        "string" => value is string s && string.IsNullOrWhiteSpace(s),
        "date" => value is DateOnly d && d == default,
        "datetime" => value is DateTimeOffset dto && dto == default,
        _ => false
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "var", "virtual", "void", "volatile", "while"
    };

    private static bool IsValidCSharpIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (CSharpKeywords.Contains(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        }
        return true;
    }

    private static List<string> ExtractUsingDirectives(string code)
    {
        var namespaces = new List<string>();
        foreach (Match match in UsingDirectiveRegex.Matches(code))
        {
            var ns = match.Groups[1].Value.Replace(" ", "");
            namespaces.Add(ns);
        }
        return namespaces;
    }

    private static string FormatInstalledPackagesHtml(List<NuGetResolveResult> packages)
    {
        var items = string.Join("",
            packages.Select(p => $"<li><span>{p.PackageId}, {p.ResolvedVersion}</span></li>"));
        return $"<div><b>Installed Packages</b><ul>{items}</ul></div>";
    }

    private static async Task<CellOutput?> TryFormatAsync(object value, IExecutionContext context)
    {
        var formatters = context.ExtensionHost.GetFormatters();
        if (formatters.Count == 0) return null;

        var fmtContext = new ExecutionFormatterContext(context);

        foreach (var formatter in formatters.OrderByDescending(f => f.Priority))
        {
            if (formatter.SupportedTypes.Any(t => t.IsInstanceOfType(value))
                && formatter.CanFormat(value, fmtContext))
            {
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
            throw new ObjectDisposedException(nameof(CSharpKernel));
    }

    private List<PortableExecutableReference> BuildDefaultReferences()
    {
        var references = new List<PortableExecutableReference>();

        // Core runtime assemblies
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedPlatformAssemblies is not null)
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var path in trustedPlatformAssemblies.Split(separator))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("netstandard", StringComparison.OrdinalIgnoreCase))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        // Additional references from options
        if (_options.DefaultReferences is not null)
        {
            foreach (var refPath in _options.DefaultReferences)
            {
                if (File.Exists(refPath))
                {
                    references.Add(MetadataReference.CreateFromFile(refPath));
                }
            }
        }

        return references;
    }
}
