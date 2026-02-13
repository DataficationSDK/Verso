using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class ExecutionHandler
{
    public static async Task<ExecutionResultDto> HandleRunAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<ExecutionRunParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for execution/run");

        var cellId = Guid.Parse(p.CellId);
        var ct = session.GetExecutionToken();

        // Notify: execution started
        session.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
        {
            CellId = p.CellId,
            State = "running"
        });

        var result = await session.Scaffold!.ExecuteCellAsync(cellId, ct);

        // Notify: execution completed
        var finalState = result.Status switch
        {
            Execution.ExecutionResult.ExecutionStatus.Success => "completed",
            Execution.ExecutionResult.ExecutionStatus.Cancelled => "cancelled",
            _ => "failed"
        };
        session.SendNotification(MethodNames.CellExecutionState, new ExecutionStateNotification
        {
            CellId = p.CellId,
            State = finalState
        });

        // Fetch cell outputs after execution
        var cell = session.Scaffold.GetCell(cellId);
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

    public static async Task<object> HandleRunAllAsync(HostSession session)
    {
        session.EnsureSession();
        var ct = session.GetExecutionToken();
        var results = await session.Scaffold!.ExecuteAllAsync(ct);

        return new
        {
            results = results.Select(r =>
            {
                var cell = session.Scaffold.GetCell(r.CellId);
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

    public static object HandleCancel(HostSession session)
    {
        session.CancelExecution();
        return new { success = true };
    }
}
