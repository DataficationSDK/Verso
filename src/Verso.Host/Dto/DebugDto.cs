namespace Verso.Host.Dto;

// --- Debugging ---
//
// The whole state travels every time it changes, so a client never has to piece the picture
// together from deltas. The notifications are written in the order the state was read, which
// makes the latest one the truth. The two requests that change state also answer with it, for a
// caller that is not listening, but an answer can be older than a notification written just
// before it and a client that hears both should go by the notifications.

// debug/setEnabled

public sealed class DebugSetEnabledParams
{
    public bool Enabled { get; set; }
}

// debug/setBreakpoints

public sealed class DebugSetBreakpointsParams
{
    public string CellId { get; set; } = "";

    /// <summary>The cell's breakpoint lines, counting from 1 in the cell's own text.</summary>
    public List<int> Lines { get; set; } = new();
}

// debug/resume

public sealed class DebugResumeParams
{
    /// <summary>One of <c>continue</c>, <c>stepOver</c>, <c>stepInto</c>, <c>stepOut</c>.</summary>
    public string Action { get; set; } = "";
}

public sealed class DebugResumeResult
{
    /// <summary><c>false</c> when no cell was stopped.</summary>
    public bool Resumed { get; set; }
}

// debug/stateChanged, and the result of the two requests that change state

public sealed class DebugStateDto
{
    public bool Enabled { get; set; }
    public List<DebugCellBreakpointsDto> Breakpoints { get; set; } = new();

    /// <summary>Where a cell is stopped, or absent when nothing is stopped.</summary>
    public DebugStopDto? Stop { get; set; }
}

public sealed class DebugCellBreakpointsDto
{
    public string CellId { get; set; } = "";
    public List<int> Lines { get; set; } = new();
}

public sealed class DebugStopDto
{
    /// <summary><c>breakpoint</c> or <c>step</c>.</summary>
    public string Reason { get; set; } = "";

    /// <summary>The chain of calls, innermost first.</summary>
    public List<DebugFrameDto> Frames { get; set; } = new();
}

public sealed class DebugFrameDto
{
    /// <summary>The function's name, or empty for code outside any function.</summary>
    public string Name { get; set; } = "";

    /// <summary>The cell the frame's code belongs to, or absent when it is in no cell.</summary>
    public string? CellId { get; set; }

    /// <summary>The line in that cell's own text, counting from 1, or 0 when unknown.</summary>
    public int Line { get; set; }

    public List<DebugVariableDto> Variables { get; set; } = new();
}

public sealed class DebugVariableDto
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Value { get; set; } = "";
}
