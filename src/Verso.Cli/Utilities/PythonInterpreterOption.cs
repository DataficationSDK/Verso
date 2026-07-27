using System.CommandLine;

namespace Verso.Cli.Utilities;

/// <summary>
/// The shared <c>--python</c> option. Python cells otherwise discover an interpreter from the
/// active virtual environment, the workspace, and well-known install locations; this pins one
/// explicitly for the run.
/// </summary>
public static class PythonInterpreterOption
{
    /// <summary>Environment variable the Python kernel consults as its highest-precedence choice.</summary>
    private const string InterpreterVariable = "VERSO_PYTHON";

    public static Option<string?> Create() => new(
        "--python",
        "Path to the Python interpreter used by Python cells. Overrides automatic discovery.");

    /// <summary>
    /// Publish the selection to the current process so the kernel picks it up when it starts.
    /// The environment is the seam the interpreter search already reads, which keeps the option
    /// working the same way for every host without threading it through kernel construction.
    /// </summary>
    public static void Apply(string? interpreterPath)
    {
        if (!string.IsNullOrWhiteSpace(interpreterPath))
            Environment.SetEnvironmentVariable(InterpreterVariable, interpreterPath!.Trim());
    }
}
