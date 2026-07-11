using System.Text.Json;
using Verso.Abstractions;
using Verso.Diffing;
using Verso.Host.Dto;
using Verso.Host.Protocol;
using Verso.Serializers;

namespace Verso.Host.Handlers;

/// <summary>
/// Handles <c>notebook/diff</c>: parses a baseline notebook payload and compares it against the
/// live session's in-memory notebook (including unsaved edits). Read-only: nothing on the
/// session is mutated.
/// </summary>
public static class DiffHandler
{
    public static async Task<NotebookDiffResult> HandleDiffAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<NotebookDiffParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for notebook/diff");

        if (string.IsNullOrWhiteSpace(p.BaselineContent))
        {
            throw new InvalidOperationException("The baseline notebook is empty.");
        }

        // Serializer selection mirrors notebook/open: file-path extension first, then a
        // Jupyter content sniff, defaulting to the native format.
        INotebookSerializer serializer = new VersoSerializer();
        if (!string.IsNullOrEmpty(p.BaselineFilePath))
        {
            serializer = ns.ExtensionHost.GetSerializers()
                .FirstOrDefault(s => s.CanImport(p.BaselineFilePath))
                ?? serializer;
        }
        else if (NotebookHandler.LooksLikeJupyterNotebook(p.BaselineContent))
        {
            serializer = ns.ExtensionHost.GetSerializers()
                .FirstOrDefault(s => s.CanImport("notebook.ipynb"))
                ?? serializer;
        }

        NotebookModel baseline;
        try
        {
            baseline = await serializer.DeserializeAsync(p.BaselineContent);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not parse the baseline as a notebook: {ex.Message}", ex);
        }

        var postProcessors = ns.ExtensionHost.GetPostProcessors()
            .Where(pp => pp.CanProcess(p.BaselineFilePath, serializer.FormatId))
            .OrderBy(pp => pp.Priority);
        foreach (var pp in postProcessors)
        {
            baseline = await pp.PostDeserializeAsync(baseline, p.BaselineFilePath);
        }

        return NotebookDiffEngine.Compute(baseline, ns.Scaffold.Notebook, p.BaselineLabel);
    }
}
