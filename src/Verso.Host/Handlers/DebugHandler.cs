using System.Text.Json;
using Verso.Abstractions;
using Verso.Debugging;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

/// <summary>
/// The debugging requests. All of them are answered while a cell is running, since a stopped cell
/// is a running cell and these are the requests that move it on; see
/// <see cref="HostSession.IsAnsweredWhileRunning"/>. Nothing here does more than change state the
/// debugger guards itself.
/// </summary>
public static class DebugHandler
{
    public static DebugStateDto HandleSetEnabled(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<DebugSetEnabledParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for debug/setEnabled");

        ns.Scaffold.Debugger.Enabled = p.Enabled;
        return Snapshot(ns.Scaffold.Debugger);
    }

    public static DebugStateDto HandleSetBreakpoints(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<DebugSetBreakpointsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for debug/setBreakpoints");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException("debug/setBreakpoints needs a cellId.");

        ns.Scaffold.Debugger.SetBreakpoints(cellId, p.Lines ?? new List<int>());
        return Snapshot(ns.Scaffold.Debugger);
    }

    public static DebugResumeResult HandleResume(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<DebugResumeParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for debug/resume");

        var action = ReadAction(p.Action)
            ?? throw new JsonException(
                "debug/resume needs an action of continue, stepOver, stepInto or stepOut.");

        return new DebugResumeResult { Resumed = ns.Scaffold.Debugger.Resume(action) };
    }

    // Matched by name rather than parsed as an enum, which would also take a number, a signed
    // number, or several names joined by commas, none of which a client ever means.
    private static DebugResumeAction? ReadAction(string? name)
    {
        if (string.Equals(name, "continue", StringComparison.OrdinalIgnoreCase)) return DebugResumeAction.Continue;
        if (string.Equals(name, "stepOver", StringComparison.OrdinalIgnoreCase)) return DebugResumeAction.StepOver;
        if (string.Equals(name, "stepInto", StringComparison.OrdinalIgnoreCase)) return DebugResumeAction.StepInto;
        if (string.Equals(name, "stepOut", StringComparison.OrdinalIgnoreCase)) return DebugResumeAction.StepOut;
        return null;
    }

    /// <summary>The debugger's state as it travels to the client.</summary>
    public static DebugStateDto Snapshot(NotebookDebugger debugger)
    {
        var stop = debugger.CurrentStop;
        return new DebugStateDto
        {
            Enabled = debugger.Enabled,
            Breakpoints = debugger.GetAllBreakpoints()
                .OrderBy(pair => pair.Key)
                .Select(pair => new DebugCellBreakpointsDto
                {
                    CellId = pair.Key.ToString(),
                    Lines = pair.Value.ToList()
                })
                .ToList(),
            Stop = stop is null ? null : MapStop(stop)
        };
    }

    private static DebugStopDto MapStop(DebugStop stop) => new()
    {
        Reason = stop.Reason == DebugStopReason.Breakpoint ? "breakpoint" : "step",
        Frames = stop.Frames.Select(frame => new DebugFrameDto
        {
            Name = frame.Name,
            CellId = frame.CellId?.ToString(),
            Line = frame.Line,
            Variables = frame.Variables.Select(variable => new DebugVariableDto
            {
                Name = variable.Name,
                TypeName = variable.TypeName,
                Value = variable.Value
            }).ToList()
        }).ToList()
    };
}
