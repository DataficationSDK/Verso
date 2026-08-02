using System.CommandLine;
using Verso.Cli.Hosting;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso serve' command to launch the Verso Blazor application.
/// </summary>
public static class ServeCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo?>("notebook", () => null, Strings.Serve_ArgNotebook)
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var portOption = new Option<int>("--port", () => 5050, Strings.Serve_OptPort);

        var noBrowserOption = new Option<bool>("--no-browser", () => false, Strings.Serve_OptNoBrowser);

        var noHttpsOption = new Option<bool>("--no-https", () => false, Strings.Serve_OptNoHttps);

        var extensionsOption = new Option<DirectoryInfo?>("--extensions", Strings.Option_Extensions);

        var verboseOption = new Option<bool>("--verbose", () => false, Strings.Serve_OptVerbose);

        var preserveFormatOption = new Option<bool>("--preserve-format", () => false, Strings.Serve_OptPreserveFormat);

        var pythonOption = PythonInterpreterOption.Create();

        var command = new Command("serve", Strings.Serve_Description)
        {
            notebookArg,
            portOption,
            noBrowserOption,
            noHttpsOption,
            extensionsOption,
            verboseOption,
            preserveFormatOption,
            pythonOption
        };

        command.SetHandler(async (context) =>
        {
            var notebook = context.ParseResult.GetValueForArgument(notebookArg);
            var port = context.ParseResult.GetValueForOption(portOption);
            var noBrowser = context.ParseResult.GetValueForOption(noBrowserOption);
            var noHttps = context.ParseResult.GetValueForOption(noHttpsOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var preserveFormat = context.ParseResult.GetValueForOption(preserveFormatOption);
            var language = context.ParseResult.GetValueForOption(LanguageOption.Instance);

            PythonInterpreterOption.Apply(context.ParseResult.GetValueForOption(pythonOption));

            // Validate notebook path if provided
            string? notebookPath = null;
            if (notebook is not null)
            {
                notebookPath = Path.GetFullPath(notebook.FullName);
                if (!File.Exists(notebookPath))
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_NotebookNotFound, notebookPath)));
                    context.ExitCode = ExitCodes.FileNotFound;
                    return;
                }
            }

            try
            {
                var options = new ServeOptions
                {
                    Port = port,
                    NoHttps = noHttps,
                    Verbose = verbose,
                    ExtensionsDirectory = extensions?.FullName,
                    PreserveFormat = preserveFormat,
                    Language = language
                };

                var app = BlazorHostBuilder.Build(options);

                // Build the URL
                var baseUrl = $"http://localhost:{port}";
                var launchUrl = notebookPath is not null
                    ? $"{baseUrl}/?recover={Uri.EscapeDataString(notebookPath)}"
                    : baseUrl;

                Console.WriteLine(string.Format(Strings.Serve_Running, baseUrl));
                Console.WriteLine(Strings.Serve_PressCtrlC);

                if (verbose)
                {
                    if (!noHttps)
                        Console.Error.WriteLine($"  HTTPS: https://localhost:{port + 1}");
                    if (extensions is not null)
                        Console.Error.WriteLine("  " + string.Format(Strings.Serve_VerboseExtensions, extensions.FullName));
                    if (notebookPath is not null)
                        Console.Error.WriteLine("  " + string.Format(Strings.Serve_VerboseNotebook, notebookPath));
                }

                // Open the browser after Kestrel has bound its ports to
                // avoid a race where the browser requests before the server
                // is listening.
                if (!noBrowser)
                {
                    app.Lifetime.ApplicationStarted.Register(
                        () => BrowserLauncher.Open(launchUrl));
                }

                var ct = context.GetCancellationToken();
                ct.Register(() => app.Lifetime.StopApplication());
                await app.RunAsync();

                context.ExitCode = ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Messages.Error(
                    string.Format(Strings.Serve_StartFailed, ex.Message)));
                context.ExitCode = ExitCodes.CellFailure;
            }
        });

        return command;
    }
}
