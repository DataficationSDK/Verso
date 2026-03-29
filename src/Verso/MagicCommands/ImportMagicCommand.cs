using Verso.Abstractions;
using Verso.Extensions;
using Verso.Parameters;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!import path [--param name=value ...]</c> — reads a notebook file, deserializes it, resolves
/// parameters against the imported notebook's definitions, and executes all code cells in the current
/// kernel session. Variables and state persist for subsequent cells.
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
    public string Description => "Imports and executes all code cells from another notebook file, with optional parameter overrides.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("path", "Path to the notebook file to import.", typeof(string), IsRequired: true),
        new ParameterDefinition("--param", "Parameter override in name=value format. May be repeated.", typeof(string), IsRequired: false)
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

        var (path, paramOverrides) = ParseArguments(arguments);

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

            if (serializer is null)
            {
                await context.WriteOutputAsync(new CellOutput("text/plain",
                    $"Error: No serializer found for '{Path.GetFileName(resolvedPath)}'. Supported formats: .verso, .ipynb",
                    IsError: true)).ConfigureAwait(false);
                return;
            }

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

            // Resolve parameters: explicit --param overrides first (with type
            // coercion and validation), then fill remaining defaults from the
            // imported notebook's definitions. Explicit overrides always win.
            var paramError = ResolveAndInjectParameters(
                notebook, paramOverrides, context.Variables);

            if (paramError is not null)
            {
                await context.WriteOutputAsync(new CellOutput("text/plain",
                    paramError, IsError: true)).ConfigureAwait(false);
                return;
            }

            var codeCellCount = 0;
            foreach (var cell in notebook.Cells)
            {
                if (!string.Equals(cell.Type, "code", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(cell.Source))
                    continue;

                await context.Notebook.ExecuteCodeAsync(cell.Source, cell.Language, context.CancellationToken)
                    .ConfigureAwait(false);
                codeCellCount++;
            }

            await context.WriteOutputAsync(new CellOutput("text/plain",
                $"Imported {codeCellCount} code cell{(codeCellCount == 1 ? "" : "s")} from {Path.GetFileName(resolvedPath)}"))
                .ConfigureAwait(false);
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
    /// Parses the arguments string into a file path and optional --param overrides.
    /// </summary>
    internal static (string Path, Dictionary<string, string> Params) ParseArguments(string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = parts[0];
        var paramOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i] is "--param" or "-p" && i + 1 < parts.Length)
            {
                var pair = parts[++i];
                var eq = pair.IndexOf('=');
                if (eq > 0)
                    paramOverrides[pair[..eq]] = pair[(eq + 1)..];
            }
        }

        return (path, paramOverrides);
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
