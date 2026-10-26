using FSharp.Compiler.CodeAnalysis;
using FSharp.Compiler.Text;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;

namespace Verso.FSharp.Kernel;

/// <summary>
/// Manages an <see cref="FSharpChecker"/> instance for IntelliSense operations.
/// Provides parse-and-check functionality and background pre-warming.
/// </summary>
internal sealed class FSharpCheckerManager : IDisposable
{
    private FSharpChecker? _checker;

    // The last check asked for, by everything it depends on. The editor asks for diagnostics and
    // for semantic tokens of the same text moments apart, and a check of a cell re-checks every
    // cell executed before it, so the second request shares the first one's check rather than
    // running its own. A request for different text replaces it.
    private readonly object _lastCheckLock = new();
    private string? _lastCheckKey;
    private Task<(FSharpParseFileResults ParseResults, FSharpCheckFileResults CheckResults)?>? _lastCheck;

    /// <summary>
    /// Creates and configures the F# checker instance.
    /// </summary>
    public void Initialize()
    {
        // FSharpChecker.Create has all optional parameters (FSharpOption<T>).
        // Pass None for all to use defaults, then override the ones we care about.
        _checker = FSharpChecker.Create(
            projectCacheSize: FSharpOption<int>.Some(3),
            keepAssemblyContents: FSharpOption<bool>.Some(true),
            keepAllBackgroundResolutions: FSharpOption<bool>.Some(true),
            legacyReferenceResolver: null,
            tryGetMetadataSnapshot: null,
            suggestNamesForErrors: FSharpOption<bool>.Some(true),
            keepAllBackgroundSymbolUses: null,
            enableBackgroundItemKeyStoreAndSemanticClassification: null,
            enablePartialTypeChecking: null,
            parallelReferenceResolution: null,
            captureIdentifiersWhenParsing: null,
            documentSource: null,
            useTransparentCompiler: null,
            transparentCompilerCacheSizes: null);
    }

    /// <summary>
    /// Parses and type-checks a source file, returning parse and check results.
    /// </summary>
    /// <returns>
    /// A tuple of (FSharpParseFileResults, FSharpCheckFileResults) or <c>null</c>
    /// if the check was aborted.
    /// </returns>
    /// <remarks>
    /// A call with the same file, text, compiler options and loaded scripts, unchanged on disk, as
    /// the previous one returns that call's results, or joins it while it is still running. An aborted or failed check is not
    /// kept.
    /// </remarks>
    public Task<(FSharpParseFileResults ParseResults, FSharpCheckFileResults CheckResults)?> ParseAndCheckAsync(
        string fileName, string sourceText, FSharpProjectOptions options, CancellationToken ct = default)
    {
        var checker = _checker
            ?? throw new InvalidOperationException("Checker has not been initialized.");

        // A cancellable request cannot be shared: cancelling it would cancel every caller joined to it.
        if (ct.CanBeCanceled)
            return CheckAsync(checker, fileName, sourceText, options, ct);

        var key = CheckKey(fileName, sourceText, options);
        lock (_lastCheckLock)
        {
            if (_lastCheck is not null && _lastCheckKey == key && !_lastCheck.IsFaulted && !_lastCheck.IsCanceled
                && !(_lastCheck.IsCompletedSuccessfully && _lastCheck.Result is null))
            {
                return _lastCheck;
            }

            _lastCheckKey = key;
            _lastCheck = CheckAsync(checker, fileName, sourceText, options, ct);
            return _lastCheck;
        }
    }

    // The other source files are #load'ed scripts, which the checker reads from disk, so a key
    // holds each one's write time as well as its path.
    private static string CheckKey(string fileName, string sourceText, FSharpProjectOptions options)
    {
        var key = new System.Text.StringBuilder();
        foreach (var option in options.OtherOptions)
            key.Append(option).Append('\0');
        foreach (var file in options.SourceFiles)
        {
            if (string.Equals(file, fileName, StringComparison.Ordinal)) continue;
            key.Append(file).Append('\u0001').Append(File.GetLastWriteTimeUtc(file).Ticks).Append('\0');
        }
        return key.Append(fileName).Append('\0').Append(sourceText).ToString();
    }

    private static async Task<(FSharpParseFileResults ParseResults, FSharpCheckFileResults CheckResults)?> CheckAsync(
        FSharpChecker checker, string fileName, string sourceText, FSharpProjectOptions options, CancellationToken ct)
    {
        var source = SourceText.ofString(sourceText);
        var asyncOp = checker.ParseAndCheckFileInProject(
            fileName, 0, source, options,
            userOpName: FSharpOption<string>.None);

        var result = await StartAsTask(asyncOp, ct).ConfigureAwait(false);
        var parseResults = result.Item1;
        var checkAnswer = result.Item2;

        if (checkAnswer is FSharpCheckFileAnswer.Succeeded succeeded)
        {
            return (parseResults, succeeded.Item);
        }

        return null;
    }

    /// <summary>
    /// Lists the files FSI compiles for the given <c>#load</c>ed scripts, in the order it
    /// compiles them: each script after the scripts it loads itself.
    /// </summary>
    /// <remarks>
    /// The closure is the one the checker works out for a script holding just those
    /// <c>#load</c> lines, so a nested <c>#load</c> resolves against the file it is in, as it
    /// does in FSI. The script itself is the last file of the closure and is left out.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetLoadClosureAsync(
        string scriptFileName, IEnumerable<string> loadedPaths, CancellationToken ct = default)
    {
        if (_checker is null)
            throw new InvalidOperationException("Checker has not been initialized.");

        var script = string.Join("\n", loadedPaths.Select(p => $"#load @\"{p.Replace("\"", "\"\"")}\""));
        var asyncOp = _checker.GetProjectOptionsFromScript(
            scriptFileName, SourceText.ofString(script),
            caret: null, previewEnabled: null, loadedTimeStamp: null, otherFlags: null,
            useFsiAuxLib: null, useSdkRefs: null, assumeDotNetFramework: null, sdkDirOverride: null,
            optionsStamp: null, userOpName: null);

        var (options, _) = await StartAsTask(asyncOp, ct).ConfigureAwait(false);
        return options.SourceFiles.Take(options.SourceFiles.Length - 1).ToArray();
    }

    /// <summary>
    /// Triggers a background parse-and-check to pre-warm the checker cache.
    /// Errors are silently swallowed.
    /// </summary>
    public void TriggerBackgroundCheck(string fileName, string sourceText, FSharpProjectOptions options)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ParseAndCheckAsync(fileName, sourceText, options).ConfigureAwait(false);
            }
            catch
            {
                // Background pre-warming errors are intentionally swallowed
            }
        });
    }

    public void Dispose()
    {
        _checker = null;
        lock (_lastCheckLock)
        {
            _lastCheckKey = null;
            _lastCheck = null;
        }
    }

    /// <summary>
    /// Converts an F# Async computation to a .NET Task.
    /// </summary>
    internal static Task<T> StartAsTask<T>(FSharpAsync<T> asyncComputation, CancellationToken ct = default)
    {
        return FSharpAsync.StartAsTask(
            asyncComputation,
            FSharpOption<TaskCreationOptions>.None,
            ct == default ? FSharpOption<CancellationToken>.None : FSharpOption<CancellationToken>.Some(ct));
    }
}
