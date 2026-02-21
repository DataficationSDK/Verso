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
            // Select serializer based on file path hint, content sniffing, or default to VersoSerializer
            INotebookSerializer serializer = new VersoSerializer();
            if (!string.IsNullOrEmpty(p.FilePath))
            {
                serializer = extensionHost.GetSerializers()
                    .FirstOrDefault(s => s.CanImport(p.FilePath))
                    ?? serializer;
            }
            else if (LooksLikeJupyterNotebook(p.Content))
            {
                serializer = extensionHost.GetSerializers()
                    .FirstOrDefault(s => s.CanImport("notebook.ipynb"))
                    ?? serializer;
            }

            notebook = await serializer.DeserializeAsync(p.Content);

            // Run post-processors after deserialization
            var postProcessors = extensionHost.GetPostProcessors()
                .Where(pp => pp.CanProcess(p.FilePath, serializer.FormatId))
                .OrderBy(pp => pp.Priority);
            foreach (var pp in postProcessors)
                notebook = await pp.PostDeserializeAsync(notebook, p.FilePath);
        }

        // Ensure essential metadata defaults are present so subsystems, the
        // metadata panel, and newly created cells all behave correctly even when
        // the file is empty or has a blank metadata section.
        notebook.DefaultKernelId ??= "csharp";
        notebook.ActiveLayoutId ??= "notebook";

        var scaffold = new Scaffold(notebook, extensionHost, p.FilePath);
        scaffold.InitializeSubsystems();

        // Diagnostic: log loaded extensions to stderr (captured by VS Code extension host)
        var kernels = extensionHost.GetKernels();
        var magicCommands = extensionHost.GetMagicCommands();
        Console.Error.WriteLine($"[Verso] notebook/open: {scaffold.Cells.Count} cells, " +
            $"{kernels.Count} kernels ({string.Join(", ", kernels.Select(k => k.LanguageId))}), " +
            $"{magicCommands.Count} magic commands ({string.Join(", ", magicCommands.Select(m => m.Name))})");

        session.SetSession(scaffold, extensionHost);

        return new NotebookOpenResult
        {
            Title = notebook.Title,
            DefaultKernel = notebook.DefaultKernelId,
            Cells = scaffold.Cells.Select(MapCell).ToList()
        };
    }

    public static object? HandleSetFilePath(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<NotebookSetFilePathParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for notebook/setFilePath");

        session.Scaffold!.SetFilePath(p.FilePath);
        return null;
    }

    public static async Task<NotebookSaveResult> HandleSaveAsync(HostSession session)
    {
        session.EnsureSession();
        // Flush layout metadata (grid positions, etc.) into the notebook model
        if (session.Scaffold!.LayoutManager is { } lm)
            await lm.SaveMetadataAsync(session.Scaffold!.Notebook);
        // Run post-processors before serialization
        var notebook = session.Scaffold!.Notebook;
        var postProcessors = session.ExtensionHost!.GetPostProcessors()
            .Where(pp => pp.CanProcess(null, "verso-native"))
            .OrderBy(pp => pp.Priority);
        foreach (var pp in postProcessors)
            notebook = await pp.PreSerializeAsync(notebook, null);

        var serializer = new VersoSerializer();
        var content = await serializer.SerializeAsync(notebook);
        return new NotebookSaveResult { Content = content };
    }

    public static LanguagesResult HandleGetLanguages(HostSession session)
    {
        session.EnsureSession();
        var scaffold = session.Scaffold!;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var languages = new List<LanguageDto>();

        // From registered kernels on scaffold
        foreach (var langId in scaffold.RegisteredLanguages)
        {
            if (!seen.Add(langId)) continue;
            var kernel = scaffold.GetKernel(langId);
            languages.Add(new LanguageDto
            {
                Id = langId,
                DisplayName = kernel?.DisplayName ?? langId
            });
        }

        return new LanguagesResult { Languages = languages };
    }

    public static CellTypesResult HandleGetCellTypes(HostSession session)
    {
        session.EnsureSession();
        var types = new List<CellTypeDto> { new() { Id = "code", DisplayName = "Code" } };

        var extHost = session.ExtensionHost!;

        var hasMarkdown = extHost.GetCellTypes()
            .Any(ct => string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
            || extHost.GetRenderers()
            .Any(r => string.Equals(r.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase));
        if (hasMarkdown)
            types.Add(new() { Id = "markdown", DisplayName = "Markdown" });

        foreach (var ct in extHost.GetCellTypes())
        {
            if (!string.Equals(ct.CellTypeId, "code", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
                types.Add(new() { Id = ct.CellTypeId, DisplayName = ct.DisplayName });
        }

        return new CellTypesResult { CellTypes = types };
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

    /// <summary>
    /// Quick content sniff to detect Jupyter .ipynb format when no file path is available.
    /// Checks for the <c>"nbformat"</c> top-level key which is present in all valid ipynb files.
    /// </summary>
    private static bool LooksLikeJupyterNotebook(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("nbformat", out _);
        }
        catch
        {
            return false;
        }
    }
}
