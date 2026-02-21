using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class CellHandler
{
    public static CellDto HandleAdd(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellAddParams>(JsonRpcMessage.SerializerOptions)
            ?? new CellAddParams();

        var cell = ns.Scaffold.AddCell(p.Type, p.Language, p.Source);
        return NotebookHandler.MapCell(cell);
    }

    public static CellDto HandleInsert(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellInsertParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/insert");

        var cell = ns.Scaffold.InsertCell(p.Index, p.Type, p.Language, p.Source);
        return NotebookHandler.MapCell(cell);
    }

    public static object HandleRemove(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellRemoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/remove");

        var removed = ns.Scaffold.RemoveCell(Guid.Parse(p.CellId));
        return new { success = removed };
    }

    public static object HandleMove(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellMoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/move");

        ns.Scaffold.MoveCell(p.FromIndex, p.ToIndex);
        return new { success = true };
    }

    public static object HandleUpdateSource(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellUpdateSourceParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/updateSource");

        ns.Scaffold.UpdateCellSource(Guid.Parse(p.CellId), p.Source);
        return new { success = true };
    }

    public static object HandleChangeType(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellChangeTypeParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/changeType");

        var cellId = Guid.Parse(p.CellId);
        var cell = ns.Scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null)
            return new { success = false };

        if (!string.Equals(cell.Type, p.Type, StringComparison.OrdinalIgnoreCase))
        {
            var extHost = ns.ExtensionHost;
            var cellType = extHost.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, p.Type, StringComparison.OrdinalIgnoreCase));

            string? language = cellType?.Kernel?.LanguageId;
            if (language is null)
            {
                var hasRenderer = extHost.GetRenderers()
                    .Any(r => string.Equals(r.CellTypeId, p.Type, StringComparison.OrdinalIgnoreCase));
                if (!hasRenderer)
                    language = ns.Scaffold.DefaultKernelId ?? "csharp";
            }

            cell.Type = p.Type;
            cell.Language = language;
            cell.Outputs.Clear();
        }

        return new { success = true };
    }

    public static CellDto? HandleGet(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellGetParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/get");

        var cell = ns.Scaffold.GetCell(Guid.Parse(p.CellId));
        return cell is null ? null : NotebookHandler.MapCell(cell);
    }

    public static object HandleList(NotebookSession ns)
    {
        return new { cells = ns.Scaffold.Cells.Select(NotebookHandler.MapCell).ToList() };
    }
}
