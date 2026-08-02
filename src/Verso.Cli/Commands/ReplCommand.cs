using System.CommandLine;
using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Repl;
using Verso.Cli.Repl.Prompt;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Repl.Settings;
using Verso.Cli.Repl.Signals;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the <c>verso repl</c> command, an interactive Read-Eval-Print Loop
/// hosted in the existing Verso CLI.
/// </summary>
/// <remarks>
/// Exit code mapping:
/// <list type="bullet">
///   <item><description>0 — clean exit (<c>.exit</c>, Ctrl+D, cancel with confirmation).</description></item>
///   <item><description>1 — fatal error outside a cell (extension load failure).</description></item>
///   <item><description>3 — notebook file does not exist.</description></item>
///   <item><description>4 — notebook file could not be deserialized.</description></item>
///   <item><description>6 — <c>--kernel</c> or <c>--theme</c> resolution failed.
///     (Code 5 is reserved for <see cref="ExitCodes.MissingParameters"/>; REPL
///     resolution failures use <see cref="ExitCodes.ResolutionFailure"/>.)</description></item>
/// </list>
/// </remarks>
public static class ReplCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo?>("notebook", Strings.Repl_ArgNotebook)
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var kernelOption = new Option<string?>("--kernel", Strings.Repl_OptKernel);

        var executeOption = new Option<bool>(new[] { "--execute", "-x" }, () => false, Strings.Repl_OptExecute);

        var themeOption = new Option<string?>("--theme", Strings.Repl_OptTheme);

        var layoutOption = new Option<string?>("--layout", Strings.Repl_OptLayout);

        var extensionsOption = new Option<DirectoryInfo?>("--extensions", Strings.Option_Extensions);

        var noColorOption = new Option<bool>("--no-color", () => false, Strings.Repl_OptNoColor);

        var plainOption = new Option<bool>("--plain", () => false, Strings.Repl_OptPlain);

        var historyOption = new Option<string?>("--history", Strings.Repl_OptHistory);

        var listKernelsOption = new Option<bool>("--list-kernels", () => false, Strings.Repl_OptListKernels);

        var listThemesOption = new Option<bool>("--list-themes", () => false, Strings.Repl_OptListThemes);

        var preserveFormatOption = new Option<bool>("--preserve-format", () => false, Strings.Repl_OptPreserveFormat);

        var pythonOption = PythonInterpreterOption.Create();

        var command = new Command("repl", Strings.Repl_Description)
        {
            notebookArg,
            kernelOption,
            executeOption,
            themeOption,
            layoutOption,
            extensionsOption,
            noColorOption,
            plainOption,
            historyOption,
            listKernelsOption,
            listThemesOption,
            preserveFormatOption,
            pythonOption
        };

        command.SetHandler(async (context) =>
        {
            var notebookFile = context.ParseResult.GetValueForArgument(notebookArg);
            var kernelId = context.ParseResult.GetValueForOption(kernelOption);
            var execute = context.ParseResult.GetValueForOption(executeOption);
            var themeName = context.ParseResult.GetValueForOption(themeOption);
            var layoutId = context.ParseResult.GetValueForOption(layoutOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);
            var noColor = context.ParseResult.GetValueForOption(noColorOption);
            var plain = context.ParseResult.GetValueForOption(plainOption);
            var historyArg = context.ParseResult.GetValueForOption(historyOption);
            var listKernels = context.ParseResult.GetValueForOption(listKernelsOption);
            var listThemes = context.ParseResult.GetValueForOption(listThemesOption);
            var preserveFormat = context.ParseResult.GetValueForOption(preserveFormatOption);

            PythonInterpreterOption.Apply(context.ParseResult.GetValueForOption(pythonOption));

            // The REPL has no consent dialog, and a prompt that nothing can answer would be
            // approved by default. #!pip is the route to installing something from here.
            PythonAutoInstallOption.Disable();

            var ct = context.GetCancellationToken();

            var options = new ReplOptions
            {
                NotebookPath = notebookFile?.FullName,
                KernelId = kernelId,
                ThemeName = themeName,
                LayoutId = layoutId,
                ExtensionsDirectory = extensions?.FullName,
                Execute = execute,
                NoColor = noColor,
                Plain = plain,
                HistoryPath = string.Equals(historyArg, "none", StringComparison.OrdinalIgnoreCase) ? null : historyArg,
                HistoryDisabled = string.Equals(historyArg, "none", StringComparison.OrdinalIgnoreCase),
                PreserveFormat = preserveFormat
            };

            context.ExitCode = await RunAsync(options, listKernels, listThemes, ct);
        });

        return command;
    }

    internal static async Task<int> RunAsync(ReplOptions options, bool listKernels, bool listThemes, CancellationToken ct)
    {
        ExtensionHost? extensionHost = null;
        ReplSession? session = null;

        try
        {
            extensionHost = new ExtensionHost();
            extensionHost.ConsentHandler = (_, _) => Task.FromResult(true);
            await extensionHost.LoadBuiltInExtensionsAsync();

            if (options.ExtensionsDirectory is not null)
                await extensionHost.LoadFromDirectoryAsync(options.ExtensionsDirectory);

            if (listKernels)
            {
                PrintKernels(extensionHost);
                return ExitCodes.Success;
            }

            if (listThemes)
            {
                PrintThemes(extensionHost);
                return ExitCodes.Success;
            }

            // Load or create the session notebook.
            NotebookModel notebook;
            string? notebookPath = null;
            if (!string.IsNullOrEmpty(options.NotebookPath))
            {
                var fullPath = Path.GetFullPath(options.NotebookPath);
                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_NotebookNotFound, fullPath)));
                    return ExitCodes.FileNotFound;
                }

                INotebookSerializer serializer;
                try
                {
                    serializer = SerializerResolver.Resolve(extensionHost, fullPath);
                }
                catch (SerializerNotFoundException ex)
                {
                    Console.Error.WriteLine(Messages.Error(ex.Message));
                    return ExitCodes.SerializationError;
                }

                var content = await File.ReadAllTextAsync(fullPath, ct);
                try
                {
                    notebook = await serializer.DeserializeAsync(content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_DeserializeFailed, fullPath, ex.Message)));
                    return ExitCodes.SerializationError;
                }

                notebookPath = fullPath;
            }
            else
            {
                notebook = new NotebookModel
                {
                    Title = string.Format(
                        Strings.Repl_ScratchTitle, DateTimeOffset.Now.ToString("yyyy-MM-ddTHH-mm-ss"))
                };
            }

            // Resolve kernel override.
            if (!string.IsNullOrEmpty(options.KernelId))
            {
                if (!TryResolveKernel(extensionHost, options.KernelId, out var resolvedKernelId, out var kernelError))
                {
                    Console.Error.WriteLine(kernelError);
                    return ExitCodes.ResolutionFailure;
                }
                notebook.DefaultKernelId = resolvedKernelId;
            }
            else if (string.IsNullOrEmpty(notebook.DefaultKernelId))
            {
                // Pick the first C#-capable kernel as the default.
                var fallback = extensionHost.GetKernels()
                    .FirstOrDefault(k => string.Equals(k.LanguageId, "csharp", StringComparison.OrdinalIgnoreCase))
                    ?? extensionHost.GetKernels().FirstOrDefault();
                notebook.DefaultKernelId = fallback?.LanguageId;
            }

            // Resolve theme.
            ITheme? activeTheme = null;
            if (!string.IsNullOrEmpty(options.ThemeName))
            {
                if (!TryResolveTheme(extensionHost, options.ThemeName, out activeTheme, out var themeError))
                {
                    Console.Error.WriteLine(themeError);
                    return ExitCodes.ResolutionFailure;
                }
            }

            // Build Scaffold.
            var scaffold = new Scaffold(notebook, extensionHost, notebookPath);
            scaffold.InitializeSubsystems();

            // Hand each extension the settings the notebook saved for it. Without this the
            // values are written on save and never read back.
            if (scaffold.SettingsManager is { } replSettings)
                await replSettings.RestoreSettingsAsync(notebook);

            session = new ReplSession(notebook, scaffold, extensionHost, notebookPath)
            {
                ActiveKernelId = notebook.DefaultKernelId,
                ActiveTheme = activeTheme,
                ActiveLayoutId = options.LayoutId,
                Settings = ReplSettingsLoader.Load(),
                PreserveFormat = options.PreserveFormat
            };

            // Execute pre-loaded cells when --execute is set.
            if (options.Execute && notebook.Cells.Count > 0)
            {
                await scaffold.ExecuteAllAsync(ct);
            }

            // Configure console & renderer.
            var useColor = ResolveUseColor(options);
            var console = BuildConsole(useColor);
            var renderer = new TerminalRenderer(console, useColor);
            renderer.BindSettings(session.Settings);

            // Select prompt driver. PrettyPrompt only runs on a real TTY; redirected
            // stdin or TERM=dumb falls through to the line-oriented plain driver.
            IReplPrompt prompt;
            if (!options.Plain && TerminalCapabilities.SupportsPrettyPrompt())
            {
                var historyPath = HistoryStore.Resolve(options.HistoryPath, options.HistoryDisabled);
                prompt = new PrettyPromptDriver(session, historyPath, useColor);
            }
            else
            {
                prompt = new PlainPromptDriver();
            }

            // Render existing cells that already have outputs (from load, or from --execute).
            if (notebook.Cells.Count > 0)
            {
                for (int i = 0; i < notebook.Cells.Count; i++)
                {
                    session.NextInputCounter();
                    var cell = notebook.Cells[i];
                    var result = cell.LastStatus switch
                    {
                        "Success" => ExecutionResult.Success(cell.Id, cell.ExecutionCount ?? 0, cell.LastElapsed ?? TimeSpan.Zero),
                        "Failed" => ExecutionResult.Failed(cell.Id, cell.ExecutionCount ?? 0, cell.LastElapsed ?? TimeSpan.Zero, new Exception(Strings.Repl_PriorExecutionFailed)),
                        "Cancelled" => ExecutionResult.Cancelled(cell.Id, cell.ExecutionCount ?? 0, cell.LastElapsed ?? TimeSpan.Zero),
                        _ => ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero)
                    };
                    if (cell.Outputs.Count > 0)
                        renderer.RenderCell(session.InputCounter, cell, result, TimeSpan.FromMilliseconds(200));
                }
            }

            // Print the session header banner.
            PrintHeader(console, useColor, session, extensionHost);

            var elapsedThreshold = TimeSpan.FromMilliseconds(session.Settings.Preview.ElapsedThresholdMs);
            using var signals = new SignalHandler(ct);
            var loop = new ReplLoop(session, prompt, renderer, console, useColor, elapsedThreshold);
            var loopExit = await loop.RunAsync(signals.Token);
            return signals.ExitRequested ?? loopExit;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Messages.Fatal(ex.Message));
            return ExitCodes.CellFailure;
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
            else if (extensionHost is not null)
                await extensionHost.DisposeAsync();
        }
    }

    private static bool ResolveUseColor(ReplOptions options)
    {
        if (options.NoColor) return false;
        if (Console.IsOutputRedirected) return false;
        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
        return true;
    }

    private static IAnsiConsole BuildConsole(bool useColor)
    {
        if (useColor) return AnsiConsole.Console;
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors
        });
    }

    private static void PrintHeader(IAnsiConsole console, bool useColor, ReplSession session, ExtensionHost extensionHost)
    {
        var kernel = session.ActiveKernelId ?? Strings.Repl_MarkerNone;
        var theme = session.ActiveTheme?.DisplayName ?? Strings.Repl_MarkerDefault;
        var notebookLine = session.NotebookPath is not null ? session.NotebookPath : Strings.Repl_MarkerScratch;
        var extensions = string.Format(Strings.Repl_HeaderExtensionCount, extensionCountOf(extensionHost));
        var hint = Messages.Typed(Strings.Repl_HeaderHint, HelpCommand, ExitCommand);

        // Read into locals and measured here, because a translated label is not the length the
        // English one was and the four of them are meant to line up.
        var labels = new[]
        {
            Strings.Repl_HeaderKernel, Strings.Repl_HeaderTheme,
            Strings.Repl_HeaderNotebook, Strings.Repl_HeaderExtensions,
        };
        var labelWidth = labels.Max(DisplayWidth.Measure);

        if (useColor)
        {
            var banner = new FigletText("Verso REPL")
                .LeftJustified()
                .Color(Color.Cyan1);
            console.Write(banner);
            console.WriteLine();

            var values = new[] { kernel, theme, notebookLine, extensions };
            var panel = new Panel(string.Join("\n", labels.Zip(values, (label, value) =>
                $"[bold]{Markup.Escape(label)}[/] {Markup.Escape(value)}")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: Color.Grey39)
            };
            console.Write(panel);
            console.MarkupLine(Messages.In("dim", hint));
            console.WriteLine();
        }
        else
        {
            console.WriteLine("Verso REPL");
            foreach (var (label, value) in labels.Zip(new[] { kernel, theme, notebookLine, extensions }))
                console.WriteLine($"  {DisplayWidth.PadRight(label, labelWidth)} {value}");
            console.WriteLine();
            console.MarkupLine(hint);
            console.WriteLine();
        }

        static int extensionCountOf(ExtensionHost host) => host.GetExtensionInfos().Count;
    }

    /// <summary>What the reader types to see the REPL's commands. The same in every language.</summary>
    private const string HelpCommand = ".help";

    /// <summary>What the reader types to leave the REPL. The same in every language.</summary>
    private const string ExitCommand = ".exit";

    /// <summary>What to type to see the kernels that are installed. The same in every language.</summary>
    private const string ListKernelsCommand = "verso repl --list-kernels";

    /// <summary>What to type to see the themes that are installed. The same in every language.</summary>
    private const string ListThemesCommand = "verso repl --list-themes";

    private static bool TryResolveKernel(ExtensionHost extensionHost, string value, out string kernelLanguageId, out string error)
    {
        var kernels = extensionHost.GetKernels();
        var match = kernels.FirstOrDefault(k => string.Equals(k.LanguageId, value, StringComparison.OrdinalIgnoreCase))
                    ?? kernels.FirstOrDefault(k => string.Equals(k.ExtensionId, value, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            kernelLanguageId = "";
            var known = string.Join(", ", kernels.Select(k => k.LanguageId));
            error = Messages.Error(string.Format(Strings.Error_KernelNotRegistered, value))
                    + (known.Length > 0 ? " " + string.Format(Strings.Error_AvailableKernels, known) : "")
                    + " " + string.Format(Strings.Hint_RunForDetails, ListKernelsCommand);
            return false;
        }

        kernelLanguageId = match.LanguageId;
        error = "";
        return true;
    }

    private static bool TryResolveTheme(ExtensionHost extensionHost, string value, out ITheme theme, out string error)
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
            error = Messages.Error(string.Format(Strings.Error_MultipleThemes, value, ids));
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

        theme = null!;
        var known = string.Join(", ", themes.Select(t => t.DisplayName));
        error = Messages.Error(string.Format(Strings.Error_ThemeNotRegistered, value))
                + (known.Length > 0 ? " " + string.Format(Strings.Error_AvailableThemes, known) : "")
                + " " + string.Format(Strings.Hint_RunForDetails, ListThemesCommand);
        return false;
    }

    private static void PrintKernels(ExtensionHost extensionHost)
    {
        var kernels = extensionHost.GetKernels()
            .OrderBy(k => k.LanguageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (kernels.Count == 0)
        {
            Console.WriteLine(Strings.List_NoKernels);
            return;
        }
        // Read once into locals: a column is padded to the width of its own heading, and the
        // heading is not the length it was in English.
        var languageHeading = Strings.Table_Language;
        var displayNameHeading = Strings.Table_DisplayName;
        var idWidth = Math.Max(DisplayWidth.Measure(languageHeading), kernels.Max(k => DisplayWidth.Measure(k.LanguageId)));
        var nameWidth = Math.Max(DisplayWidth.Measure(displayNameHeading), kernels.Max(k => DisplayWidth.Measure(k.DisplayName)));
        Console.WriteLine($"{DisplayWidth.PadRight(languageHeading, idWidth)}  {DisplayWidth.PadRight(displayNameHeading, nameWidth)}  {Strings.Table_Description}");
        foreach (var kernel in kernels)
        {
            var description = kernel.Description ?? string.Empty;
            Console.WriteLine($"{DisplayWidth.PadRight(kernel.LanguageId, idWidth)}  {DisplayWidth.PadRight(kernel.DisplayName, nameWidth)}  {description}");
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
}
