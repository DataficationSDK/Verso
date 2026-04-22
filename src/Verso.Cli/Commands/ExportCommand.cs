using System.CommandLine;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Utilities;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso export' command: dispatches a notebook to an
/// <see cref="IToolbarAction"/> registered with <see cref="ToolbarPlacement.ExportMenu"/>
/// and writes the produced bytes to disk. The name matches the
/// <c>Export</c> toolbar placement surfaced by the editor UI and VS Code
/// extension.
/// </summary>
public static class ExportCommand
{
    public static Command Create()
    {
        var inputArg = new Argument<FileInfo?>("input", "Path to the source notebook file.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var formatOption = new Option<string?>(new[] { "--format", "-f" },
            "Export format, matched against the DisplayName of a registered IToolbarAction whose placement is ExportMenu. Case-insensitive. Quote values containing whitespace. Use --list to see installed formats.");

        var outputOption = new Option<FileInfo?>(new[] { "--output", "-o" },
            "Output file path. If omitted, the exporter's suggested filename is written to the current directory.");

        var executeOption = new Option<bool>(new[] { "--execute", "-x" }, () => false,
            "Execute the notebook before exporting so stored outputs are refreshed.");

        var layoutOption = new Option<string?>("--layout",
            "Layout id to apply during export, exposed as ActiveLayoutId on the action context.");

        var themeOption = new Option<string?>("--theme",
            "DisplayName of a registered theme, matched case-insensitively. Quote values with whitespace. ThemeId is accepted as a fallback to disambiguate display-name collisions. Use --list-themes to see installed themes.");

        var extensionsOption = new Option<DirectoryInfo?>("--extensions",
            "Directory to scan for additional extension assemblies.");

        var listOption = new Option<bool>("--list", () => false,
            "List registered export actions (DisplayName, ActionId, Description) and exit.");

        var listThemesOption = new Option<bool>("--list-themes", () => false,
            "List registered themes (DisplayName, Kind, Description) and exit.");

        var command = new Command("export", "Export a notebook via an ExportMenu toolbar action.")
        {
            inputArg,
            formatOption,
            outputOption,
            executeOption,
            layoutOption,
            themeOption,
            extensionsOption,
            listOption,
            listThemesOption
        };

        command.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArg);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var execute = context.ParseResult.GetValueForOption(executeOption);
            var layout = context.ParseResult.GetValueForOption(layoutOption);
            var themeText = context.ParseResult.GetValueForOption(themeOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);
            var list = context.ParseResult.GetValueForOption(listOption);
            var listThemes = context.ParseResult.GetValueForOption(listThemesOption);

            ExtensionHost? extensionHost = null;
            Scaffold? scaffold = null;
            try
            {
                extensionHost = new ExtensionHost();
                extensionHost.ConsentHandler = (_, _) => Task.FromResult(true);
                await extensionHost.LoadBuiltInExtensionsAsync();

                if (extensions is not null)
                    await extensionHost.LoadFromDirectoryAsync(extensions.FullName);

                if (list)
                {
                    PrintExportActions(extensionHost);
                    context.ExitCode = ExitCodes.Success;
                    return;
                }

                if (listThemes)
                {
                    PrintThemes(extensionHost);
                    context.ExitCode = ExitCodes.Success;
                    return;
                }

                if (input is null)
                {
                    Console.Error.WriteLine("Error: <input> is required unless --list is specified.");
                    context.ExitCode = ExitCodes.FileNotFound;
                    return;
                }

                if (string.IsNullOrWhiteSpace(format))
                {
                    Console.Error.WriteLine("Error: --format is required. Use 'verso export --list' to see available formats.");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                var inputPath = Path.GetFullPath(input.FullName);
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                    context.ExitCode = ExitCodes.FileNotFound;
                    return;
                }

                INotebookSerializer serializer;
                try
                {
                    serializer = SerializerResolver.Resolve(extensionHost, inputPath);
                }
                catch (SerializerNotFoundException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                var content = await File.ReadAllTextAsync(inputPath);
                NotebookModel notebook;
                try
                {
                    notebook = await serializer.DeserializeAsync(content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Failed to deserialize '{inputPath}': {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                if (execute)
                {
                    scaffold = new Scaffold(notebook, extensionHost, inputPath);
                    scaffold.InitializeSubsystems();
                    var results = await scaffold.ExecuteAllAsync(context.GetCancellationToken());

                    if (HasAnyFailure(notebook, results))
                    {
                        Console.Error.WriteLine("Error: Notebook execution reported errors. Aborting export.");
                        context.ExitCode = ExitCodes.CellFailure;
                        return;
                    }
                }

                if (!TryResolveAction(extensionHost, format, out var action, out var resolveError))
                {
                    Console.Error.WriteLine(resolveError);
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                ITheme? selectedTheme = null;
                if (!string.IsNullOrWhiteSpace(themeText))
                {
                    if (!TryResolveTheme(extensionHost, themeText, out selectedTheme, out var themeError))
                    {
                        Console.Error.WriteLine(themeError);
                        context.ExitCode = ExitCodes.SerializationError;
                        return;
                    }
                }

                var metadata = new NotebookMetadataContext(notebook, inputPath);
                var outputPath = output?.FullName;

                var ctx = new CliToolbarActionContext(
                    notebook.Cells,
                    metadata,
                    extensionHost,
                    selectedTheme,
                    layout,
                    outputPath,
                    context.GetCancellationToken());

                await action.ExecuteAsync(ctx);

                if (ctx.WrittenPath is null)
                {
                    Console.Error.WriteLine($"Error: Export action '{action.ActionId}' did not produce a file.");
                    context.ExitCode = ExitCodes.CellFailure;
                    return;
                }

                Console.WriteLine($"Exported '{inputPath}' -> '{ctx.WrittenPath}'");
                context.ExitCode = ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = ExitCodes.CellFailure;
            }
            finally
            {
                // Scaffold.DisposeAsync disposes the ExtensionHost it was handed,
                // which would clear the toolbar action registry if called before
                // the export action runs. Defer disposal until after export completes.
                if (scaffold is not null)
                    await scaffold.DisposeAsync();
                else if (extensionHost is not null)
                    await extensionHost.DisposeAsync();
            }
        });

        return command;
    }

    private static void PrintExportActions(ExtensionHost extensionHost)
    {
        var actions = extensionHost.GetToolbarActions()
            .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
            .OrderBy(a => a.Order)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (actions.Count == 0)
        {
            Console.WriteLine("No export actions are registered.");
            return;
        }

        var nameWidth = Math.Max("FORMAT".Length, actions.Max(a => a.DisplayName.Length));

        Console.WriteLine($"{"FORMAT".PadRight(nameWidth)}  DESCRIPTION");
        foreach (var action in actions)
        {
            var description = action.Description ?? string.Empty;
            Console.WriteLine($"{action.DisplayName.PadRight(nameWidth)}  {description}");
        }
    }

    private static void PrintThemes(ExtensionHost extensionHost)
    {
        var themes = extensionHost.GetThemes()
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (themes.Count == 0)
        {
            Console.WriteLine("No themes are registered.");
            return;
        }

        var nameWidth = Math.Max("THEME".Length, themes.Max(t => t.DisplayName.Length));
        var kindWidth = Math.Max("KIND".Length, themes.Max(t => t.ThemeKind.ToString().Length));

        Console.WriteLine($"{"THEME".PadRight(nameWidth)}  {"KIND".PadRight(kindWidth)}  DESCRIPTION");
        foreach (var theme in themes)
        {
            var description = theme.Description ?? string.Empty;
            Console.WriteLine($"{theme.DisplayName.PadRight(nameWidth)}  {theme.ThemeKind.ToString().PadRight(kindWidth)}  {description}");
        }
    }

    private static bool TryResolveTheme(
        ExtensionHost extensionHost,
        string value,
        out ITheme theme,
        out string error)
    {
        var themes = extensionHost.GetThemes();

        var byName = themes
            .Where(t => string.Equals(t.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
        {
            theme = byName[0];
            error = "";
            return true;
        }

        if (byName.Count > 1)
        {
            var exactById = byName.FirstOrDefault(t =>
                string.Equals(t.ThemeId, value, StringComparison.OrdinalIgnoreCase));
            if (exactById is not null)
            {
                theme = exactById;
                error = "";
                return true;
            }

            var ids = string.Join(", ", byName.Select(t => t.ThemeId));
            theme = null!;
            error = $"Error: Multiple themes share display name '{value}'. Disambiguate by ThemeId: {ids}.";
            return false;
        }

        var byId = themes.FirstOrDefault(t =>
            string.Equals(t.ThemeId, value, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            theme = byId;
            error = "";
            return true;
        }

        var known = string.Join(", ", themes.Select(t => t.DisplayName));
        theme = null!;
        error = $"Error: Theme '{value}' is not registered." +
            (known.Length > 0 ? $" Available themes: {known}." : "") +
            " Run 'verso export --list-themes' for details.";
        return false;
    }

    private static bool TryResolveAction(
        ExtensionHost extensionHost,
        string format,
        out IToolbarAction action,
        out string error)
    {
        var exportActions = extensionHost.GetToolbarActions()
            .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
            .ToList();

        var byName = exportActions
            .Where(a => string.Equals(a.DisplayName, format, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
        {
            action = byName[0];
            error = "";
            return true;
        }

        if (byName.Count > 1)
        {
            var exactById = byName.FirstOrDefault(a => a.ActionId == format);
            if (exactById is not null)
            {
                action = exactById;
                error = "";
                return true;
            }

            var ids = string.Join(", ", byName.Select(a => a.ActionId));
            action = null!;
            error = $"Error: Multiple export actions share display name '{format}'. Disambiguate by ActionId: {ids}.";
            return false;
        }

        var byId = exportActions.FirstOrDefault(a => a.ActionId == format);
        if (byId is not null)
        {
            action = byId;
            error = "";
            return true;
        }

        action = null!;
        error = $"Error: Export format '{format}' is not registered. Run 'verso export --list' to see available formats.";
        return false;
    }

    private static bool HasAnyFailure(NotebookModel notebook, IReadOnlyList<ExecutionResult> results)
    {
        if (results.Any(r => r.Status == ExecutionResult.ExecutionStatus.Failed))
            return true;

        foreach (var result in results)
        {
            var cell = notebook.Cells.FirstOrDefault(c => c.Id == result.CellId);
            if (cell?.Outputs.Any(o => o.IsError) == true)
                return true;
        }

        return false;
    }
}
