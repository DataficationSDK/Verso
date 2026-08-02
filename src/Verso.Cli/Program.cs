using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Verso.Cli.Commands;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

// Ahead of the command tree, because building it reads every description.
LanguageOption.ApplyFromArguments(args);

var rootCommand = new RootCommand(Strings.Root_Description);

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
        Console.Error.WriteLine(string.Format(Strings.Root_UnhandledError, ex.Message));
        context.ExitCode = ExitCodes.CellFailure;
    })
    .Build();

return await parser.InvokeAsync(args);
