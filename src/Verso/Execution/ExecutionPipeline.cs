using System.Diagnostics;
using Verso.Abstractions;
using Verso.Contexts;

namespace Verso.Execution;

/// <summary>
/// Encapsulates the single-cell execution workflow. Per specification §5.3, execution is routed
/// based on the cell type: if a matching <see cref="ICellType"/> has a non-null <see cref="ICellType.Kernel"/>,
/// the cell is executed via that kernel; otherwise it is rendered via an <see cref="ICellRenderer"/>.
/// Cells with no matching cell type fall back to kernel resolution by language, and then to renderer
/// resolution by cell type string.
/// </summary>
internal sealed class ExecutionPipeline
{
    private readonly IVariableStore _variables;
    private readonly IThemeContext _theme;
    private readonly LayoutCapabilities _layoutCapabilities;
    private readonly IExtensionHostContext _extensionHost;
    private readonly INotebookMetadata _notebookMetadata;
    private readonly INotebookOperations _notebook;
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
        INotebookOperations notebook,
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
        _notebook = notebook;
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
            // §5.3: Check ICellType registry first — cell types declare whether they have a kernel.
            var cellType = _extensionHost.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));

            if (cellType is not null)
            {
                if (cellType.Kernel is not null)
                {
                    // Cell type has a kernel — execute via that kernel.
                    return await ExecuteWithKernelAsync(cell, cellType.Kernel, ct, stopwatch, executionCount)
                        .ConfigureAwait(false);
                }

                // Cell type has no kernel — render only via its renderer.
                return await RenderCellAsync(cell, cellType.Renderer, ct, stopwatch, executionCount)
                    .ConfigureAwait(false);
            }

            // No ICellType match — use the cell's own Language and Type to decide.
            // Try kernel only if the cell has an explicit language set.
            if (!string.IsNullOrEmpty(cell.Language))
            {
                var kernel = _resolveKernel(cell.Language);
                if (kernel is not null)
                {
                    return await ExecuteWithKernelAsync(cell, kernel, ct, stopwatch, executionCount)
                        .ConfigureAwait(false);
                }
            }

            // Try to find a renderer matching the cell type string.
            var renderer = _extensionHost.GetRenderers()
                .FirstOrDefault(r => string.Equals(r.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));

            if (renderer is not null)
            {
                return await RenderCellAsync(cell, renderer, ct, stopwatch, executionCount)
                    .ConfigureAwait(false);
            }

            // Last resort: try the notebook's default kernel.
            var defaultLanguageId = _resolveLanguageId(cellId);
            var defaultKernel = !string.IsNullOrEmpty(defaultLanguageId) ? _resolveKernel(defaultLanguageId) : null;

            if (defaultKernel is not null)
            {
                return await ExecuteWithKernelAsync(cell, defaultKernel, ct, stopwatch, executionCount)
                    .ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"No kernel or renderer found for cell {cellId} (type='{cell.Type}', language='{cell.Language}').");
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

    private async Task<ExecutionResult> ExecuteWithKernelAsync(
        CellModel cell, ILanguageKernel kernel, CancellationToken ct,
        Stopwatch stopwatch, int executionCount)
    {
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
            cell.Id,
            executionCount,
            _variables,
            ct,
            _theme,
            _layoutCapabilities,
            _extensionHost,
            _notebookMetadata,
            _notebook,
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
        return ExecutionResult.Success(cell.Id, executionCount, stopwatch.Elapsed);
    }

    private async Task<ExecutionResult> RenderCellAsync(
        CellModel cell, ICellRenderer renderer, CancellationToken ct,
        Stopwatch stopwatch, int executionCount)
    {
        ct.ThrowIfCancellationRequested();

        cell.Outputs.Clear();

        var renderContext = new CellRenderContext(
            cell.Id,
            cell.Metadata,
            _variables,
            ct,
            _theme,
            _layoutCapabilities,
            _extensionHost,
            _notebookMetadata,
            _notebook);

        var result = await renderer.RenderInputAsync(cell.Source, renderContext).ConfigureAwait(false);
        cell.Outputs.Add(new CellOutput(result.MimeType, result.Content));

        stopwatch.Stop();
        return ExecutionResult.Success(cell.Id, executionCount, stopwatch.Elapsed);
    }
}
