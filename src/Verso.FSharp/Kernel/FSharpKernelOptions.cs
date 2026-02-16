namespace Verso.FSharp.Kernel;

/// <summary>
/// Configuration for the F# Interactive kernel session.
/// </summary>
internal sealed record FSharpKernelOptions
{
    /// <summary>
    /// Default FSI command-line arguments used to initialize the evaluation session.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultArgs = new[]
    {
        "fsi.exe",
        "--noninteractive",
        "--nologo",
        "--gui-",
        "--targetprofile:netcore",
        "--simpleresolution"
    };

    /// <summary>
    /// Assembly names that should be referenced by default when creating the FSI session.
    /// These are resolved from <c>TRUSTED_PLATFORM_ASSEMBLIES</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAssemblyNames = new[]
    {
        "FSharp.Core",
        "System.Runtime",
        "System.Console",
        "System.Collections",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "System.IO",
        "System.Net.Http",
        "System.Text.Json",
        "Microsoft.CSharp",
        "mscorlib",
        "netstandard"
    };

    /// <summary>
    /// Namespaces opened by default at session initialization.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultOpenNamespaces = new[]
    {
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Threading.Tasks"
    };

    /// <summary>
    /// Custom FSI arguments. When <c>null</c>, <see cref="DefaultArgs"/> is used.
    /// </summary>
    public IReadOnlyList<string>? FsiArgs { get; init; }

    /// <summary>
    /// Additional assembly names to reference beyond <see cref="DefaultAssemblyNames"/>.
    /// </summary>
    public IReadOnlyList<string>? AdditionalAssemblyNames { get; init; }

    /// <summary>
    /// Namespaces to open by default. When <c>null</c>, <see cref="DefaultOpenNamespaces"/> is used.
    /// </summary>
    public IReadOnlyList<string>? DefaultOpens { get; init; }

    /// <summary>
    /// F# compiler warning level (0–5). Default: 3.
    /// </summary>
    public int WarningLevel { get; init; } = 3;

    /// <summary>
    /// F# language version for the session. Default: <c>"preview"</c>.
    /// </summary>
    public string LangVersion { get; init; } = "preview";

    /// <summary>
    /// Whether to publish underscore-prefixed bindings to the variable store. Default: false.
    /// </summary>
    public bool PublishPrivateBindings { get; init; } = false;

    /// <summary>
    /// Maximum number of collection elements to display in formatted output. Default: 100.
    /// </summary>
    public int MaxCollectionDisplay { get; init; } = 100;
}
