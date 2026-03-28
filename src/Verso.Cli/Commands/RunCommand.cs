using System.CommandLine;
using Verso.Cli.Execution;
using Verso.Cli.Utilities;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso run' command for headless notebook execution.
/// </summary>
public static class RunCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo>("notebook", "Path to a .verso, .ipynb, or .dib file.");

        var cellOption = new Option<string[]>("--cell", "Execute only the specified cell (index or GUID). May be repeated.")
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };

        var kernelOption = new Option<string?>("--kernel", "Override the notebook's default kernel.");

        var outputOption = new Option<OutputFormat>("--output", () => OutputFormat.Text,
            "Output format: text, json, or none.");

        var outputFileOption = new Option<FileInfo?>("--output-file",
            "Write output to a file instead of stdout. Implies --output json if no format specified.");

        var saveOption = new Option<bool>("--save", () => false,
            "Save updated outputs back to the notebook file after execution.");

        var timeoutOption = new Option<int>("--timeout", () => 300,
            "Maximum total execution time in seconds.");

        var extensionsOption = new Option<DirectoryInfo?>("--extensions",
            "Additional directory to scan for extension assemblies.");

        var failFastOption = new Option<bool>("--fail-fast", () => false,
            "Stop execution on the first cell failure.");

        var verboseOption = new Option<bool>("--verbose", () => false,
            "Print cell execution progress to stderr.");

        var paramOption = new Option<string[]>("--param",
            "Set a notebook parameter (format: name=value). May be repeated.")
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };

        var interactiveOption = new Option<bool>("--interactive", () => false,
            "Prompt for missing required parameters on stdin instead of failing.");

        var command = new Command("run", "Execute a notebook headlessly and stream cell outputs.")
        {
            notebookArg,
            cellOption,
            kernelOption,
            outputOption,
            outputFileOption,
            saveOption,
            timeoutOption,
            extensionsOption,
            failFastOption,
            verboseOption,
            paramOption,
            interactiveOption
        };

        command.SetHandler(async (context) =>
        {
            var notebook = context.ParseResult.GetValueForArgument(notebookArg);
            var cells = context.ParseResult.GetValueForOption(cellOption);
            var kernel = context.ParseResult.GetValueForOption(kernelOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var outputFile = context.ParseResult.GetValueForOption(outputFileOption);
            var save = context.ParseResult.GetValueForOption(saveOption);
            var timeout = context.ParseResult.GetValueForOption(timeoutOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);
            var failFast = context.ParseResult.GetValueForOption(failFastOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var paramValues = context.ParseResult.GetValueForOption(paramOption);
            var interactive = context.ParseResult.GetValueForOption(interactiveOption);

            // Parse --param name=value pairs into a dictionary
            var paramDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (paramValues is { Length: > 0 })
            {
                foreach (var pv in paramValues)
                {
                    var eqIndex = pv.IndexOf('=');
                    if (eqIndex <= 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid --param format '{pv}'. Expected name=value.");
                        context.ExitCode = ExitCodes.MissingParameters;
                        return;
                    }
                    paramDict[pv[..eqIndex]] = pv[(eqIndex + 1)..];
                }
            }

            // If --output-file is specified without explicit --output, default to json
            if (outputFile is not null && context.ParseResult.FindResultFor(outputOption) is null)
                output = OutputFormat.Json;

            var ct = context.GetCancellationToken();

            // Link Ctrl+C to cancellation
            using var ctrlCCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                ctrlCCts.Cancel();
            };
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ctrlCCts.Token);

            var options = new RunOptions
            {
                FilePath = notebook.FullName,
                KernelOverride = kernel,
                CellSelectors = cells is { Length: > 0 } ? cells : null,
                ExtensionsDirectory = extensions?.FullName,
                FailFast = failFast,
                Save = save,
                TimeoutSeconds = timeout,
                Verbose = verbose,
                Parameters = paramDict.Count > 0 ? paramDict : null,
                Interactive = interactive
            };

            var runner = new HeadlessRunner();
            var result = await runner.ExecuteAsync(options, linkedCts.Token);

            // Handle file not found
            if (result.ExitCode == ExitCodes.FileNotFound)
            {
                Console.Error.WriteLine($"Error: Notebook file not found: {notebook.FullName}");
                context.ExitCode = ExitCodes.FileNotFound;
                return;
            }

            // Handle serialization error
            if (result.ExitCode == ExitCodes.SerializationError)
            {
                var ext = Path.GetExtension(notebook.FullName);
                Console.Error.WriteLine($"Error: Unsupported or invalid notebook format '{ext}'.");
                context.ExitCode = ExitCodes.SerializationError;
                return;
            }

            // Handle missing/invalid parameters (error already written by HeadlessRunner)
            if (result.ExitCode == ExitCodes.MissingParameters)
            {
                context.ExitCode = ExitCodes.MissingParameters;
                return;
            }

            // Render output
            switch (output)
            {
                case OutputFormat.Text:
                    var renderer = new OutputRenderer(Console.Out, Console.Error, verbose);
                    for (var i = 0; i < result.Cells.Count; i++)
                    {
                        var cell = result.Cells[i];
                        var cellResult = result.CellResults.FirstOrDefault(r => r.CellId == cell.Id);
                        if (cellResult is not null)
                        {
                            renderer.RenderCell(i, cell, cellResult);
                        }
                    }
                    renderer.WriteSummary(result.CellResults, result.TotalElapsed);
                    break;

                case OutputFormat.Json:
                    var doc = JsonOutputWriter.Build(
                        result.NotebookPath,
                        result.Cells,
                        result.CellResults,
                        result.TotalElapsed,
                        result.Variables,
                        result.ResolvedParameters);

                    if (outputFile is not null)
                        await JsonOutputWriter.WriteToFileAsync(doc, outputFile.FullName);
                    else
                        JsonOutputWriter.WriteTo(doc, Console.Out);
                    break;

                case OutputFormat.None:
                    // Suppress all output; exit code only
                    break;
            }

            context.ExitCode = result.ExitCode;
        });

        return command;
    }
}

public enum OutputFormat
{
    Text,
    Json,
    None
}
