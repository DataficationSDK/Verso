using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Verso.Cli.Commands;
using Verso.Cli.Utilities;

var rootCommand = new RootCommand("Verso CLI — execute, serve, and convert Verso notebooks.");

// Subcommands
rootCommand.AddCommand(RunCommand.Create());
rootCommand.AddCommand(InfoCommand.Create());

rootCommand.AddCommand(ServeCommand.Create());
rootCommand.AddCommand(ConvertCommand.Create());
rootCommand.AddCommand(ExportCommand.Create());
rootCommand.AddCommand(ReplCommand.Create());

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
