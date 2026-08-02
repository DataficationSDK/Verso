using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Verso.Cli.Commands;
using Verso.Cli.Utilities;

// Ahead of the command tree, because building it reads every description.
LanguageOption.ApplyFromArguments(args);

var rootCommand = new RootCommand("Verso CLI — execute, serve, and convert Verso notebooks.");

// Global rather than per-command: it has to be accepted before a subcommand name so that
// "verso --language de --help" works, and every subcommand reads the same instance.
rootCommand.AddGlobalOption(LanguageOption.Instance);

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
