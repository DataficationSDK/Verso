using Microsoft.FSharp.Core;
using static FSharp.Compiler.Interactive.Shell;

namespace Verso.FSharp.Kernel;

/// <summary>
/// Wraps <see cref="FsiEvaluationSession"/> and contains all FCS interop.
/// Callers see only .NET types -- no F# compiler types leak out.
/// </summary>
internal sealed class FsiSessionManager : IDisposable
{
    private FsiEvaluationSession? _session;
    private StringWriter? _fsiOut;
    private StringWriter? _fsiErr;
    private bool _disposed;

    /// <summary>
    /// Structured result from an FSI evaluation.
    /// </summary>
    internal sealed record EvalResult(
        string? FsiOutput,
        string? FsiError,
        string? ConsoleOutput,
        string? ConsoleError,
        bool HasCompilationErrors,
        string? CompilationErrorText,
        object? ResultValue);

    /// <summary>
    /// Creates and initializes the FSI evaluation session.
    /// </summary>
    public void Initialize(FSharpKernelOptions options)
    {
        if (_session is not null)
            return;

        _fsiOut = new StringWriter();
        _fsiErr = new StringWriter();

        var fsiConfig = FsiEvaluationSession.GetDefaultConfiguration();

        // Build base args: start from custom or default, then append options-driven flags
        var baseArgs = (options.FsiArgs ?? FSharpKernelOptions.DefaultArgs).ToList();

        // Apply WarningLevel and LangVersion from options
        baseArgs.Add($"--warn:{options.WarningLevel}");
        baseArgs.Add($"--langversion:{options.LangVersion}");

        // Build assembly references from TRUSTED_PLATFORM_ASSEMBLIES
        var assemblyNames = new HashSet<string>(
            FSharpKernelOptions.DefaultAssemblyNames,
            StringComparer.OrdinalIgnoreCase);

        if (options.AdditionalAssemblyNames is not null)
        {
            foreach (var name in options.AdditionalAssemblyNames)
                assemblyNames.Add(name);
        }

        var additionalArgs = new List<string>(baseArgs);
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedPlatformAssemblies is not null)
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var path in trustedPlatformAssemblies.Split(separator))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (assemblyNames.Contains(fileName))
                {
                    additionalArgs.Add($"-r:{path}");
                }
            }
        }

        _session = FsiEvaluationSession.Create(
            fsiConfig,
            additionalArgs.ToArray(),
            new StringReader(""),
            _fsiOut,
            _fsiErr,
            FSharpOption<bool>.Some(false),
            null);
    }

    /// <summary>
    /// Evaluates an F# interaction and returns a structured result.
    /// Console.Out and Console.Error are captured during evaluation.
    /// </summary>
    public EvalResult EvalInteraction(string code, CancellationToken ct)
    {
        EnsureSession();

        // Reset FSI output buffers
        _fsiOut!.GetStringBuilder().Clear();
        _fsiErr!.GetStringBuilder().Clear();

        // Capture Console.Out and Console.Error
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var consoleOutWriter = new StringWriter();
        var consoleErrWriter = new StringWriter();

        try
        {
            Console.SetOut(consoleOutWriter);
            Console.SetError(consoleErrWriter);

            ct.ThrowIfCancellationRequested();

            var result = _session!.EvalInteractionNonThrowing(code, ct);

            // result is (FSharpChoice<FSharpOption<FsiValue>, exn>, FSharpDiagnostic[])
            var choice = result.Item1;
            var diagnostics = result.Item2;

            // Check for errors in diagnostics
            var errors = diagnostics
                .Where(d => d.Severity.IsError)
                .ToArray();

            if (errors.Length > 0)
            {
                var errorText = string.Join(
                    Environment.NewLine,
                    errors.Select(d => d.ToString()));

                return new EvalResult(
                    FsiOutput: GetOutput(_fsiOut),
                    FsiError: GetOutput(_fsiErr),
                    ConsoleOutput: GetOutput(consoleOutWriter),
                    ConsoleError: GetOutput(consoleErrWriter),
                    HasCompilationErrors: true,
                    CompilationErrorText: errorText,
                    ResultValue: null);
            }

            // Extract value from FSharpChoice (Choice1Of2 = success, Choice2Of2 = exception)
            object? resultValue = null;
            if (choice.IsChoice1Of2)
            {
                // Use reflection to safely extract the FsiValue from the Choice
                var itemProp = choice.GetType().GetProperty("Item");
                var fsiValueOption = itemProp?.GetValue(choice);
                if (fsiValueOption is not null)
                {
                    // fsiValueOption is FSharpOption<FsiValue> — extract .Value.ReflectionValue
                    var valueProp = fsiValueOption.GetType().GetProperty("Value");
                    var fsiValue = valueProp?.GetValue(fsiValueOption);
                    if (fsiValue is not null)
                    {
                        var reflProp = fsiValue.GetType().GetProperty("ReflectionValue");
                        resultValue = reflProp?.GetValue(fsiValue);
                    }
                }
            }
            else if (choice.IsChoice2Of2)
            {
                // Runtime exception during evaluation
                var itemProp = choice.GetType().GetProperty("Item");
                var exception = itemProp?.GetValue(choice) as Exception;
                return new EvalResult(
                    FsiOutput: GetOutput(_fsiOut),
                    FsiError: GetOutput(_fsiErr),
                    ConsoleOutput: GetOutput(consoleOutWriter),
                    ConsoleError: GetOutput(consoleErrWriter),
                    HasCompilationErrors: false,
                    CompilationErrorText: null,
                    ResultValue: exception);
            }

            return new EvalResult(
                FsiOutput: GetOutput(_fsiOut),
                FsiError: GetOutput(_fsiErr),
                ConsoleOutput: GetOutput(consoleOutWriter),
                ConsoleError: GetOutput(consoleErrWriter),
                HasCompilationErrors: false,
                CompilationErrorText: null,
                ResultValue: resultValue);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    /// <summary>
    /// Returns all bound values in the current FSI session.
    /// </summary>
    public List<(string Name, object Value, Type Type)> GetBoundValues()
    {
        EnsureSession();

        var results = new List<(string, object, Type)>();
        foreach (var bv in _session!.GetBoundValues())
        {
            var value = bv.Value.ReflectionValue;
            if (value is not null)
            {
                results.Add((bv.Name, value, value.GetType()));
            }
        }
        return results;
    }

    /// <summary>
    /// Injects a CLR object as a bound value into the FSI session.
    /// </summary>
    public void AddBoundValue(string name, object value)
    {
        EnsureSession();
        _session!.AddBoundValue(name, value);
    }

    /// <summary>
    /// Evaluates F# code silently (no output captured) for injecting helper modules.
    /// </summary>
    public void EvalSilent(string code)
    {
        EnsureSession();

        _fsiOut!.GetStringBuilder().Clear();
        _fsiErr!.GetStringBuilder().Clear();

        _session!.EvalInteractionNonThrowing(code, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fsiOut?.Dispose();
        _fsiErr?.Dispose();
        _session = null;
    }

    private void EnsureSession()
    {
        if (_session is null)
            throw new InvalidOperationException("FSI session has not been initialized. Call Initialize first.");
        if (_disposed)
            throw new ObjectDisposedException(nameof(FsiSessionManager));
    }

    private static string? GetOutput(StringWriter writer)
    {
        var text = writer.ToString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
