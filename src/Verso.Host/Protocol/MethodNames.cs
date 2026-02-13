namespace Verso.Host.Protocol;

public static class MethodNames
{
    // Host lifecycle
    public const string HostReady = "host/ready";
    public const string HostShutdown = "host/shutdown";

    // Notebook operations
    public const string NotebookOpen = "notebook/open";
    public const string NotebookSave = "notebook/save";
    public const string NotebookGetLanguages = "notebook/getLanguages";
    public const string NotebookGetToolbarActions = "notebook/getToolbarActions";
    public const string NotebookGetTheme = "notebook/getTheme";

    // Cell operations
    public const string CellAdd = "cell/add";
    public const string CellInsert = "cell/insert";
    public const string CellRemove = "cell/remove";
    public const string CellMove = "cell/move";
    public const string CellUpdateSource = "cell/updateSource";
    public const string CellGet = "cell/get";
    public const string CellList = "cell/list";

    // Execution
    public const string ExecutionRun = "execution/run";
    public const string ExecutionRunAll = "execution/runAll";
    public const string ExecutionCancel = "execution/cancel";
    public const string CellExecutionState = "cell/executionState";

    // Kernel
    public const string KernelRestart = "kernel/restart";
    public const string KernelGetCompletions = "kernel/getCompletions";
    public const string KernelGetDiagnostics = "kernel/getDiagnostics";
    public const string KernelGetHoverInfo = "kernel/getHoverInfo";

    // Output
    public const string OutputClearAll = "output/clearAll";
}
