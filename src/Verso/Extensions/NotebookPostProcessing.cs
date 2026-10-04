using Verso.Abstractions;

namespace Verso.Extensions;

/// <summary>
/// Runs the loaded <see cref="INotebookPostProcessor"/> extensions around reading and writing a
/// notebook, so every host that opens or saves a file applies them by the same rule: those for
/// which <see cref="INotebookPostProcessor.CanProcess"/> is true, in <see cref="INotebookPostProcessor.Priority"/> order.
/// </summary>
internal static class NotebookPostProcessing
{
    /// <summary>
    /// The format identifier post-processors are given when a notebook is written in the native
    /// format, whose serializer reports itself as <c>verso</c>.
    /// </summary>
    internal const string NativeSaveFormatId = "verso-native";

    /// <summary>
    /// Runs <see cref="INotebookPostProcessor.PostDeserializeAsync"/> on a notebook just read,
    /// returning the notebook the last post-processor produced.
    /// </summary>
    /// <param name="host">The extension host whose post-processors apply.</param>
    /// <param name="notebook">The notebook as the serializer produced it.</param>
    /// <param name="filePath">The file it was read from, when there is one.</param>
    /// <param name="formatId">The <see cref="INotebookSerializer.FormatId"/> of the serializer that read it.</param>
    internal static async Task<NotebookModel> AfterDeserializeAsync(
        IExtensionHostContext host, NotebookModel notebook, string? filePath, string formatId)
    {
        foreach (var postProcessor in Matching(host, filePath, formatId))
            notebook = await postProcessor.PostDeserializeAsync(notebook, filePath).ConfigureAwait(false);
        return notebook;
    }

    /// <summary>
    /// Runs <see cref="INotebookPostProcessor.PreSerializeAsync"/> on a notebook about to be
    /// written, returning the notebook that should be serialized.
    /// </summary>
    /// <param name="host">The extension host whose post-processors apply.</param>
    /// <param name="notebook">The notebook being saved.</param>
    /// <param name="filePath">The file it will be written to, when there is one.</param>
    /// <param name="formatId">The <see cref="INotebookSerializer.FormatId"/> of the serializer that will write it.</param>
    internal static async Task<NotebookModel> BeforeSerializeAsync(
        IExtensionHostContext host, NotebookModel notebook, string? filePath, string formatId)
    {
        var saveFormatId = string.Equals(formatId, "verso", StringComparison.OrdinalIgnoreCase)
            ? NativeSaveFormatId
            : formatId;

        foreach (var postProcessor in Matching(host, filePath, saveFormatId))
            notebook = await postProcessor.PreSerializeAsync(notebook, filePath).ConfigureAwait(false);
        return notebook;
    }

    private static IEnumerable<INotebookPostProcessor> Matching(
        IExtensionHostContext host, string? filePath, string formatId)
        => host.GetPostProcessors()
            .Where(pp => pp.CanProcess(filePath, formatId))
            .OrderBy(pp => pp.Priority)
            .ToList();
}
