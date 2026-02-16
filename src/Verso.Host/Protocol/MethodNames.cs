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
    public const string NotebookSetFilePath = "notebook/setFilePath";

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

    // Layout
    public const string LayoutGetLayouts = "layout/getLayouts";
    public const string LayoutSwitch = "layout/switch";
    public const string LayoutRender = "layout/render";
    public const string LayoutUpdateCell = "layout/updateCell";
    public const string LayoutSetEditMode = "layout/setEditMode";

    // Theme
    public const string ThemeGetThemes = "theme/getThemes";
    public const string ThemeSwitch = "theme/switch";

    // Extension management
    public const string ExtensionList = "extension/list";
    public const string ExtensionEnable = "extension/enable";
    public const string ExtensionDisable = "extension/disable";

    // Settings
    public const string SettingsGetDefinitions = "settings/getDefinitions";
    public const string SettingsGet = "settings/get";
    public const string SettingsUpdate = "settings/update";
    public const string SettingsReset = "settings/reset";
    public const string SettingsChanged = "settings/changed";

    // Variable explorer
    public const string VariableList = "variable/list";
    public const string VariableInspect = "variable/inspect";
}
