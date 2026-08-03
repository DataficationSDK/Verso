using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Diffing;
using Verso.Serializers;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Services;

/// <summary>
/// Notebook diff support for the server host: lists comparison baselines (last saved file,
/// git refs, arbitrary files) and computes diffs against the live in-memory notebook.
/// </summary>
public sealed partial class ServerNotebookService
{
    public Task<IReadOnlyList<DiffSourceInfo>> GetDiffSourcesAsync()
    {
        var hasFilePath = !string.IsNullOrEmpty(_filePath);
        var inGitRepo = hasFilePath
            && Path.GetDirectoryName(Path.GetFullPath(_filePath!)) is { } directory
            && GitCliHelper.FindRepoRoot(directory) is not null;

        // Named through DiffSources so this host and the embedded one offer the same four
        // baselines under the same words, and a rename cannot reach one without the other.
        static DiffSourceInfo Describe(string id, string kind, bool available) =>
            new(id, DiffSources.NameOf(id)!, kind, available,
                available ? null : DiffSources.UnavailableReason(id));

        IReadOnlyList<DiffSourceInfo> sources = new List<DiffSourceInfo>
        {
            Describe(DiffSources.LastSaved, "lastSaved", hasFilePath),
            Describe(DiffSources.GitHead, "git", inGitRepo),
            Describe(DiffSources.GitRef, "git", inGitRepo),
            Describe(DiffSources.File, "file", true),
        };
        return Task.FromResult(sources);
    }

    public async Task<NotebookDiffResult?> ComputeDiffAsync(string sourceId, string? explicitInput = null)
    {
        if (_scaffold is null)
        {
            throw new InvalidOperationException(UI.Common_NoNotebookOpen);
        }

        var (content, baselinePath, label) = sourceId switch
        {
            DiffSources.LastSaved => await ReadLastSavedBaselineAsync(),
            DiffSources.GitHead => ReadGitBaseline("HEAD"),
            DiffSources.GitRef => ReadGitBaseline(
                !string.IsNullOrWhiteSpace(explicitInput)
                    ? explicitInput.Trim()
                    : throw new InvalidOperationException("A git ref is required to compare with a ref.")),
            DiffSources.File => await ReadFileBaselineAsync(
                !string.IsNullOrWhiteSpace(explicitInput)
                    ? explicitInput.Trim()
                    : throw new InvalidOperationException("A file path is required to compare with a file.")),
            _ => throw new ArgumentException($"Unknown comparison source '{sourceId}'.", nameof(sourceId)),
        };

        var baseline = await DeserializeBaselineAsync(content, baselinePath);

        // Layout state and extension settings live in their managers until save flushes them
        // into the model; flush here too, or unsaved pane and layout edits are invisible to
        // the comparison. Neither flush marks the notebook dirty.
        if (_scaffold.LayoutManager is { } lm)
            await lm.SaveMetadataAsync(_scaffold.Notebook);
        if (_scaffold.SettingsManager is { } sm)
            await sm.SaveSettingsAsync(_scaffold.Notebook);

        // The cell-type registry tells the comparison which outputs are derived rather than
        // authored, so re-rendered markup cells are not reported as edits.
        return NotebookDiffEngine.Compute(
            baseline, _scaffold.Notebook, label, _extensionHost?.GetCellTypes());
    }

    private async Task<(string Content, string BaselinePath, string Label)> ReadLastSavedBaselineAsync()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            throw new InvalidOperationException(UI.Compare_NotSavedYet);
        }

        if (!File.Exists(_filePath))
        {
            throw new InvalidOperationException(string.Format(UI.Compare_FileMissing, _filePath));
        }

        var content = await File.ReadAllTextAsync(_filePath);
        return (content, _filePath, DiffSources.ResolvedName(DiffSources.LastSaved, null));
    }

    private (string Content, string BaselinePath, string Label) ReadGitBaseline(string refName)
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            throw new InvalidOperationException(UI.Compare_NotSavedYet);
        }

        var (content, label) = GitCliHelper.Show(_filePath, refName);
        return (content, _filePath, label);
    }

    private async Task<(string Content, string BaselinePath, string Label)> ReadFileBaselineAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(string.Format(UI.Compare_FileMissing, path));
        }

        var content = await File.ReadAllTextAsync(path);
        return (content, path, DiffSources.ResolvedName(DiffSources.File, Path.GetFileName(path)));
    }

    /// <summary>
    /// Parses baseline content through the serializer registered for its file extension
    /// (defaulting to the native format) and the registered post-processors, so the baseline
    /// side gets the same normalization (format migrations, polyglot cell splitting) as a
    /// regular open.
    /// </summary>
    private async Task<NotebookModel> DeserializeBaselineAsync(string content, string baselinePath)
    {
        INotebookSerializer serializer = _extensionHost?.GetSerializers()
            .FirstOrDefault(s => s.CanImport(baselinePath))
            ?? new VersoSerializer();

        NotebookModel baseline;
        try
        {
            baseline = await serializer.DeserializeAsync(content);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                string.Format(UI.Compare_ParseFailed, Path.GetFileName(baselinePath), ex.Message), ex);
        }

        if (_extensionHost is not null)
        {
            var postProcessors = _extensionHost.GetPostProcessors()
                .Where(pp => pp.CanProcess(baselinePath, serializer.FormatId))
                .OrderBy(pp => pp.Priority);
            foreach (var postProcessor in postProcessors)
            {
                baseline = await postProcessor.PostDeserializeAsync(baseline, baselinePath);
            }
        }

        return baseline;
    }
}
