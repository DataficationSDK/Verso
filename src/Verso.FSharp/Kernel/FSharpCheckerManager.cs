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
            useSyntaxTreeCache: null,
            useTransparentCompiler: null);
    }

    /// <summary>
    /// Parses and type-checks a source file, returning parse and check results.
    /// </summary>
    /// <returns>
    /// A tuple of (FSharpParseFileResults, FSharpCheckFileResults) or <c>null</c>
    /// if the check was aborted.
    /// </returns>
    public async Task<(FSharpParseFileResults ParseResults, FSharpCheckFileResults CheckResults)?> ParseAndCheckAsync(
        string fileName, string sourceText, FSharpProjectOptions options, CancellationToken ct = default)
    {
        if (_checker is null)
            throw new InvalidOperationException("Checker has not been initialized.");

        var source = SourceText.ofString(sourceText);
        var asyncOp = _checker.ParseAndCheckFileInProject(
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
