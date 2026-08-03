using System.CommandLine;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Resources;
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
        var inputArg = new Argument<FileInfo?>("input", Strings.Arg_InputNotebook)
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var formatOption = new Option<string?>(new[] { "--format", "-f" }, Strings.Export_OptFormat);

        var outputOption = new Option<FileInfo?>(new[] { "--output", "-o" }, Strings.Export_OptOutput);

        var executeOption = new Option<bool>(new[] { "--execute", "-x" }, () => false, Strings.Export_OptExecute);

        var layoutOption = new Option<string?>("--layout", Strings.Export_OptLayout);

        var themeOption = new Option<string?>("--theme", Strings.Export_OptTheme);

        var extensionsOption = new Option<DirectoryInfo?>("--extensions", Strings.Option_Extensions);

        var listOption = new Option<bool>("--list", () => false, Strings.Export_OptList);

        var listThemesOption = new Option<bool>("--list-themes", () => false, Strings.Export_OptListThemes);

        var command = new Command("export", Strings.Export_Description)
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

            // Export approves consent requests outright so it can load the extensions a notebook
            // declares. That must not extend to installing packages, so the policy is turned off
            // rather than left to a prompt that would be answered yes by the handler below.
            PythonAutoInstallOption.Disable();

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
                    Console.Error.WriteLine(Messages.Error(Strings.Export_InputRequired));
                    context.ExitCode = ExitCodes.FileNotFound;
                    return;
                }

                if (string.IsNullOrWhiteSpace(format))
                {
                    Console.Error.WriteLine(Messages.Error(Strings.Export_FormatRequired) + " "
                        + string.Format(Strings.Hint_RunForFormats, ListFormatsCommand));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                var inputPath = Path.GetFullPath(input.FullName);
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_InputNotFound, inputPath)));
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
                    Console.Error.WriteLine(Messages.Error(ex.Message));
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
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_DeserializeFailed, inputPath, ex.Message)));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                if (execute)
                {
                    scaffold = new Scaffold(notebook, extensionHost, inputPath);
                    scaffold.InitializeSubsystems();

                    // Hand each extension the settings the notebook saved for it, so an export
                    // that runs the notebook runs it configured the way it was saved.
                    if (scaffold.SettingsManager is { } exportSettings)
                        await exportSettings.RestoreSettingsAsync(notebook);

                    var results = await scaffold.ExecuteAllAsync(context.GetCancellationToken());

                    if (HasAnyFailure(notebook, results))
                    {
                        Console.Error.WriteLine(Messages.Error(Strings.Export_ExecutionErrors));
                        context.ExitCode = ExitCodes.CellFailure;
                        return;
                    }
                }

                if (!ToolbarActionResolver.TryResolveAction(extensionHost, format, out var action, out var resolveError))
                {
                    Console.Error.WriteLine(resolveError + " "
                        + string.Format(Strings.Hint_RunForFormats, ListFormatsCommand));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                ITheme? selectedTheme = null;
                if (!string.IsNullOrWhiteSpace(themeText))
                {
                    if (!ToolbarActionResolver.TryResolveTheme(extensionHost, themeText, out selectedTheme, out var themeError))
                    {
                        Console.Error.WriteLine(themeError + " "
                            + string.Format(Strings.Hint_RunForDetails, ListThemesCommand));
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
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Export_NoFileProduced, action.ActionId)));
                    context.ExitCode = ExitCodes.CellFailure;
                    return;
                }

                Console.WriteLine(string.Format(Strings.Export_Done, inputPath, ctx.WrittenPath));
                context.ExitCode = ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Messages.Error(ex.Message));
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

    /// <summary>What to type to see the formats that are installed. The same in every language.</summary>
    private const string ListFormatsCommand = "verso export --list";

    /// <summary>What to type to see the themes that are installed. The same in every language.</summary>
    private const string ListThemesCommand = "verso export --list-themes";

    private static void PrintExportActions(ExtensionHost extensionHost)
    {
        var actions = extensionHost.GetToolbarActions()
            .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
            .OrderBy(a => a.Order)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (actions.Count == 0)
        {
            Console.WriteLine(Strings.Export_NoActions);
            return;
        }

        // Read once into a local: a column is padded to the width of its own heading, and the
        // heading is not the length it was in English.
        var formatHeading = Strings.Table_Format;
        var nameWidth = Math.Max(DisplayWidth.Measure(formatHeading), actions.Max(a => DisplayWidth.Measure(a.DisplayName)));

        Console.WriteLine($"{DisplayWidth.PadRight(formatHeading, nameWidth)}  {Strings.Table_Description}");
        foreach (var action in actions)
        {
            var description = action.Description ?? string.Empty;
            Console.WriteLine($"{DisplayWidth.PadRight(action.DisplayName, nameWidth)}  {description}");
        }
    }

    private static void PrintThemes(ExtensionHost extensionHost)
    {
        var themes = extensionHost.GetThemes()
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (themes.Count == 0)
        {
            Console.WriteLine(Strings.List_NoThemes);
            return;
        }

        var themeHeading = Strings.Table_Theme;
        var kindHeading = Strings.Table_Kind;
        var nameWidth = Math.Max(DisplayWidth.Measure(themeHeading), themes.Max(t => DisplayWidth.Measure(t.DisplayName)));
        var kindWidth = Math.Max(DisplayWidth.Measure(kindHeading), themes.Max(t => t.ThemeKind.ToString().Length));

        Console.WriteLine($"{DisplayWidth.PadRight(themeHeading, nameWidth)}  {DisplayWidth.PadRight(kindHeading, kindWidth)}  {Strings.Table_Description}");
        foreach (var theme in themes)
        {
            var description = theme.Description ?? string.Empty;
            Console.WriteLine($"{DisplayWidth.PadRight(theme.DisplayName, nameWidth)}  {theme.ThemeKind.ToString().PadRight(kindWidth)}  {description}");
        }
    }

    private static bool HasAnyFailure(NotebookModel notebook, IReadOnlyList<ExecutionResult> results)
        => CellOutcome.AnyFailed(notebook.Cells, results);
}
