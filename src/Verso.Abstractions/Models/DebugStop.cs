namespace Verso.Abstractions;

/// <summary>
/// A report that a cell has stopped, made by a kernel through <see cref="IDebugSession.StopAsync"/>.
/// </summary>
/// <param name="Reason">Why the cell stopped.</param>
/// <param name="Frames">
/// The chain of calls that led here, innermost first, so the first entry is where the cell
/// stopped. Never empty.
/// </param>
public sealed record DebugStop(
    DebugStopReason Reason,
    IReadOnlyList<DebugFrame> Frames);

/// <summary>
/// One call in the chain that led to a stop.
/// </summary>
/// <param name="Name">
/// The name of the function this frame is running, as the user would recognize it. Empty for code
/// that sits outside any function, which a host presents as the cell itself.
/// </param>
/// <param name="CellId">
/// The cell this frame's code belongs to, or <c>null</c> when the code is not in any cell.
/// </param>
/// <param name="Line">
/// The line the frame is at, counting from 1 in the code the kernel was given for
/// <paramref name="CellId"/>. Zero when unknown.
/// </param>
/// <param name="Variables">
/// What is in scope in this frame. A kernel that can only inspect the innermost frame leaves the
/// others empty.
/// </param>
public sealed record DebugFrame(
    string Name,
    Guid? CellId,
    int Line,
    IReadOnlyList<DebugVariable> Variables);

/// <summary>
/// A variable in scope at a stop, already reduced to text a host can show as is.
/// </summary>
/// <param name="Name">The variable's name as written in the code.</param>
/// <param name="TypeName">A display name for the value's type, or empty when unknown.</param>
/// <param name="Value">A short rendering of the value.</param>
public sealed record DebugVariable(
    string Name,
    string TypeName,
    string Value);
