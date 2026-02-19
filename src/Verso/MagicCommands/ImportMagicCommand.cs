using Verso.Abstractions;

namespace Verso.MagicCommands;

/// <summary>
/// <c>#!import path</c> — reads a notebook file, deserializes it, and executes all code cells
/// in the current kernel session. Variables and state persist for subsequent cells.
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
    public string Description => "Imports and executes all code cells from another notebook file.";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("path", "Path to the notebook file to import.", typeof(string), IsRequired: true)
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            await context.WriteOutputAsync(new CellOutput("text/plain",
                "Error: #!import requires a file path. Usage: #!import <path>", IsError: true))
                .ConfigureAwait(false);
            return;
        }

        var path = arguments.Trim();

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
