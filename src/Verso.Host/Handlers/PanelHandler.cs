using System.Text.Json;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class PanelHandler
{
    public static async Task<PanelListResult> HandleListAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<PanelListParams>(JsonRpcMessage.SerializerOptions)
            ?? new PanelListParams();

        var context = new PanelContext(ns.Scaffold, ParseCellId(p.SelectedCellId));
        var result = new PanelListResult();

        foreach (var panel in ns.ExtensionHost.GetPanels())
        {
            try
            {
                if (!await panel.IsAvailableAsync(context)) continue;
                result.Panels.Add(new PanelInfoDto
                {
                    PanelId = panel.PanelId,
                    ExtensionId = panel.ExtensionId,
                    DisplayName = panel.DisplayName,
                    IconName = panel.IconName,
                    IconMarkup = panel.IconMarkup,
                    Order = panel.Order
                });
            }
            catch
            {
                // A panel that throws while reporting availability is treated as
                // unavailable rather than failing the whole list.
            }
        }

        return result;
    }

    public static async Task<PanelRenderResult> HandleRenderAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<PanelRenderParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for panel/render");

        var result = new PanelRenderResult();

        if (!ns.ExtensionHost.TryGetPanel(p.ExtensionId, p.PanelId, out var panel))
            return result;

        IReadOnlyList<RenderResult> representations;
        try
        {
            representations = await panel.RenderAsync(
                new PanelContext(ns.Scaffold, ParseCellId(p.SelectedCellId)));
        }
        catch
        {
            // A panel that throws while rendering shows as empty rather than failing
            // the request, matching how a failing property provider is skipped.
            return result;
        }

        foreach (var representation in representations)
        {
            result.Representations.Add(new PanelRepresentationDto
            {
                MimeType = representation.MimeType,
                Content = representation.Content
            });
        }

        return result;
    }

    public static async Task<object?> HandleInteractAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<PanelInteractParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for panel/interact");

        if (!ns.ExtensionHost.TryGetPanelInteractionHandler(p.ExtensionId, p.PanelId, out var handler))
            return null;

        var selectedCellId = ParseCellId(p.SelectedCellId);

        var context = new PanelInteractionContext
        {
            ExtensionId = p.ExtensionId,
            PanelId = p.PanelId,
            InteractionType = p.InteractionType,
            Payload = p.Payload,
            TargetId = p.TargetId,
            SelectedCellId = selectedCellId,
            Verso = new PanelContext(ns.Scaffold, selectedCellId),
            RequestRefresh = () => ns.SendPanelUpdated(p.ExtensionId, p.PanelId)
        };

        await handler.OnPanelInteractionAsync(context);
        return null;
    }

    private static Guid? ParseCellId(string? value)
        => Guid.TryParse(value, out var id) ? id : null;
}
