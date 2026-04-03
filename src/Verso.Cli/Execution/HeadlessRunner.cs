using Verso.Abstractions;
using Verso.Cli.Parameters;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Execution;

/// <summary>
/// Configuration for a headless notebook execution run.
/// </summary>
public sealed class RunOptions
{
    public string FilePath { get; init; } = "";
    public string? KernelOverride { get; init; }
    public string[]? CellSelectors { get; init; }
    public string? ExtensionsDirectory { get; init; }
    public bool FailFast { get; init; }
    public bool Save { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
    public bool Verbose { get; init; }
    public Dictionary<string, string>? Parameters { get; init; }
    public bool Interactive { get; init; }
    public bool TrustLocalAssemblies { get; init; }
}

/// <summary>
/// Result of a headless notebook execution run.
/// </summary>
public sealed class RunResult
{
    public int ExitCode { get; init; }
    public IReadOnlyList<ExecutionResult> CellResults { get; init; } = Array.Empty<ExecutionResult>();
    public IReadOnlyList<CellModel> Cells { get; init; } = Array.Empty<CellModel>();
    public TimeSpan TotalElapsed { get; init; }
    public IReadOnlyList<VariableDescriptor>? Variables { get; init; }
    public Dictionary<string, object>? ResolvedParameters { get; init; }
    public string NotebookPath { get; init; } = "";
}

/// <summary>
/// Bootstraps the Verso engine and executes a notebook headlessly.
/// Follows the bootstrap sequence from Spec Section 6.1.
/// </summary>
public sealed class HeadlessRunner
{
    /// <summary>
    /// Executes a notebook with the given options.
    /// </summary>
    public async Task<RunResult> ExecuteAsync(RunOptions options, CancellationToken externalCt = default)
    {
        var filePath = Path.GetFullPath(options.FilePath);

        if (!File.Exists(filePath))
        {
            return new RunResult
            {
                ExitCode = ExitCodes.FileNotFound,
                CellResults = Array.Empty<ExecutionResult>(),
                Cells = Array.Empty<CellModel>(),
                TotalElapsed = TimeSpan.Zero,
                NotebookPath = filePath
            };
        }

        var extensionHost = new ExtensionHost();
        extensionHost.ConsentHandler = (extensions, _) =>
        {
            if (!options.TrustLocalAssemblies)
            {
                foreach (var ext in extensions)
                {
                    if (ext.Source == "session-generated local assembly")
                    {
                        Console.Error.WriteLine(
                            $"Warning: Refusing session-generated extension '{ext.PackageId}'. " +
                            "Use --trust-local-assemblies to allow.");
                        return Task.FromResult(false);
                    }
                }
            }
            return Task.FromResult(true);
        };

        try
        {
            await extensionHost.LoadBuiltInExtensionsAsync();

            if (options.ExtensionsDirectory is not null)
            {
                Console.Error.WriteLine($"Warning: Loading third-party extensions from '{options.ExtensionsDirectory}'. These extensions are auto-approved for headless execution.");
                await extensionHost.LoadFromDirectoryAsync(options.ExtensionsDirectory);
            }

            INotebookSerializer serializer;
            try
            {
                serializer = SerializerResolver.Resolve(extensionHost, filePath);
            }
            catch (SerializerNotFoundException)
            {
                return new RunResult
                {
                    ExitCode = ExitCodes.SerializationError,
                    CellResults = Array.Empty<ExecutionResult>(),
                    Cells = Array.Empty<CellModel>(),
                    TotalElapsed = TimeSpan.Zero,
                    NotebookPath = filePath
                };
            }

            var content = await File.ReadAllTextAsync(filePath, externalCt);
            NotebookModel notebook;
            try
            {
                notebook = await serializer.DeserializeAsync(content);
            }
            catch (Exception)
            {
                return new RunResult
                {
                    ExitCode = ExitCodes.SerializationError,
                    CellResults = Array.Empty<ExecutionResult>(),
                    Cells = Array.Empty<CellModel>(),
                    TotalElapsed = TimeSpan.Zero,
                    NotebookPath = filePath
                };
            }

            await using var scaffold = new Scaffold(notebook, extensionHost, filePath);
            scaffold.InitializeSubsystems();

            // Resolve and inject notebook parameters
            var isVersoFormat = filePath.EndsWith(".verso", StringComparison.OrdinalIgnoreCase);
            var resolver = new ParameterResolver(
                notebook.Parameters,
                options.Parameters ?? new Dictionary<string, string>(),
                isVersoFormat,
                options.Interactive);

            var paramResult = resolver.Resolve();
            if (!paramResult.IsSuccess)
            {
                Console.Error.WriteLine(paramResult.ErrorMessage);
                return new RunResult
                {
                    ExitCode = ExitCodes.MissingParameters,
                    NotebookPath = filePath
                };
            }

            foreach (var (name, value) in paramResult.Parameters)
            {
                scaffold.Variables.Set(name, value);
            }

            if (options.KernelOverride is not null)
            {
                notebook.DefaultKernelId = options.KernelOverride;
            }

            // Set up cancellation: link external token with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeoutCts.Token);
            var ct = linkedCts.Token;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<ExecutionResult>();
            var timedOut = false;

            try
            {
                var cellsToExecute = ResolveCells(notebook, options.CellSelectors);

                if (cellsToExecute is not null)
                {
                    // Execute specific cells
                    var total = cellsToExecute.Count;
                    for (var i = 0; i < total; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var cellId = cellsToExecute[i];
                        var cell = notebook.Cells.FirstOrDefault(c => c.Id == cellId);
                        var lang = cell?.Language ?? "unknown";

                        if (options.Verbose)
                            Console.Error.WriteLine($"[{i}/{total}] Executing cell {i} ({lang})...");

                        var cellSw = System.Diagnostics.Stopwatch.StartNew();
                        var result = await scaffold.ExecuteCellAsync(cellId, ct);
                        cellSw.Stop();
                        results.Add(result);

                        if (options.Verbose)
                            Console.Error.WriteLine($"[{i}/{total}] Cell {i} completed in {cellSw.Elapsed.TotalSeconds:F1}s ({result.Status})");

                        if (options.FailFast && CellHasErrors(cellId, notebook, result))
                            break;
                    }
                }
                else if (options.FailFast)
                {
                    // Execute all cells manually for fail-fast support
                    var total = notebook.Cells.Count;
                    for (var i = 0; i < total; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var cell = notebook.Cells[i];

                        if (options.Verbose)
                            Console.Error.WriteLine($"[{i}/{total}] Executing cell {i} ({cell.Language ?? "unknown"})...");

                        var cellSw = System.Diagnostics.Stopwatch.StartNew();
                        var result = await scaffold.ExecuteCellAsync(cell.Id, ct);
                        cellSw.Stop();
                        results.Add(result);

                        if (options.Verbose)
                            Console.Error.WriteLine($"[{i}/{total}] Cell {i} completed in {cellSw.Elapsed.TotalSeconds:F1}s ({result.Status})");

                        if (CellHasErrors(cell.Id, notebook, result))
                            break;
                    }
                }
                else
                {
                    // ExecuteAllAsync doesn't support per-cell verbose callbacks,
                    // so when verbose is on, execute cells individually
                    if (options.Verbose)
                    {
                        var total = notebook.Cells.Count;
                        for (var i = 0; i < total; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var cell = notebook.Cells[i];

                            Console.Error.WriteLine($"[{i}/{total}] Executing cell {i} ({cell.Language ?? "unknown"})...");

                            var cellSw = System.Diagnostics.Stopwatch.StartNew();
                            var result = await scaffold.ExecuteCellAsync(cell.Id, ct);
                            cellSw.Stop();
                            results.Add(result);

                            Console.Error.WriteLine($"[{i}/{total}] Cell {i} completed in {cellSw.Elapsed.TotalSeconds:F1}s ({result.Status})");
                        }
                    }
                    else
                    {
                        var allResults = await scaffold.ExecuteAllAsync(ct);
                        results.AddRange(allResults);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }

            // The .NET scripting engine cannot cancel user code mid-execution.
            // CancellationToken is checked between script steps, but not within awaited
            // tasks that don't propagate it (e.g. Task.Delay without a token). If the
            // timeout CTS fired but the kernel didn't throw OperationCanceledException,
            // detect it here after the cell completes.
            if (!timedOut && timeoutCts.IsCancellationRequested)
                timedOut = true;

            sw.Stop();

            // Save notebook if requested
            if (options.Save)
            {
                var serialized = await serializer.SerializeAsync(notebook);
                await File.WriteAllTextAsync(filePath, serialized, CancellationToken.None);
            }

            // Determine exit code
            // The Verso engine kernel may catch exceptions internally and report them
            // as error outputs while still returning ExecutionResult.Success.
            // We check both the result status AND the cell outputs for errors.
            int exitCode;
            if (timedOut)
            {
                exitCode = ExitCodes.Timeout;
            }
            else if (HasAnyFailure(notebook, results))
            {
                exitCode = ExitCodes.CellFailure;
            }
            else
            {
                exitCode = ExitCodes.Success;
            }

            return new RunResult
            {
                ExitCode = exitCode,
                CellResults = results,
                Cells = notebook.Cells,
                TotalElapsed = sw.Elapsed,
                Variables = options.Verbose ? scaffold.Variables.GetAll() : null,
                ResolvedParameters = paramResult.Parameters.Count > 0 ? paramResult.Parameters : null,
                NotebookPath = filePath
            };
        }
        finally
        {
            await extensionHost.DisposeAsync();
        }
    }

    /// <summary>
    /// Resolves cell selectors (indexes or GUIDs) to a list of cell IDs.
    /// Returns null if no selectors are specified (execute all).
    /// </summary>
    private static List<Guid>? ResolveCells(NotebookModel notebook, string[]? selectors)
    {
        if (selectors is null || selectors.Length == 0) return null;

        var resolved = new List<Guid>();
        foreach (var selector in selectors)
        {
            if (Guid.TryParse(selector, out var guid))
            {
                resolved.Add(guid);
            }
            else if (int.TryParse(selector, out var index) && index >= 0 && index < notebook.Cells.Count)
            {
                resolved.Add(notebook.Cells[index].Id);
            }
            else
            {
                throw new ArgumentException($"Invalid cell selector '{selector}'. Use a 0-based index or a cell GUID.");
            }
        }
        return resolved;
    }

    /// <summary>
    /// Checks whether a cell had errors, either via the ExecutionResult status
    /// or by inspecting cell outputs for error markers. The Verso kernel may
    /// catch exceptions and report them as error outputs while returning Success.
    /// </summary>
    private static bool CellHasErrors(Guid cellId, NotebookModel notebook, ExecutionResult result)
    {
        if (result.Status == ExecutionResult.ExecutionStatus.Failed)
            return true;

        var cell = notebook.Cells.FirstOrDefault(c => c.Id == cellId);
        return cell?.Outputs.Any(o => o.IsError) ?? false;
    }

    /// <summary>
    /// Checks whether any executed cell had failures.
    /// </summary>
    private static bool HasAnyFailure(NotebookModel notebook, IReadOnlyList<ExecutionResult> results)
    {
        if (results.Any(r => r.Status == ExecutionResult.ExecutionStatus.Failed))
            return true;

        // Also check cell outputs for errors that the kernel handled internally
        foreach (var result in results)
        {
            var cell = notebook.Cells.FirstOrDefault(c => c.Id == result.CellId);
            if (cell?.Outputs.Any(o => o.IsError) == true)
                return true;
        }

        return false;
    }
}
