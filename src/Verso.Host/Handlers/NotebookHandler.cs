using System.Text.Json;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Host.Dto;
using Verso.Host.Protocol;
using Verso.Serializers;

namespace Verso.Host.Handlers;

public static class NotebookHandler
{
    public static async Task<NotebookOpenResult> HandleOpenAsync(HostSession session, JsonElement? @params)
    {
        var p = @params?.Deserialize<NotebookOpenParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for notebook/open");

        var extensionHost = new ExtensionHost();
        await extensionHost.LoadBuiltInExtensionsAsync();

        NotebookModel notebook;
        if (string.IsNullOrWhiteSpace(p.Content))
        {
            notebook = new NotebookModel();
        }
        else
        {
            var serializer = new VersoSerializer();
            notebook = await serializer.DeserializeAsync(p.Content);
        }

        var scaffold = new Scaffold(notebook, extensionHost);
        scaffold.InitializeSubsystems();

        // Register discovered kernels
        foreach (var kernel in extensionHost.GetKernels())
            scaffold.RegisterKernel(kernel);

        session.SetSession(scaffold, extensionHost);

        return new NotebookOpenResult
        {
            Title = notebook.Title,
            DefaultKernel = notebook.DefaultKernelId,
            Cells = scaffold.Cells.Select(MapCell).ToList()
        };
    }

    public static async Task<NotebookSaveResult> HandleSaveAsync(HostSession session)
    {
        session.EnsureSession();
        var serializer = new VersoSerializer();
        var content = await serializer.SerializeAsync(session.Scaffold!.Notebook);
        return new NotebookSaveResult { Content = content };
    }

    public static LanguagesResult HandleGetLanguages(HostSession session)
    {
        session.EnsureSession();
        var scaffold = session.Scaffold!;
        var extensionHost = session.ExtensionHost!;

        var languages = new List<LanguageDto>();

        // From registered kernels on scaffold
        foreach (var langId in scaffold.RegisteredLanguages)
        {
            var kernel = scaffold.GetKernel(langId);
            languages.Add(new LanguageDto
            {
                Id = langId,
                DisplayName = kernel?.DisplayName ?? langId
            });
        }

        return new LanguagesResult { Languages = languages };
    }

    public static ToolbarActionsResult HandleGetToolbarActions(HostSession session)
    {
        session.EnsureSession();
        var actions = session.ExtensionHost!.GetToolbarActions();
        return new ToolbarActionsResult
        {
            Actions = actions.Select(a => new ToolbarActionDto
            {
                ActionId = a.ActionId,
                DisplayName = a.DisplayName,
                Icon = a.Icon,
                Placement = a.Placement.ToString(),
                Order = a.Order
            }).ToList()
        };
    }

    internal static CellDto MapCell(CellModel cell)
    {
        return new CellDto
        {
            Id = cell.Id.ToString(),
            Type = cell.Type,
            Language = cell.Language,
            Source = cell.Source,
            Outputs = cell.Outputs.Select(MapOutput).ToList()
        };
    }

    internal static CellOutputDto MapOutput(CellOutput output)
    {
        return new CellOutputDto
        {
            MimeType = output.MimeType,
            Content = output.Content,
            IsError = output.IsError,
            ErrorName = output.ErrorName,
            ErrorStackTrace = output.ErrorStackTrace
        };
    }
}
