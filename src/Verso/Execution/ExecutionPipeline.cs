using System.Diagnostics;
using Verso.Abstractions;
using Verso.Contexts;

namespace Verso.Execution;

/// <summary>
/// Encapsulates the single-cell execution workflow: resolve kernel, build context,
/// execute, capture outputs, handle errors.
/// </summary>
internal sealed class ExecutionPipeline
{
    private readonly IVariableStore _variables;
    private readonly IThemeContext _theme;
    private readonly LayoutCapabilities _layoutCapabilities;
    private readonly IExtensionHostContext _extensionHost;
    private readonly INotebookMetadata _notebookMetadata;
    private readonly Func<string, ILanguageKernel?> _resolveKernel;
    private readonly Func<ILanguageKernel, Task> _ensureInitialized;
    private readonly Func<Guid, string?> _resolveLanguageId;
    private readonly Func<Guid, int> _getExecutionCount;

    public ExecutionPipeline(
        IVariableStore variables,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        Func<string, ILanguageKernel?> resolveKernel,
        Func<ILanguageKernel, Task> ensureInitialized,
        Func<Guid, string?> resolveLanguageId,
        Func<Guid, int> getExecutionCount)
    {
        _variables = variables;
        _theme = theme;
        _layoutCapabilities = layoutCapabilities;
        _extensionHost = extensionHost;
        _notebookMetadata = notebookMetadata;
        _resolveKernel = resolveKernel;
        _ensureInitialized = ensureInitialized;
        _resolveLanguageId = resolveLanguageId;
        _getExecutionCount = getExecutionCount;
    }

    public async Task<ExecutionResult> ExecuteAsync(CellModel cell, CancellationToken ct)
    {
        var cellId = cell.Id;
        var executionCount = _getExecutionCount(cellId);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var languageId = _resolveLanguageId(cellId);
            if (string.IsNullOrEmpty(languageId))
                throw new InvalidOperationException(
                    $"No language specified for cell {cellId} and no default kernel is configured.");

            var kernel = _resolveKernel(languageId)
                ?? throw new InvalidOperationException(
                    $"No kernel registered for language '{languageId}'.");

            await _ensureInitialized(kernel).ConfigureAwait(false);

            cell.Outputs.Clear();

            var outputLock = new object();
            var streamedOutputs = new HashSet<CellOutput>(ReferenceEqualityComparer.Instance);

            Task AppendOutput(CellOutput output)
            {
                lock (outputLock)
                {
                    cell.Outputs.Add(output);
                    streamedOutputs.Add(output);
                }
                return Task.CompletedTask;
            }

            var context = new Contexts.ExecutionContext(
                cellId,
                executionCount,
                _variables,
                ct,
                _theme,
                _layoutCapabilities,
                _extensionHost,
                _notebookMetadata,
                writeOutput: AppendOutput,
                display: AppendOutput);

            ct.ThrowIfCancellationRequested();

            var returnedOutputs = await kernel.ExecuteAsync(cell.Source, context).ConfigureAwait(false);

            if (returnedOutputs is { Count: > 0 })
            {
                lock (outputLock)
                {
                    foreach (var output in returnedOutputs)
                    {
                        if (!streamedOutputs.Contains(output))
                            cell.Outputs.Add(output);
                    }
                }
            }

            stopwatch.Stop();
            return ExecutionResult.Success(cellId, executionCount, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ExecutionResult.Cancelled(cellId, executionCount, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            lock (cell.Outputs)
            {
                cell.Outputs.Add(new CellOutput(
                    "text/plain",
                    ex.Message,
                    IsError: true,
                    ErrorName: ex.GetType().Name,
                    ErrorStackTrace: ex.StackTrace));
            }
            return ExecutionResult.Failed(cellId, executionCount, stopwatch.Elapsed, ex);
        }
    }
}
