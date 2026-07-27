using Verso.Python.Interpreter;

namespace Verso.Python.Tests.Interpreter;

/// <summary>
/// Test double for <see cref="IInterpreterValidator"/> that maps candidate paths to fixed results,
/// so precedence and command tests never launch a Python process.
/// </summary>
internal sealed class FakeInterpreterValidator : IInterpreterValidator
{
    private readonly Dictionary<string, InterpreterValidation> _map = new(StringComparer.Ordinal);

    public List<string> ValidatedPaths { get; } = new();

    public FakeInterpreterValidator Valid(
        string path, string version = "3.13.3", string implementation = "CPython", bool managed = false)
    {
        _map[path] = InterpreterValidation.Valid(MakeInfo(path, version, implementation, managed));
        return this;
    }

    public FakeInterpreterValidator Alias(string path, string resolvedExecutable, string version = "3.13.3")
    {
        // A candidate that validates but reports a different resolved executable (used for dedup tests).
        _map[path] = InterpreterValidation.Valid(MakeInfo(resolvedExecutable, version, "CPython", false));
        return this;
    }

    public FakeInterpreterValidator Rejected(string path, string reason)
    {
        _map[path] = InterpreterValidation.Rejected(reason);
        return this;
    }

    public Task<InterpreterValidation> ValidateAsync(string executablePath, CancellationToken cancellationToken)
    {
        ValidatedPaths.Add(executablePath);
        return Task.FromResult(_map.TryGetValue(executablePath, out var validation)
            ? validation
            : InterpreterValidation.NotFound);
    }

    public static InterpreterInfo MakeInfo(
        string executable, string version = "3.13.3", string implementation = "CPython", bool managed = false)
    {
        InterpreterValidator.TryParseVersion(version, out var parsed);
        return new InterpreterInfo(executable, parsed, version, implementation, managed);
    }
}
