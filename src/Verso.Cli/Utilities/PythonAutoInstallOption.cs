using System.CommandLine;
using Verso.Cli.Resources;

namespace Verso.Cli.Utilities;

/// <summary>
/// The shared <c>--auto-install</c> option. A Python cell importing a package the environment
/// does not have normally prompts, which needs somebody to answer; a command that runs without a
/// user turns the behavior off entirely unless this option asks for it.
/// </summary>
public static class PythonAutoInstallOption
{
    /// <summary>Environment variable the Python kernel reads for its install policy.</summary>
    private const string PolicyVariable = "VERSO_PYTHON_AUTO_INSTALL";

    public static Option<bool> Create() => new("--auto-install", Strings.Option_AutoInstall);

    /// <summary>
    /// Publish the policy to the current process so the kernel picks it up when it starts. The
    /// environment is the seam the kernel already reads, which keeps this working the same way
    /// for every command without threading it through kernel construction.
    /// </summary>
    /// <param name="autoInstall">Whether the caller opted in.</param>
    public static void Apply(bool autoInstall)
    {
        // Off rather than the usual prompt default: nothing here can present a consent dialog,
        // and a prompt nobody answers would either block or approve silently.
        Environment.SetEnvironmentVariable(PolicyVariable, autoInstall ? "auto" : "off");
    }

    /// <summary>
    /// Turn installs off for a command that has no way to ask and no option to opt in.
    /// </summary>
    public static void Disable() => Apply(autoInstall: false);
}
