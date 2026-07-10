using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Parameters;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!import path [--param name=value ...] [--show-output]</c> -- reads a notebook file or source file,
/// deserializes or parses it, resolves parameters against the imported notebook's definitions, and executes
/// every cell whose type carries source (any type other than <c>markdown</c> or <c>raw</c>), dispatching to
/// the kernel that handles each cell's language. Variables and state persist for subsequent cells.
/// <para>
/// By default the imported cells run silently, except that error outputs are always surfaced.
/// Pass <c>--show-output</c> to surface all output each imported cell produces in the importing
/// cell, in execution order.
/// </para>
/// <para>
/// Supported formats:
/// <list type="bullet">
/// <item>Notebook files (<c>.verso</c>, <c>.ipynb</c>, <c>.dib</c>) -- deserialized and executed cell-by-cell.</item>
/// <item>Source files (<c>.cs</c>, <c>.csx</c>, <c>.fs</c>, <c>.fsx</c>, <c>.py</c>, <c>.ps1</c>, <c>.psm1</c>,
/// <c>.js</c>, <c>.mjs</c>, <c>.ts</c>, <c>.tsx</c>, <c>.sql</c>, etc.) -- magic commands are extracted,
/// sorted by priority (NuGet/pip first, then imports, then remaining directives), and the file is executed
/// in the kernel that owns the file extension.</item>
/// </list>
/// </para>
/// </summary>
[VersoExtension]
public sealed class ImportMagicCommand : IMagicCommand
{
    // --- IExtension ---

    public string ExtensionId => "verso.magic.import";
    string IExtension.Name => "Import Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "import";
    public string Description => "Imports another notebook file and executes its cells (any cell type other than markdown or raw), with optional parameter overrides. Pass --show-output to display the imported cells' output.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("path", "Path to the notebook file to import.", typeof(string), IsRequired: true),
        new ParameterDefinition("--param", "Parameter override in name=value format. May be repeated.", typeof(string), IsRequired: false),
        new ParameterDefinition("--show-output", "Display the output produced by the imported cells. Off by default.", typeof(bool), IsRequired: false)
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                "Error: #!import requires a file path. Usage: #!import <path> [--param name=value ...]",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        var (path, paramOverrides, showOutput) = ParseArguments(arguments);

        try
        {
            var resolvedPath = ResolvePath(path, context.NotebookMetadata.FilePath);

            if (!File.Exists(resolvedPath))
            {
                await context.WriteOutputAsync(new CellOutput("text/plain",
                    $"Error: File not found: {resolvedPath}", IsError: true))
                    .ConfigureAwait(false);
                return;
            }

            var serializer = context.ExtensionHost.GetSerializers()
                .FirstOrDefault(s => s.CanImport(resolvedPath));

            if (serializer is not null)
            {
                await ImportNotebookAsync(resolvedPath, serializer, paramOverrides, showOutput, context)
                    .ConfigureAwait(false);
                return;
            }

            // No serializer -- try to match the file extension to a registered kernel.
            var extension = Path.GetExtension(resolvedPath);
            var kernel = FindKernelByFileExtension(extension, context.ExtensionHost);

            if (kernel is not null)
            {
                await ImportSourceFileAsync(resolvedPath, kernel, showOutput, context).ConfigureAwait(false);
                return;
            }

            var supportedExtensions = GetSupportedExtensions(context.ExtensionHost);
            await context.WriteOutputAsync(CellOutput.Error(
                $"No serializer or kernel found for '{Path.GetFileName(resolvedPath)}'. " +
                $"Supported formats: {supportedExtensions}")).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                $"Error importing notebook: {ex.Message}", IsError: true))
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports a notebook file by deserializing it and executing all code cells.
    /// </summary>
    private async Task ImportNotebookAsync(
        string resolvedPath,
        INotebookSerializer serializer,
        Dictionary<string, string> paramOverrides,
        bool showOutput,
        IMagicCommandContext context)
    {
        var content = await File.ReadAllTextAsync(resolvedPath, context.CancellationToken)
            .ConfigureAwait(false);
        var notebook = await serializer.DeserializeAsync(content).ConfigureAwait(false);

        // Pre-scan for #!extension directives and request consent
        var directives = ExtensionMagicCommand.ScanForExtensionDirectives(notebook);
        if (directives.Count > 0)
        {
            var importedDirectives = directives
                .Select(e => new ExtensionConsentInfo(e.PackageId, e.Version,
                    $"imported from {Path.GetFileName(resolvedPath)}"))
                .ToList();

            var approved = await context.ExtensionHost.RequestExtensionConsentAsync(
                importedDirectives, context.CancellationToken).ConfigureAwait(false);

            if (approved && context.ExtensionHost is ExtensionHost host)
            {
                foreach (var d in directives)
                    host.ApprovePackage(d.PackageId);
            }
        }

        var paramError = ResolveAndInjectParameters(notebook, paramOverrides, context.Variables);

        if (paramError is not null)
        {
            await context.WriteOutputAsync(CellOutput.Error(paramError)).ConfigureAwait(false);
            return;
        }

        var executedCount = 0;
        var failedCount = 0;
        foreach (var cell in notebook.Cells)
        {
            if (!IsExecutableCellType(cell.Type))
                continue;
            if (string.IsNullOrWhiteSpace(cell.Source))
                continue;

            // Always capture outputs so failures inside the imported notebook are visible.
            // Without --show-output only error outputs are surfaced; successful cells stay silent.
            var outputs = await context.Notebook.ExecuteCodeCaptureOutputsAsync(
                cell.Source, cell.Language, context.CancellationToken).ConfigureAwait(false);

            var failed = false;
            foreach (var output in outputs)
            {
                failed |= output.IsError;
                if (showOutput || output.IsError)
                    await context.WriteOutputAsync(output).ConfigureAwait(false);
            }

            if (failed)
                failedCount++;
            executedCount++;
        }

        var summary = $"Imported {executedCount} cell{(executedCount == 1 ? "" : "s")} from {Path.GetFileName(resolvedPath)}";
        if (failedCount > 0)
            summary += $" ({failedCount} failed)";

        await context.WriteOutputAsync(new CellOutput("text/plain", summary, IsError: failedCount > 0))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <c>true</c> for cell types that carry executable source. Markdown and raw cells
    /// hold documentation only and are skipped during import. Every other type (including
    /// kernel-specific types like "sql", "python", or custom extension types) is dispatched
    /// through <see cref="INotebookOperations.ExecuteCodeAsync"/>, which routes by language.
    /// </summary>
    internal static bool IsExecutableCellType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        if (string.Equals(type, "markdown", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(type, "raw", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// Imports a source file by extracting magic commands, sorting them by priority
    /// (package directives first, then imports, then other directives), and executing the
    /// reassembled content in the kernel that owns the file extension.
    /// </summary>
    private async Task ImportSourceFileAsync(
        string resolvedPath,
        ILanguageKernel kernel,
        bool showOutput,
        IMagicCommandContext context)
    {
        var content = await File.ReadAllTextAsync(resolvedPath, context.CancellationToken)
            .ConfigureAwait(false);

        var (magicLines, codeLines) = ExtractMagicCommands(content);

        // Sort magic commands by execution priority:
        // 1. Package directives (#r "nuget:...", #!nuget, #!pip, #!npm)
        // 2. Import directives (#!import)
        // 3. All other magic commands
        var packageLines = new List<string>();
        var importLines = new List<string>();
        var otherMagicLines = new List<string>();

        foreach (var line in magicLines)
        {
            if (IsPackageDirective(line))
                packageLines.Add(line);
            else if (line.StartsWith("#!import", StringComparison.OrdinalIgnoreCase))
                importLines.Add(line);
            else
                otherMagicLines.Add(line);
        }

        // Reassemble: magic commands (priority-ordered) followed by remaining code
        var reassembled = new List<string>();
        reassembled.AddRange(packageLines);
        reassembled.AddRange(importLines);
        reassembled.AddRange(otherMagicLines);
        reassembled.AddRange(codeLines);

        var code = string.Join(Environment.NewLine, reassembled);
        var failed = false;

        if (!string.IsNullOrWhiteSpace(code))
        {
            // Always capture outputs so failures are visible. Without --show-output only
            // error outputs are surfaced; successful execution stays silent.
            var outputs = await context.Notebook.ExecuteCodeCaptureOutputsAsync(
                code, kernel.LanguageId, context.CancellationToken).ConfigureAwait(false);
            foreach (var output in outputs)
            {
                failed |= output.IsError;
                if (showOutput || output.IsError)
                    await context.WriteOutputAsync(output).ConfigureAwait(false);
            }
        }

        var fileName = Path.GetFileName(resolvedPath);
        var magicCount = magicLines.Count;
        var summary = magicCount > 0
            ? $"Imported {fileName} ({magicCount} directive{(magicCount == 1 ? "" : "s")} extracted)"
            : $"Imported {fileName}";
        if (failed)
            summary += " (execution failed)";

        await context.WriteOutputAsync(new CellOutput("text/plain", summary, IsError: failed))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a registered kernel whose <see cref="ILanguageKernel.FileExtensions"/> includes the given extension.
    /// </summary>
    internal static ILanguageKernel? FindKernelByFileExtension(string extension, IExtensionHostContext extensionHost)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        return extensionHost.GetKernels()
            .FirstOrDefault(k => k.FileExtensions
                .Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Extracts magic command lines from source file content. Magic commands are lines starting
    /// with <c>#!</c> or <c>#r "nuget:...</c> (NuGet reference directives). All other lines are
    /// returned as code.
    /// </summary>
    internal static (List<string> MagicLines, List<string> CodeLines) ExtractMagicCommands(string content)
    {
        var magicLines = new List<string>();
        var codeLines = new List<string>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').TrimStart();

            if (trimmed.StartsWith("#!", StringComparison.Ordinal))
            {
                magicLines.Add(trimmed);
            }
            else if (NuGetReferencePattern.IsMatch(trimmed))
            {
                // #r "nuget: PackageName, Version" -- treat as a magic command
                magicLines.Add(trimmed);
            }
            else
            {
                codeLines.Add(line.TrimEnd('\r'));
            }
        }

        return (magicLines, codeLines);
    }

    private static readonly Regex NuGetReferencePattern = new(
        @"^#r\s+""nuget:\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns <c>true</c> if the line is a package installation directive
    /// (NuGet, pip, or npm).
    /// </summary>
    private static bool IsPackageDirective(string line)
    {
        if (NuGetReferencePattern.IsMatch(line))
            return true;
        if (line.StartsWith("#!nuget", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.StartsWith("#!pip", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.StartsWith("#!npm", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Builds a human-readable list of supported file extensions from serializers and kernels.
    /// </summary>
    private static string GetSupportedExtensions(IExtensionHostContext extensionHost)
    {
        var extensions = new List<string>();

        foreach (var s in extensionHost.GetSerializers())
            extensions.AddRange(s.FileExtensions);

        foreach (var k in extensionHost.GetKernels())
            extensions.AddRange(k.FileExtensions);

        return string.Join(", ", extensions.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e));
    }

    /// <summary>
    /// Parses the arguments string into a file path, optional --param overrides, and the
    /// --show-output flag. The path is the first token that is not a recognized flag, so the
    /// flag may appear before or after the path.
    /// </summary>
    internal static (string Path, Dictionary<string, string> Params, bool ShowOutput) ParseArguments(string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? path = null;
        var paramOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var showOutput = false;

        for (var i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], "--show-output", StringComparison.OrdinalIgnoreCase))
            {
                showOutput = true;
            }
            else if (parts[i] is "--param" or "-p" && i + 1 < parts.Length)
            {
                var pair = parts[++i];
                var eq = pair.IndexOf('=');
                if (eq > 0)
                    paramOverrides[pair[..eq]] = pair[(eq + 1)..];
            }
            else
            {
                path ??= parts[i];
            }
        }

        return (path ?? "", paramOverrides, showOutput);
    }

    /// <summary>
    /// Resolves --param overrides and imported notebook defaults, validates required
    /// parameters, and merges everything into the variable store. Returns an error
    /// message if required parameters are missing, or null on success.
    /// </summary>
    internal static string? ResolveAndInjectParameters(
        NotebookModel notebook,
        Dictionary<string, string> paramOverrides,
        IVariableStore variables)
    {
        var definitions = notebook.Parameters;

        // If the imported notebook has no parameter definitions, inject any
        // overrides as untyped strings and return.
        if (definitions is not { Count: > 0 })
        {
            foreach (var (name, value) in paramOverrides)
                variables.Set(name, value);
            return null;
        }

        // 1. Apply explicit --param overrides with type coercion.
        foreach (var (name, raw) in paramOverrides)
        {
            if (definitions.TryGetValue(name, out var def))
            {
                if (ParameterValueParser.TryParse(def.Type, raw, out var typed, out var error) && typed is not null)
                    variables.Set(name, typed);
                else
                    return $"Error: Invalid value for parameter '{name}' ({def.Type}): {error}";
            }
            else
            {
                // Unknown parameter -- inject as string (matches CLI behavior).
                variables.Set(name, raw);
            }
        }

        // 2. Fill defaults for parameters not already in the store.
        foreach (var (name, def) in definitions)
        {
            if (variables.TryGet<object>(name, out var existing) && existing is not null)
                continue;

            if (def.Default is null)
                continue;

            var value = def.Default;
            if (value is string str && def.Type is not "string")
            {
                if (ParameterValueParser.TryParse(def.Type, str, out var parsed, out _) && parsed is not null)
                    value = parsed;
            }

            variables.Set(name, value);
        }

        // 3. Validate required parameters.
        var missing = new List<string>();
        foreach (var (name, def) in definitions)
        {
            if (!def.Required) continue;
            if (variables.TryGet<object>(name, out var val) && val is not null) continue;
            missing.Add($"  {name} ({def.Type}){(def.Description is not null ? " -- " + def.Description : "")}");
        }

        if (missing.Count > 0)
            return $"Error: Missing required parameter{(missing.Count > 1 ? "s" : "")} " +
                   $"for imported notebook:\n{string.Join("\n", missing)}";

        return null;
    }

    /// <summary>
    /// Resolves a file path relative to the notebook's directory, or the current working directory
    /// if no notebook path is available.
    /// </summary>
    internal static string ResolvePath(string path, string? notebookFilePath)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var baseDir = !string.IsNullOrEmpty(notebookFilePath)
            ? Path.GetDirectoryName(notebookFilePath)
            : null;

        baseDir ??= Directory.GetCurrentDirectory();

        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
