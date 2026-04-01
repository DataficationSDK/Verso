using System.CommandLine;
using Verso.Cli.Hosting;
using Verso.Cli.Utilities;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso serve' command to launch the Verso Blazor application.
/// </summary>
public static class ServeCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo?>("notebook", () => null,
            "Optional notebook to open on startup.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var portOption = new Option<int>("--port", () => 5050,
            "HTTP port to listen on.");

        var noBrowserOption = new Option<bool>("--no-browser", () => false,
            "Do not open a browser tab on startup.");

        var noHttpsOption = new Option<bool>("--no-https", () => false,
            "Disable HTTPS (HTTP only).");

        var extensionsOption = new Option<DirectoryInfo?>("--extensions",
            "Directory to scan for additional extension assemblies.");

        var verboseOption = new Option<bool>("--verbose", () => false,
            "Print startup details to stderr.");

        var command = new Command("serve", "Launch the Verso Blazor application as a local web server.")
        {
            notebookArg,
            portOption,
            noBrowserOption,
            noHttpsOption,
            extensionsOption,
            verboseOption
        };

        command.SetHandler(async (context) =>
        {
            var notebook = context.ParseResult.GetValueForArgument(notebookArg);
            var port = context.ParseResult.GetValueForOption(portOption);
            var noBrowser = context.ParseResult.GetValueForOption(noBrowserOption);
            var noHttps = context.ParseResult.GetValueForOption(noHttpsOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            // Validate notebook path if provided
            string? notebookPath = null;
            if (notebook is not null)
            {
                notebookPath = Path.GetFullPath(notebook.FullName);
                if (!File.Exists(notebookPath))
                {
                    Console.Error.WriteLine($"Error: Notebook file not found: {notebookPath}");
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
                    ExtensionsDirectory = extensions?.FullName
                };

                var app = BlazorHostBuilder.Build(options);

                // Build the URL
                var baseUrl = $"http://localhost:{port}";
                var launchUrl = notebookPath is not null
                    ? $"{baseUrl}/?recover={Uri.EscapeDataString(notebookPath)}"
                    : baseUrl;

                Console.WriteLine($"Verso is running at {baseUrl}");
                Console.WriteLine("Press Ctrl+C to stop.");

                if (verbose)
                {
                    if (!noHttps)
                        Console.Error.WriteLine($"  HTTPS: https://localhost:{port + 1}");
                    if (extensions is not null)
                        Console.Error.WriteLine($"  Extensions: {extensions.FullName}");
                    if (notebookPath is not null)
                        Console.Error.WriteLine($"  Notebook: {notebookPath}");
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
                Console.Error.WriteLine($"Error: Failed to start server: {ex.Message}");
                context.ExitCode = ExitCodes.CellFailure;
            }
        });

        return command;
    }
}
