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

    private readonly CSharpKernelOptions _options;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private ScriptStateManager? _stateManager;
    private RoslynWorkspaceManager? _workspaceManager;
    private ScriptGlobals? _globals;
    private bool _initialized;
    private bool _disposed;

    public CSharpKernel() : this(new CSharpKernelOptions()) { }

    public CSharpKernel(CSharpKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.csharp";
    public string Name => "C# (Roslyn)";
    public string Version => "0.5.0";
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

        var imports = _options.DefaultImports ?? CSharpKernelOptions.StandardImports;
        var references = BuildDefaultReferences();

        var scriptOptions = ScriptOptions.Default
            .AddImports(imports)
            .AddReferences(references);

        _stateManager = new ScriptStateManager(scriptOptions);
        _workspaceManager = new RoslynWorkspaceManager(imports, references.Select(r => (MetadataReference)r));
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
        try
        {
            // Process #r "nuget:" directives
            var (cleanedCode, nugetResults) = await ProcessNuGetReferencesAsync(
                code, context.CancellationToken).ConfigureAwait(false);

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
            Console.SetOut(consoleWriter);

            // Create globals on first execution so C# cells can access the shared variable store
            _globals ??= new ScriptGlobals(context.Variables);

            var scriptState = await _stateManager!.RunAsync(cleanedCode, _globals, context.CancellationToken)
                .ConfigureAwait(false);

            var outputs = new List<CellOutput>();

            // Capture console output
            var consoleOutput = consoleWriter.ToString();
            if (!string.IsNullOrEmpty(consoleOutput))
            {
                var consoleCell = new CellOutput("text/plain", consoleOutput);
                await context.WriteOutputAsync(consoleCell).ConfigureAwait(false);
                outputs.Add(consoleCell);
            }

            // Capture return value
            if (scriptState.ReturnValue is not null)
            {
                var returnOutput = new CellOutput("text/plain", scriptState.ReturnValue.ToString() ?? "");
                await context.WriteOutputAsync(returnOutput).ConfigureAwait(false);
                outputs.Add(returnOutput);
            }

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

        if (_stateManager is not null)
            await _stateManager.DisposeAsync().ConfigureAwait(false);

        _workspaceManager?.Dispose();
        _executionLock.Dispose();
    }

    private async Task<(string CleanedCode, List<NuGetResolveResult> Results)> ProcessNuGetReferencesAsync(
        string code, CancellationToken ct)
    {
        var matches = NuGetReferenceRegex.Matches(code);
        if (matches.Count == 0)
            return (code, new List<NuGetResolveResult>());

        var results = new List<NuGetResolveResult>();
        var resolver = new NuGetPackageResolver();

        foreach (Match match in matches)
        {
            var directive = match.Groups[1].Value;
            var parsed = NuGetPackageResolver.ParseNuGetReference(directive);
            if (parsed is null) continue;

            var result = await resolver.ResolvePackageAsync(parsed.Value.PackageId, parsed.Value.Version, ct)
                .ConfigureAwait(false);
            results.Add(result);
        }

        // Remove the #r "nuget:" lines from the code
        var cleanedCode = NuGetReferenceRegex.Replace(code, "").Trim();
        return (cleanedCode, results);
    }

    private static string FormatInstalledPackagesHtml(List<NuGetResolveResult> packages)
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
