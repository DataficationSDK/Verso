using System.CommandLine;
using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Repl;
using Verso.Cli.Repl.Prompt;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Repl.Settings;
using Verso.Cli.Repl.Signals;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the <c>verso repl</c> command — an interactive Read-Eval-Print Loop
/// hosted in the existing Verso CLI. See
/// <c>agent-docs/specifications/Verso/Verso-Repl-Specification-v1.0.md</c>.
/// </summary>
/// <remarks>
/// Exit code mapping:
/// <list type="bullet">
///   <item><description>0 — clean exit (<c>.exit</c>, Ctrl+D, cancel with confirmation).</description></item>
///   <item><description>1 — fatal error outside a cell (extension load failure).</description></item>
///   <item><description>3 — notebook file does not exist.</description></item>
///   <item><description>4 — notebook file could not be deserialized.</description></item>
///   <item><description>6 — <c>--kernel</c> or <c>--theme</c> resolution failed.
///     (The spec assigns this to code 5 conceptually; the CLI reserves 5 for
///     <see cref="ExitCodes.MissingParameters"/> and uses <see cref="ExitCodes.ResolutionFailure"/>
///     for REPL resolution failures.)</description></item>
/// </list>
/// </remarks>
public static class ReplCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo?>("notebook",
            "Path to a .verso, .ipynb, or .dib file. When omitted, starts with an empty scratch notebook.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var kernelOption = new Option<string?>("--kernel",
            "Active kernel for the first cell. Matched against ILanguageKernel.KernelId case-insensitively. Can be changed at runtime with .kernel.");

        var executeOption = new Option<bool>(new[] { "--execute", "-x" }, () => false,
            "When combined with <notebook>, executes all loaded cells before handing control to the prompt.");

        var themeOption = new Option<string?>("--theme",
            "Active theme for output rendering. DisplayName case-insensitive with ThemeId fallback.");

        var layoutOption = new Option<string?>("--layout",
            "Default layout id, passed to .export as ActiveLayoutId unless the command overrides it.");

        var extensionsOption = new Option<DirectoryInfo?>("--extensions",
            "Additional directory to scan for extension assemblies.");

        var noColorOption = new Option<bool>("--no-color", () => false,
            "Disable ANSI styling. Output is plain UTF-8 text.");

        var plainOption = new Option<bool>("--plain", () => false,
            "Force the line-oriented fallback prompt, bypassing PrettyPrompt even when the terminal would support it.");

        var historyOption = new Option<string?>("--history",
            "Path to the prompt history file. Use 'none' to disable persistent history.");

        var listKernelsOption = new Option<bool>("--list-kernels", () => false,
            "Print available kernels and exit.");

        var listThemesOption = new Option<bool>("--list-themes", () => false,
            "Print registered themes and exit.");

        var command = new Command("repl", "Start an interactive Verso REPL in the terminal.")
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
            listThemesOption
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
                HistoryDisabled = string.Equals(historyArg, "none", StringComparison.OrdinalIgnoreCase)
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
                    Console.Error.WriteLine($"Error: Notebook file not found: {fullPath}");
                    return ExitCodes.FileNotFound;
                }

                INotebookSerializer serializer;
                try
                {
                    serializer = SerializerResolver.Resolve(extensionHost, fullPath);
                }
                catch (SerializerNotFoundException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return ExitCodes.SerializationError;
                }

                var content = await File.ReadAllTextAsync(fullPath, ct);
                try
                {
                    notebook = await serializer.DeserializeAsync(content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Failed to deserialize '{fullPath}': {ex.Message}");
                    return ExitCodes.SerializationError;
                }

                notebookPath = fullPath;
            }
            else
            {
                notebook = new NotebookModel
                {
                    Title = $"Verso REPL — {DateTimeOffset.Now:yyyy-MM-ddTHH-mm-ss}"
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
                // Pick the first C#-capable kernel (§4.1 default).
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

            session = new ReplSession(notebook, scaffold, extensionHost, notebookPath)
            {
                ActiveKernelId = notebook.DefaultKernelId,
                ActiveTheme = activeTheme,
                ActiveLayoutId = options.LayoutId,
                Settings = ReplSettingsLoader.Load()
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
                        "Failed" => ExecutionResult.Failed(cell.Id, cell.ExecutionCount ?? 0, cell.LastElapsed ?? TimeSpan.Zero, new Exception("Prior execution failed.")),
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
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
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
        var kernel = session.ActiveKernelId ?? "<none>";
        var theme = session.ActiveTheme?.DisplayName ?? "<default>";
        var notebookLine = session.NotebookPath is not null ? session.NotebookPath : "*scratch*";
        var extensionCount = extensionHost.GetExtensionInfos().Count;

        if (useColor)
        {
            var banner = new FigletText("Verso REPL")
                .LeftJustified()
                .Color(Color.Cyan1);
            console.Write(banner);
            console.WriteLine();

            var panel = new Panel(
                $"[bold]kernel:[/] {Markup.Escape(kernel)}\n" +
                $"[bold]theme:[/]  {Markup.Escape(theme)}\n" +
                $"[bold]notebook:[/] {Markup.Escape(notebookLine)}\n" +
                $"[bold]extensions:[/] {extensionCount} loaded")
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: Color.Grey39)
            };
            console.Write(panel);
            console.MarkupLine("[dim]Type [bold].help[/] for commands, [bold].exit[/] to quit.[/]");
            console.WriteLine();
        }
        else
        {
            console.WriteLine("Verso REPL");
            console.WriteLine($"  kernel:     {kernel}");
            console.WriteLine($"  theme:      {theme}");
            console.WriteLine($"  notebook:   {notebookLine}");
            console.WriteLine($"  extensions: {extensionCount} loaded");
            console.WriteLine();
            console.WriteLine("Type .help for commands, .exit to quit.");
            console.WriteLine();
        }
    }

    private static bool TryResolveKernel(ExtensionHost extensionHost, string value, out string kernelLanguageId, out string error)
    {
        var kernels = extensionHost.GetKernels();
        var match = kernels.FirstOrDefault(k => string.Equals(k.LanguageId, value, StringComparison.OrdinalIgnoreCase))
                    ?? kernels.FirstOrDefault(k => string.Equals(k.ExtensionId, value, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            kernelLanguageId = "";
            var known = string.Join(", ", kernels.Select(k => k.LanguageId));
            error = $"Error: Kernel '{value}' is not registered." +
                    (known.Length > 0 ? $" Available kernels: {known}." : "") +
                    " Run 'verso repl --list-kernels' for details.";
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

        theme = null!;
        var known = string.Join(", ", themes.Select(t => t.DisplayName));
        error = $"Error: Theme '{value}' is not registered." +
                (known.Length > 0 ? $" Available themes: {known}." : "") +
                " Run 'verso repl --list-themes' for details.";
        return false;
    }

    private static void PrintKernels(ExtensionHost extensionHost)
    {
        var kernels = extensionHost.GetKernels()
            .OrderBy(k => k.LanguageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (kernels.Count == 0)
        {
            Console.WriteLine("No kernels are registered.");
            return;
        }
        var idWidth = Math.Max("LANGUAGE".Length, kernels.Max(k => k.LanguageId.Length));
        var nameWidth = Math.Max("DISPLAY NAME".Length, kernels.Max(k => k.DisplayName.Length));
        Console.WriteLine($"{"LANGUAGE".PadRight(idWidth)}  {"DISPLAY NAME".PadRight(nameWidth)}  DESCRIPTION");
        foreach (var kernel in kernels)
        {
            var description = kernel.Description ?? string.Empty;
            Console.WriteLine($"{kernel.LanguageId.PadRight(idWidth)}  {kernel.DisplayName.PadRight(nameWidth)}  {description}");
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
}
