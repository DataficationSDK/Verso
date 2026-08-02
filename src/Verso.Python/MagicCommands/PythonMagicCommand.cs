using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Resources;

namespace Verso.Python.MagicCommands;

/// <summary>
/// <c>#!python</c> inspects or selects the Python interpreter for the current session.
/// <list type="bullet">
/// <item><c>#!python</c> shows the active interpreter, its version, and environment kind.</item>
/// <item><c>#!python &lt;path-or-name&gt;</c> selects an interpreter for this session.</item>
/// <item><c>#!python --list</c> lists discovered interpreters and where each came from.</item>
/// </list>
/// The selection is held for the session only; a restart applies it. To pin an interpreter across
/// sessions, keep a <c>#!python &lt;path&gt;</c> line in a cell, or set the host interpreter setting.
/// </summary>
[VersoExtension]
public sealed class PythonMagicCommand : IMagicCommand
{
    /// <summary>Session variable key under which the selected interpreter path is held.</summary>
    internal const string InterpreterSelectionStoreKey = "__verso_python_interpreter";

    private readonly InterpreterLocator _locator;

    /// <summary>Creates the command with interpreter discovery backed by the real validator.</summary>
    public PythonMagicCommand() : this(new InterpreterLocator(new InterpreterValidator()))
    {
    }

    internal PythonMagicCommand(InterpreterLocator locator)
        => _locator = locator ?? throw new ArgumentNullException(nameof(locator));

    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.python";
    string IExtension.Name => Strings.Magic_Python_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "python";
    public string Description => Strings.Magic_Python_Description;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition(
            "interpreter",
            Strings.Magic_Python_Param_Target,
            typeof(string)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        // This command inspects or records a selection; it never consumes the cell's code.
        context.SuppressExecution = false;

        var argument = (arguments ?? string.Empty).Trim();
        var query = BuildQuery(context);

        if (argument.Length == 0)
        {
            await ShowStatusAsync(context, query).ConfigureAwait(false);
            return;
        }

        if (argument is "--list" or "-l")
        {
            await ListAsync(context, query).ConfigureAwait(false);
            return;
        }

        await SelectAsync(argument, context, query).ConfigureAwait(false);
    }

    private InterpreterQuery BuildQuery(IMagicCommandContext context)
    {
        string? notebookDirectory = null;
        var filePath = context.NotebookMetadata?.FilePath;
        if (!string.IsNullOrEmpty(filePath))
        {
            try { notebookDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath!)); }
            catch { /* leave the notebook directory unset */ }
        }

        string? sessionSelection = null;
        if (context.Variables.TryGet<string>(InterpreterSelectionStoreKey, out var stored) && !string.IsNullOrEmpty(stored))
            sessionSelection = stored;

        return new InterpreterQuery(
            SessionSelection: sessionSelection,
            NotebookDirectory: notebookDirectory,
            WorkspaceRoot: notebookDirectory);
    }

    private async Task ShowStatusAsync(IMagicCommandContext context, InterpreterQuery query)
    {
        var resolution = await _locator.ResolveAsync(query, context.CancellationToken).ConfigureAwait(false);
        if (!resolution.Found)
        {
            await context.WriteOutputAsync(CellOutput.Error(resolution.BuildNotFoundMessage())).ConfigureAwait(false);
            return;
        }

        await context.WriteOutputAsync(CellOutput.Plain(Describe(resolution.Interpreter!)))
            .ConfigureAwait(false);
    }

    private async Task ListAsync(IMagicCommandContext context, InterpreterQuery query)
    {
        var interpreters = await _locator.DiscoverAllAsync(query, context.CancellationToken).ConfigureAwait(false);
        if (interpreters.Count == 0)
        {
            await context.WriteOutputAsync(CellOutput.Plain(Strings.Magic_Python_NoneFound)).ConfigureAwait(false);
            return;
        }

        var lines = new List<string> { Strings.Magic_Python_DiscoveredHeading };
        foreach (var interpreter in interpreters)
            lines.Add($"  {interpreter.Executable}  -  Python {interpreter.RawVersion} ({SourceLabel(interpreter.Source)})");

        await context.WriteOutputAsync(CellOutput.Plain(string.Join("\n", lines))).ConfigureAwait(false);
    }

    private async Task SelectAsync(string request, IMagicCommandContext context, InterpreterQuery query)
    {
        var validation = await ResolveRequestedAsync(request, query, context.CancellationToken).ConfigureAwait(false);
        if (validation.Status != InterpreterValidationStatus.Valid)
        {
            var reason = validation.Status == InterpreterValidationStatus.Rejected
                ? validation.RejectionReason!
                : string.Format(Strings.Magic_Python_NotFoundFor, request);
            await context.WriteOutputAsync(CellOutput.Error(reason)).ConfigureAwait(false);
            return;
        }

        var interpreter = validation.Info!;
        context.Variables.Set(InterpreterSelectionStoreKey, interpreter.Executable);

        // The store is cleared as part of a restart, and the selection has to survive that, so the
        // kernel is told directly as well. This is also the only route when the cell holds nothing
        // but this command: execution never reaches the kernel in that case.
        var kernel = FindPythonKernel(context);
        kernel?.SetSessionInterpreter(interpreter.Executable);

        var message = string.Format(
            Strings.Magic_Python_Selected, interpreter.Executable, interpreter.RawVersion);
        message += kernel is null
            ? Strings.Magic_Python_SelectedAppliesLater
            : Strings.Magic_Python_SelectedRestarting;

        await context.WriteOutputAsync(CellOutput.Plain(message)).ConfigureAwait(false);

        if (kernel is not null)
        {
            try
            {
                await context.Notebook.RestartKernelAsync("python").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await context.WriteOutputAsync(CellOutput.Error(
                        string.Format(Strings.Magic_Python_RestartFailed, ex.Message)))
                    .ConfigureAwait(false);
            }
        }
    }

    private static PythonKernel? FindPythonKernel(IMagicCommandContext context)
    {
        try { return context.ExtensionHost?.GetKernels().OfType<PythonKernel>().FirstOrDefault(); }
        catch { return null; }
    }

    private async Task<InterpreterValidation> ResolveRequestedAsync(
        string request, InterpreterQuery query, CancellationToken cancellationToken)
    {
        if (File.Exists(request))
            return await _locator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

        // A bare version like "3.12" selects a matching discovered interpreter.
        if (request.Length > 0 && char.IsDigit(request[0]))
        {
            var interpreters = await _locator.DiscoverAllAsync(query, cancellationToken).ConfigureAwait(false);
            var match = interpreters.FirstOrDefault(i => i.RawVersion.StartsWith(request, StringComparison.Ordinal));
            return match is not null ? InterpreterValidation.Valid(match) : InterpreterValidation.NotFound;
        }

        // Otherwise treat it as a command name the interpreter can resolve itself (through PATH).
        return await _locator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string Describe(InterpreterInfo interpreter)
    {
        var text = string.Format(Strings.Magic_Python_Active, interpreter.Executable,
            interpreter.RawVersion, interpreter.Implementation, interpreter.EnvironmentKind);
        if (interpreter.IsExternallyManaged)
            text += Strings.Magic_Python_ExternallyManaged;
        return text;
    }

    private static string SourceLabel(InterpreterSource source) => source switch
    {
        InterpreterSource.Explicit => Strings.InterpreterSource_Explicit,
        // The name of an environment variable, so it reads the same in every language.
        InterpreterSource.EnvironmentOverride => "VERSO_PYTHON",
        InterpreterSource.SessionSelection => Strings.InterpreterSource_SessionSelection,
        InterpreterSource.VirtualEnv => Strings.InterpreterSource_VirtualEnv,
        InterpreterSource.CondaPrefix => Strings.InterpreterSource_CondaPrefix,
        InterpreterSource.WorkspaceVenv => Strings.InterpreterSource_WorkspaceVenv,
        InterpreterSource.Path => Strings.InterpreterSource_Path,
        InterpreterSource.WellKnown => Strings.InterpreterSource_WellKnown,
        InterpreterSource.DerivedEnvironment => Strings.InterpreterSource_DerivedEnvironment,
        _ => "unknown",
    };
}
