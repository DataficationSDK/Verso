using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Verso.Cli.Commands;
using Verso.Cli.Utilities;

var rootCommand = new RootCommand("Verso CLI — execute, serve, and convert Verso notebooks.");

// Subcommands
rootCommand.AddCommand(RunCommand.Create());
rootCommand.AddCommand(InfoCommand.Create());

// Stubs for future commands
var serveCommand = new Command("serve", "Launch the Verso Blazor application (not yet implemented).");
serveCommand.SetHandler(() =>
{
    Console.WriteLine("The 'serve' command is not yet implemented.");
});
rootCommand.AddCommand(serveCommand);

var convertCommand = new Command("convert", "Convert between notebook formats (not yet implemented).");
convertCommand.SetHandler(() =>
{
    Console.WriteLine("The 'convert' command is not yet implemented.");
});
rootCommand.AddCommand(convertCommand);

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseVersionOption()
    .UseExceptionHandler((ex, context) =>
    {
        Console.Error.WriteLine($"Unhandled error: {ex.Message}");
        context.ExitCode = ExitCodes.CellFailure;
    })
    .Build();

return await parser.InvokeAsync(args);
