using Verso.Abstractions;
using Verso.Cli.Repl.Settings;
using Verso.Extensions;

namespace Verso.Cli.Repl;

/// <summary>
/// Holds the mutable state for one <c>verso repl</c> run: the in-memory
/// <see cref="NotebookModel"/> being edited, the <see cref="Scaffold"/>
/// executing its cells, and the currently active kernel / theme / layout
/// selections. A session is created by <see cref="ReplCommand"/> once and
/// handed to <see cref="ReplLoop"/> for the duration of the process.
/// </summary>
public sealed class ReplSession : IAsyncDisposable
{
    /// <summary>
    /// Notebook the session mutates. Each user submission becomes a new
    /// <see cref="CellModel"/> appended to <see cref="NotebookModel.Cells"/>.
    /// </summary>
    public NotebookModel Notebook { get; }

    /// <summary>Execution facade, reused across every cell for variable persistence.</summary>
    public Scaffold Scaffold { get; private set; }

    /// <summary>Extension host for enumeration and re-resolution during the session.</summary>
    public ExtensionHost ExtensionHost { get; }

    /// <summary>Active kernel id for the next cell. Mutated by <c>.kernel</c>.</summary>
    public string? ActiveKernelId { get; set; }

    /// <summary>Active theme id for rendering. Mutated by <c>.theme</c>.</summary>
    public ITheme? ActiveTheme { get; set; }

    /// <summary>Default layout id for <c>.export</c>. Mutated by <c>.layout</c>.</summary>
    public string? ActiveLayoutId { get; set; }

    /// <summary>Path the session's notebook was loaded from (null for scratch).</summary>
    public string? NotebookPath { get; set; }

    /// <summary>Monotonically increasing submission counter rendered in prompts and output frames.</summary>
    public int InputCounter { get; private set; }

    /// <summary>True once <see cref="MarkDirty"/> has been called and .save has not followed.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// One-shot "the user already saw an unsaved-work warning and is retrying the
    /// destructive op" flag. Cleared as soon as it is consumed by
    /// <see cref="ConfirmDiscardUnsavedChanges"/>, and whenever the session becomes
    /// clean (so a save-then-redirty cycle doesn't silently inherit stale consent).
    /// </summary>
    private bool _discardConfirmationPending;

    /// <summary>Next-submission cell type override (one-shot, set by <c>.md</c>/<c>.code</c>).</summary>
    public string? NextCellTypeOverride { get; set; }

    /// <summary>
    /// Text pre-loaded into the prompt's edit buffer at the next read cycle.
    /// Consumed exactly once; set by <c>.recall</c>.
    /// </summary>
    public string? PendingInitialText { get; set; }

    /// <summary>Runtime-mutable settings (row/line caps, elapsed threshold). Mutated by <c>.set</c>.</summary>
    public ReplSettings Settings { get; set; } = new();

    public ReplSession(NotebookModel notebook, Scaffold scaffold, ExtensionHost extensionHost, string? notebookPath)
    {
        Notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        Scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
        ExtensionHost = extensionHost ?? throw new ArgumentNullException(nameof(extensionHost));
        NotebookPath = notebookPath;
    }

    public int NextInputCounter() => ++InputCounter;

    public void MarkDirty()
    {
        IsDirty = true;
        // New user work invalidates any prior "retry to discard" consent: after
        // typing fresh cells the user should see a new warning before a destructive
        // meta-command proceeds.
        _discardConfirmationPending = false;
    }

    public void MarkClean()
    {
        IsDirty = false;
        // Clearing the dirty flag invalidates any prior "retry to discard" consent:
        // if the user dirties the session again later, they should see a fresh warning
        // before a destructive meta-command (.load, etc.) proceeds.
        _discardConfirmationPending = false;
    }

    /// <summary>
    /// Called by destructive meta-commands (e.g. <c>.load</c>) before they overwrite
    /// session state. Returns <c>true</c> if the caller may proceed — either because
    /// the session is clean, or because the user already saw the unsaved-work warning
    /// and is invoking the command a second time. Returns <c>false</c> on the first
    /// invocation against a dirty session; the caller should print a warning in that
    /// case and let the user decide whether to retry.
    /// </summary>
    public bool ConfirmDiscardUnsavedChanges()
    {
        if (!IsDirty)
        {
            _discardConfirmationPending = false;
            return true;
        }

        if (_discardConfirmationPending)
        {
            _discardConfirmationPending = false;
            return true;
        }

        _discardConfirmationPending = true;
        return false;
    }

    /// <summary>
    /// Replaces the current Scaffold with a fresh one bound to the same notebook and
    /// extension host. Used by <c>.reset</c> to clear kernel state while preserving cell history.
    /// </summary>
    /// <remarks>
    /// The previous Scaffold is intentionally not disposed here: <c>Scaffold.DisposeAsync</c>
    /// disposes the <see cref="Extensions.ExtensionHost"/> it was handed, which would tear
    /// down extensions still in use by the new Scaffold. Old kernel instances are left
    /// running until process exit. This is consistent with the REPL contract that kernel
    /// state is not hibernated across invocations.
    /// </remarks>
    public Task ResetScaffoldAsync()
    {
        Scaffold = new Scaffold(Notebook, ExtensionHost, NotebookPath);
        Scaffold.InitializeSubsystems();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Scaffold.DisposeAsync();
    }
}
