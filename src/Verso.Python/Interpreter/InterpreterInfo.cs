namespace Verso.Python.Interpreter;

/// <summary>
/// A validated Python interpreter: the executable to run and the facts the validation probe reported.
/// <see cref="Source"/> is filled in by the resolver once the interpreter wins a precedence tier.
/// </summary>
internal sealed record InterpreterInfo(
    string Executable,
    Version Version,
    string RawVersion,
    string Implementation,
    bool IsExternallyManaged,
    InterpreterSource Source = InterpreterSource.Unknown)
{
    /// <summary>A short human-readable description of the environment this interpreter represents.</summary>
    public string EnvironmentKind => Source switch
    {
        InterpreterSource.VirtualEnv => "virtual environment",
        InterpreterSource.WorkspaceVenv => "workspace virtual environment",
        InterpreterSource.CondaPrefix => "conda environment",
        InterpreterSource.DerivedEnvironment => "managed environment",
        InterpreterSource.SessionSelection => "session selection",
        InterpreterSource.Explicit => "configured interpreter",
        InterpreterSource.EnvironmentOverride => "environment override",
        _ => "system interpreter",
    };
}
