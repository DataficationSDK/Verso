using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class CellHandler
{
    // The cell/* mutation RPCs can be driven by clients other than the notebook view itself
    // (the VS Code chat tools call them directly), so the host must announce structural
    // changes: the client re-pulls its cell cache and re-renders the active layout's slots.
    // Without this an externally added cell sits in the client's hidden cell pool with no
    // slot to portal into and never appears until the notebook is reopened. A view that
    // initiated the mutation itself just performs one redundant, coalesced refresh.
    private static void NotifyCellsChanged(NotebookSession ns) =>
        ns.SendNotification(MethodNames.NotebookCellsChanged);

    public static CellDto HandleAdd(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellAddParams>(JsonRpcMessage.SerializerOptions)
            ?? new CellAddParams();

        var cell = ns.Scaffold.AddCell(p.Type, p.Language, p.Source);
        NotifyCellsChanged(ns);
        return NotebookHandler.MapCell(cell);
    }

    public static CellDto HandleInsert(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellInsertParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/insert");

        var cell = ns.Scaffold.InsertCell(p.Index, p.Type, p.Language, p.Source);
        NotifyCellsChanged(ns);
        return NotebookHandler.MapCell(cell);
    }

    public static object HandleRemove(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellRemoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/remove");

        var removed = ns.Scaffold.RemoveCell(Guid.Parse(p.CellId));
        if (removed)
            NotifyCellsChanged(ns);
        return new { success = removed };
    }

    public static object HandleMove(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellMoveParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/move");

        ns.Scaffold.MoveCell(p.FromIndex, p.ToIndex);
        if (p.FromIndex != p.ToIndex)
            NotifyCellsChanged(ns);
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

            // Layout chrome is type-sensitive (per-type slot classes, heading folding),
            // so a type change needs the same re-render as an add/remove.
            NotifyCellsChanged(ns);
        }

        return new { success = true };
    }

    public static object HandleChangeLanguage(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellChangeLanguageParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/changeLanguage");

        var cellId = Guid.Parse(p.CellId);
        var cell = ns.Scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null)
            return new { success = false };

        var language = p.Language;
        if (!ns.Scaffold.RegisteredLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            return new { success = false };

        if (!string.Equals(cell.Language, language, StringComparison.OrdinalIgnoreCase))
        {
            cell.Language = language;
            cell.Outputs.Clear();

            // Eagerly warm up the target kernel so IntelliSense is ready immediately
            _ = Task.Run(async () =>
            {
                try { await ns.Scaffold.WarmUpKernelAsync(language); }
                catch { /* warm-up failure is non-fatal */ }
            });
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
