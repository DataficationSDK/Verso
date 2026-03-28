using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class InteractionHandler
{
    public static async Task<CellInteractResult> HandleInteractAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CellInteractParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for cell/interact");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException($"Invalid CellId: {p.CellId}");

        if (!Enum.TryParse<CellRegion>(p.Region, ignoreCase: true, out var region))
            region = CellRegion.Output; // Default to Output for interactions from rendered forms

        var handler = ns.ExtensionHost.GetInteractionHandler(p.ExtensionId)
            ?? throw new InvalidOperationException($"No interaction handler found for extension '{p.ExtensionId}'.");

        var context = new CellInteractionContext
        {
            Region = region,
            InteractionType = p.InteractionType,
            Payload = p.Payload,
            OutputBlockId = p.OutputBlockId,
            CellId = cellId,
            ExtensionId = p.ExtensionId,
            CancellationToken = CancellationToken.None,
            Variables = ns.Scaffold.Variables,
            Notebook = ns.Scaffold.NotebookOps,
            NotebookModel = ns.Scaffold.Notebook
        };

        var response = await handler.OnCellInteractionAsync(context);

        return new CellInteractResult { Response = response };
    }
}
