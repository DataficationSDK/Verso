using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Verso.Cli.Utilities;

/// <summary>
/// Opens a URL in the user's default browser. Best-effort; failures are silently ignored.
/// </summary>
public static class BrowserLauncher
{
    public static void Open(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch
        {
            // Browser launch is best-effort; swallow all exceptions.
        }
    }
}
