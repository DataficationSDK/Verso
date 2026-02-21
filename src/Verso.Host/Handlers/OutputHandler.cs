namespace Verso.Host.Handlers;

public static class OutputHandler
{
    public static object HandleClearAll(NotebookSession ns)
    {
        ns.Scaffold.ClearAllOutputs();
        return new { success = true };
    }
}
