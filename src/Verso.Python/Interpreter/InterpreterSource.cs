namespace Verso.Python.Interpreter;

/// <summary>
/// Identifies where a resolved Python interpreter came from, in the order the resolver consults them.
/// Surfaced to the user by the interpreter listing so they can see why a given interpreter was chosen.
/// </summary>
internal enum InterpreterSource
{
    /// <summary>No source assigned yet; the resolver overwrites this once an interpreter wins a tier.</summary>
    Unknown = 0,

    /// <summary>Explicitly configured executable (highest precedence).</summary>
    Explicit,

    /// <summary>Set through the <c>VERSO_PYTHON</c> environment variable.</summary>
    EnvironmentOverride,

    /// <summary>Chosen for the current session with the <c>#!python</c> command.</summary>
    SessionSelection,

    /// <summary>The active virtual environment (<c>VIRTUAL_ENV</c>).</summary>
    VirtualEnv,

    /// <summary>The active conda environment (<c>CONDA_PREFIX</c>).</summary>
    CondaPrefix,

    /// <summary>A <c>.venv</c> or <c>venv</c> directory found near the notebook.</summary>
    WorkspaceVenv,

    /// <summary>Discovered on the executable search path.</summary>
    Path,

    /// <summary>Found in a well-known installation location.</summary>
    WellKnown,

    /// <summary>A Verso-managed environment derived from an externally managed base interpreter.</summary>
    DerivedEnvironment,
}
