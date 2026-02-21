using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;

namespace Verso.Host.Handlers;

public static class ExtensionHandler
{
    public static ExtensionListResult HandleList(NotebookSession ns)
    {
        var infos = ns.ExtensionHost.GetExtensionInfos();
        return new ExtensionListResult
        {
            Extensions = infos.Select(i => new ExtensionInfoDto
            {
                ExtensionId = i.ExtensionId,
                Name = i.Name,
                Version = i.Version,
                Author = i.Author,
                Description = i.Description,
                Status = i.Status.ToString(),
                Capabilities = i.Capabilities.ToList()
            }).ToList()
        };
    }

    public static async Task<ExtensionListResult> HandleEnableAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        await ns.ExtensionHost.EnableExtensionAsync(extensionId);
        return HandleList(ns);
    }

    public static async Task<ExtensionListResult> HandleDisableAsync(NotebookSession ns, JsonElement? @params)
    {
        var extensionId = @params?.GetProperty("extensionId").GetString()
            ?? throw new JsonException("Missing extensionId");
        await ns.ExtensionHost.DisableExtensionAsync(extensionId);
        return HandleList(ns);
    }
}
