using System.Diagnostics;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Display;
using Verso.MagicCommands;

namespace Verso.Execution;

/// <summary>
/// Encapsulates the single-cell execution workflow. Execution is routed
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
    private readonly Func<string, IMagicCommand?> _resolveMagicCommand;
    private readonly Action<Guid>? _notifyOutputUpdated;
    private readonly Func<Guid, string, bool, CancellationToken, Task<string?>>? _requestInput;
    private readonly BackgroundFaultSink? _backgroundFaults;

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
        Func<Guid, int> getExecutionCount,
        Func<string, IMagicCommand?>? resolveMagicCommand = null,
        Action<Guid>? notifyOutputUpdated = null,
        Func<Guid, string, bool, CancellationToken, Task<string?>>? requestInput = null,
        BackgroundFaultSink? backgroundFaults = null)
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
        _resolveMagicCommand = resolveMagicCommand ?? (_ => null);
        _notifyOutputUpdated = notifyOutputUpdated;
        _requestInput = requestInput;
        _backgroundFaults = backgroundFaults;
    }

    public async Task<ExecutionResult> ExecuteAsync(CellModel cell, CancellationToken ct)
    {
        var cellId = cell.Id;
        var executionCount = _getExecutionCount(cellId);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check the ICellType registry first: cell types declare whether they have a kernel.
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

        // How much this cell is still allowed to produce. A runaway loop that prints is otherwise
        // bounded by nothing, and every block it makes is saved with the notebook.
        var budget = new OutputBudget();

        // Adds one output if the cell still has room, and otherwise the one-time notice saying it
        // does not. Returns whether the cell's outputs changed. Callers hold outputLock.
        bool Admit(CellOutput output)
        {
            if (budget.TryAddBlock(cell.Outputs.Count, output.Content))
            {
                cell.Outputs.Add(output);
                streamedOutputs.Add(output);
                return true;
            }

            var notice = budget.TryDescribeLimit();
            if (notice is null)
                return false;

            cell.Outputs.Add(notice);
            return true;
        }

        Task AppendOutput(CellOutput output)
        {
            bool changed;
            lock (outputLock)
            {
                changed = Admit(output);
            }

            if (changed)
                _notifyOutputUpdated?.Invoke(cell.Id);

            return Task.CompletedTask;
        }

        // Where each named output block currently sits. Held for the length of one execution and
        // nowhere else: an identifier only has to survive long enough for the kernel that minted
        // it to write to the same block again, and keeping it off CellOutput means the record's
        // equality (which the diff view compares on) stays about content rather than identity.
        var outputBlocks = new Dictionary<string, int>(StringComparer.Ordinal);

        Task UpdateOutput(string blockId, CellOutput output)
        {
            bool changed;
            lock (outputLock)
            {
                if (outputBlocks.TryGetValue(blockId, out var index) && index < cell.Outputs.Count)
                {
                    var existing = cell.Outputs[index];
                    var admitted = budget.TryReviseBlock(existing.Content, output.Content);
                    changed = admitted is not null;

                    if (admitted is not null)
                    {
                        var revised = admitted.Length == output.Content.Length
                            ? output
                            : output with { Content = admitted };

                        // The instance being replaced is no longer the one the kernel will return,
                        // so the new one is tracked in its place to keep the end-of-cell append
                        // from adding it a second time.
                        streamedOutputs.Remove(existing);
                        cell.Outputs[index] = revised;
                        streamedOutputs.Add(revised);
                    }

                    var notice = budget.TryDescribeLimit();
                    if (notice is not null)
                    {
                        cell.Outputs.Add(notice);
                        changed = true;
                    }
                }
                else
                {
                    // A block only claims its position once it is known to fit, so a rejected first
                    // write leaves nothing for a later write to that name to revise.
                    var position = cell.Outputs.Count;
                    changed = Admit(output);
                    if (changed && !budget.LimitReached)
                        outputBlocks[blockId] = position;
                }
            }

            if (changed)
                _notifyOutputUpdated?.Invoke(cell.Id);

            return Task.CompletedTask;
        }

        // --- Magic command interception ---
        // A cell may contain multiple consecutive magic commands (e.g. two #!extension lines).
        // Loop until the first non-empty line is no longer a recognized magic command.
        var currentSource = cell.Source;
        bool reportElapsedTime = false;
        bool anyMagicProcessed = false;
        bool suppressExecution = false;

        while (true)
        {
            var parseResult = MagicCommandParser.Parse(currentSource);
            if (!parseResult.IsMagicCommand)
                break;

            var magicCommand = _resolveMagicCommand(parseResult.CommandName!);
            if (magicCommand is null)
            {
                // Check whether the command exists but is disabled (vs truly unknown).
                var isDisabled = _extensionHost.GetLoadedExtensions()
                    .OfType<IMagicCommand>()
                    .Any(m => string.Equals(m.Name, parseResult.CommandName, StringComparison.OrdinalIgnoreCase));

                var message = isDisabled
                    ? $"Magic command '#!{parseResult.CommandName}' belongs to a disabled extension. Enable the extension to use this command."
                    : $"Unknown magic command '#!{parseResult.CommandName}'. Use '#!about' to list available commands.";

                await AppendOutput(new CellOutput("text/plain", message, IsError: true)).ConfigureAwait(false);
                break;
            }

            anyMagicProcessed = true;

            var magicContext = new MagicCommandContext(
                parseResult.RemainingCode,
                _variables,
                ct,
                _theme,
                _layoutCapabilities,
                _extensionHost,
                _notebookMetadata,
                _notebook,
                AppendOutput);

            await magicCommand.ExecuteAsync(parseResult.Arguments ?? "", magicContext).ConfigureAwait(false);

            // A suppressing command (e.g. #!sql-connect, #!nuget) means the cell's
            // trailing code must not run on the kernel — but any further magic commands
            // on subsequent lines still must, so two #!sql-connect lines each register
            // their connection. Record the request and keep processing magic lines
            // instead of returning here.
            if (magicContext.SuppressExecution)
                suppressExecution = true;

            if (magicContext.ReportElapsedTime)
                reportElapsedTime = true;

            currentSource = parseResult.RemainingCode;

            if (string.IsNullOrWhiteSpace(currentSource))
            {
                stopwatch.Stop();
                return ExecutionResult.Success(cell.Id, executionCount, stopwatch.Elapsed);
            }
        }

        // A processed magic command requested suppression: this is a setup-only cell
        // (connections, package installs, ...), so do not pass trailing code to the kernel.
        if (suppressExecution)
        {
            stopwatch.Stop();
            return ExecutionResult.Success(cell.Id, executionCount, stopwatch.Elapsed);
        }

        // Determine the code to execute: if any magic commands were processed, use the remaining code
        var codeToExecute = anyMagicProcessed ? currentSource : cell.Source;

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
            display: AppendOutput,
            requestInput: (prompt, isPassword, inputCt) =>
                _requestInput is null
                    ? throw new NotSupportedException("Interactive input is not supported by this host.")
                    : _requestInput(cell.Id, prompt, isPassword, inputCt == default ? ct : inputCt),
            updateOutput: UpdateOutput);

        ct.ThrowIfCancellationRequested();

        // Set up the ambient display handler so user code can call .Display()
        var displayFormatterContext = new DisplayFormatterContext(context);
        var displayHandler = new DisplayHandler(AppendOutput, displayFormatterContext);
        using var _ = DisplayContext.SetHandler(displayHandler.DisplayAsync);

        // Marks this notebook as the one running while the cell is in flight. A background failure
        // surfacing now has no record of which notebook started the work that raised it, and this
        // is the only signal available for guessing.
        IReadOnlyList<CellOutput> returnedOutputs;
        using (_backgroundFaults?.BeginExecution())
        {
            returnedOutputs = await kernel.ExecuteAsync(codeToExecute, context).ConfigureAwait(false);
        }

        if (returnedOutputs is { Count: > 0 })
        {
            lock (outputLock)
            {
                foreach (var output in returnedOutputs)
                {
                    if (streamedOutputs.Contains(output))
                        continue;

                    Admit(output);

                    // Once the cell is full the rest cannot fit either, and the notice explaining
                    // why has already been added.
                    if (budget.LimitReached)
                        break;
                }
            }
        }

        // Work an earlier cell started and did not wait for can fail long after that cell was
        // answered. This is the first moment there is a cell to report it against. Reported
        // regardless of how much output the cell already produced: a failure the user has no other
        // way to learn about is exactly what must not be dropped to save room.
        var backgroundFaults = _backgroundFaults?.Drain() ?? Array.Empty<CellOutput>();
        if (backgroundFaults.Count > 0)
        {
            lock (outputLock)
            {
                foreach (var fault in backgroundFaults)
                    cell.Outputs.Add(fault);
            }
            _notifyOutputUpdated?.Invoke(cell.Id);
        }

        stopwatch.Stop();

        if (reportElapsedTime)
        {
            var elapsedOutput = new CellOutput("text/plain", $"Wall time: {stopwatch.Elapsed.TotalMilliseconds:F1}ms");
            lock (outputLock)
            {
                cell.Outputs.Add(elapsedOutput);
            }
        }

        // Some kernels honor cancellation by returning early without throwing
        // (e.g. PowerShell's BeginStop interrupts a sleep cleanly). Treat that
        // as a cancelled completion so the cell shows the cancelled status badge
        // rather than a misleading success checkmark.
        if (ct.IsCancellationRequested)
            return ExecutionResult.Cancelled(cell.Id, executionCount, stopwatch.Elapsed);

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
        // Surface the rendered output the same way kernel execution does, so out-of-process
        // hosts receive it incrementally as the cell finishes rather than only at the end of a
        // Run All. Without this, render-only cells (e.g. Markdown) emit no output notification
        // and their content appears only after the whole batch completes.
        _notifyOutputUpdated?.Invoke(cell.Id);

        stopwatch.Stop();

        if (ct.IsCancellationRequested)
            return ExecutionResult.Cancelled(cell.Id, executionCount, stopwatch.Elapsed);

        return ExecutionResult.Success(cell.Id, executionCount, stopwatch.Elapsed);
    }
}
