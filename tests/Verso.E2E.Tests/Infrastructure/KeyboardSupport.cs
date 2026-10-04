using System.Runtime.InteropServices;

namespace Verso.E2E.Tests.Infrastructure;

/// <summary>
/// Known limits of the keyboard input these tests drive the browser with.
/// </summary>
public static class KeyboardSupport
{
    /// <summary>
    /// Marks the current test inconclusive where key presses cannot be trusted. On macOS, Motus
    /// sends each key's Windows key code as the native one, which Chrome reads as a different
    /// Mac key that is never released; it repeats thousands of times a second until the browser
    /// stops answering. CI runs these tests on Linux.
    /// </summary>
    public static void RequireReliableKeyPresses()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Inconclusive(
                "Motus key presses are unreliable on macOS: https://github.com/DataficationSDK/Motus/issues/2");
        }
    }
}
