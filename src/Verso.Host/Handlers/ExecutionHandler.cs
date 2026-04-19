using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class ExecutionHandler
{
    public static async Task<ExecutionResultDto> HandleRunAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ExecutionRunParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for execution/run");

        var cellId = Guid.Parse(p.CellId);
        var ct = ns.GetExecutionToken();

        // Notify: execution started
        ns.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
        {
            CellId = p.CellId,
            State = "running"
        });

        var result = await ns.Scaffold.ExecuteCellAsync(cellId, ct);

        // Notify: execution completed
        var finalState = result.Status switch
        {
            Execution.ExecutionResult.ExecutionStatus.Success => "completed",
            Execution.ExecutionResult.ExecutionStatus.Cancelled => "cancelled",
            _ => "failed"
        };
        ns.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
        {
            CellId = p.CellId,
            State = finalState
        });

        // Notify: variables may have changed (kernels publish variables after execution)
        ns.SendNotification(MethodNames.VariableChanged);

        // Fetch cell outputs after execution
        var cell = ns.Scaffold.GetCell(cellId);
        var outputs = cell?.Outputs.Select(NotebookHandler.MapOutput).ToList() ?? new List<CellOutputDto>();

        return new ExecutionResultDto
        {
            CellId = p.CellId,
            Status = finalState,
            ExecutionCount = result.ExecutionCount,
            ElapsedMs = result.Elapsed.TotalMilliseconds,
            Outputs = outputs,
            ErrorMessage = result.Error?.Message
        };
    }

    public static async Task<object> HandleRunAllAsync(NotebookSession ns)
    {
        var ct = ns.GetExecutionToken();

        // Forward per-cell Scaffold events as JSON-RPC notifications so the
        // browser sees progress incrementally during the batch rather than
        // in a single burst at the end. Mirrors the single-cell path above.
        void OnExecuting(Guid id) =>
            ns.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
            {
                CellId = id.ToString(),
                State = "running"
            });

        void OnExecuted(Guid id)
        {
            var cell = ns.Scaffold.GetCell(id);
            var state = cell?.LastStatus switch
            {
                nameof(Execution.ExecutionResult.ExecutionStatus.Success) => "completed",
                nameof(Execution.ExecutionResult.ExecutionStatus.Cancelled) => "cancelled",
                _ => "failed"
            };
            ns.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
            {
                CellId = id.ToString(),
                State = state
            });
        }

        ns.Scaffold.OnCellExecuting += OnExecuting;
        ns.Scaffold.OnCellExecuted += OnExecuted;

        IReadOnlyList<Execution.ExecutionResult> results;
        try
        {
            results = await ns.Scaffold.ExecuteAllAsync(ct);
        }
        finally
        {
            ns.Scaffold.OnCellExecuting -= OnExecuting;
            ns.Scaffold.OnCellExecuted -= OnExecuted;
        }

        // Notify: variables may have changed after running all cells
        ns.SendNotification(MethodNames.VariableChanged);

        return new
        {
            results = results.Select(r =>
            {
                var cell = ns.Scaffold.GetCell(r.CellId);
                return new ExecutionResultDto
                {
                    CellId = r.CellId.ToString(),
                    Status = r.Status switch
                    {
                        Execution.ExecutionResult.ExecutionStatus.Success => "completed",
                        Execution.ExecutionResult.ExecutionStatus.Cancelled => "cancelled",
                        _ => "failed"
                    },
                    ExecutionCount = r.ExecutionCount,
                    ElapsedMs = r.Elapsed.TotalMilliseconds,
                    Outputs = cell?.Outputs.Select(NotebookHandler.MapOutput).ToList() ?? new List<CellOutputDto>(),
                    ErrorMessage = r.Error?.Message
                };
            }).ToList()
        };
    }

    public static object HandleCancel(NotebookSession ns)
    {
        ns.CancelExecution();
        return new { success = true };
    }
}
