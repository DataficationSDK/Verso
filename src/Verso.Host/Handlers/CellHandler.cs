using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class CellHandler
{
    public static CellDto HandleAdd(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellAddParams>(JsonRpcMessage.SerializerOptions)
            ?? new CellAddParams();

        var cell = session.Scaffold!.AddCell(p.Type, p.Language, p.Source);
        return NotebookHandler.MapCell(cell);
    }

    public static CellDto HandleInsert(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellInsertParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/insert");

        var cell = session.Scaffold!.InsertCell(p.Index, p.Type, p.Language, p.Source);
        return NotebookHandler.MapCell(cell);
    }

    public static object HandleRemove(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellRemoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/remove");

        var removed = session.Scaffold!.RemoveCell(Guid.Parse(p.CellId));
        return new { success = removed };
    }

    public static object HandleMove(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellMoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/move");

        session.Scaffold!.MoveCell(p.FromIndex, p.ToIndex);
        return new { success = true };
    }

    public static object HandleUpdateSource(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellUpdateSourceParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/updateSource");

        session.Scaffold!.UpdateCellSource(Guid.Parse(p.CellId), p.Source);
        return new { success = true };
    }

    public static CellDto? HandleGet(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CellGetParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/get");

        var cell = session.Scaffold!.GetCell(Guid.Parse(p.CellId));
        return cell is null ? null : NotebookHandler.MapCell(cell);
    }

    public static object HandleList(HostSession session)
    {
        session.EnsureSession();
        return new { cells = session.Scaffold!.Cells.Select(NotebookHandler.MapCell).ToList() };
    }
}
