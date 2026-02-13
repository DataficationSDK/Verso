namespace Verso.Host.Handlers;

public static class OutputHandler
{
    public static object HandleClearAll(HostSession session)
    {
        session.EnsureSession();
        session.Scaffold!.ClearAllOutputs();
        return new { success = true };
    }
}
