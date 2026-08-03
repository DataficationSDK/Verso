using System.CommandLine;
using Verso.Cli.Execution;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Execution;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso run' command for headless notebook execution.
/// </summary>
public static class RunCommand
{
    public static Command Create()
    {
        var notebookArg = new Argument<FileInfo>("notebook", Strings.Run_ArgNotebook);

        var cellOption = new Option<string[]>("--cell", Strings.Run_OptCell)
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };

        var kernelOption = new Option<string?>("--kernel", Strings.Run_OptKernel);

        var outputOption = new Option<OutputFormat>("--output", () => OutputFormat.Text, Strings.Run_OptOutput);

        var outputFileOption = new Option<FileInfo?>("--output-file", Strings.Run_OptOutputFile);

        var saveOption = new Option<bool>("--save", () => false, Strings.Run_OptSave);

        var timeoutOption = new Option<int>("--timeout", () => 300, Strings.Run_OptTimeout);

        var extensionsOption = new Option<DirectoryInfo?>("--extensions", Strings.Option_Extensions);

        var failFastOption = new Option<bool>("--fail-fast", () => false, Strings.Run_OptFailFast);

        var failOnStderrOption = new Option<bool>("--fail-on-stderr", () => false, Strings.Run_OptFailOnStderr);

        var verboseOption = new Option<bool>("--verbose", () => false, Strings.Run_OptVerbose);

        var paramOption = new Option<string[]>("--param", Strings.Run_OptParam)
        {
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };

        var interactiveOption = new Option<bool>("--interactive", () => false, Strings.Run_OptInteractive);

        var includeMarkdownOption = new Option<bool>("--include-markdown", () => false, Strings.Run_OptIncludeMarkdown);

        var showParametersOption = new Option<bool>("--show-parameters", () => false, Strings.Run_OptShowParameters);

        var trustLocalOption = new Option<bool>("--trust-local-assemblies", () => false, Strings.Run_OptTrustLocal);

        var ignoreViewStateOption = new Option<bool>("--ignore-view-state", () => false, Strings.Run_OptIgnoreViewState);

        var pythonOption = PythonInterpreterOption.Create();
        var autoInstallOption = PythonAutoInstallOption.Create();

        var command = new Command("run", Strings.Run_Description)
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
            failOnStderrOption,
            verboseOption,
            paramOption,
            interactiveOption,
            includeMarkdownOption,
            showParametersOption,
            trustLocalOption,
            ignoreViewStateOption,
            pythonOption,
            autoInstallOption
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

            // Set before anything executes, because it changes what counts as a failed cell for
            // the summary, the JSON, --fail-fast, and the exit code alike.
            CellOutcome.FailOnStandardError = context.ParseResult.GetValueForOption(failOnStderrOption);

            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var paramValues = context.ParseResult.GetValueForOption(paramOption);
            var interactive = context.ParseResult.GetValueForOption(interactiveOption);
            var includeMarkdown = context.ParseResult.GetValueForOption(includeMarkdownOption);
            var showParameters = context.ParseResult.GetValueForOption(showParametersOption);
            var trustLocal = context.ParseResult.GetValueForOption(trustLocalOption);
            var ignoreViewState = context.ParseResult.GetValueForOption(ignoreViewStateOption);

            PythonInterpreterOption.Apply(context.ParseResult.GetValueForOption(pythonOption));
            PythonAutoInstallOption.Apply(context.ParseResult.GetValueForOption(autoInstallOption));

            // Parse --param name=value pairs into a dictionary
            var paramDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (paramValues is { Length: > 0 })
            {
                foreach (var pv in paramValues)
                {
                    var eqIndex = pv.IndexOf('=');
                    if (eqIndex <= 0)
                    {
                        Console.Error.WriteLine(Messages.Error(
                            string.Format(Strings.Run_InvalidParamFormat, pv)));
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
                Interactive = interactive,
                TrustLocalAssemblies = trustLocal
            };

            var runner = new HeadlessRunner();
            var result = await runner.ExecuteAsync(options, linkedCts.Token);

            // Handle file not found
            if (result.ExitCode == ExitCodes.FileNotFound)
            {
                Console.Error.WriteLine(Messages.Error(
                    string.Format(Strings.Error_NotebookNotFound, notebook.FullName)));
                context.ExitCode = ExitCodes.FileNotFound;
                return;
            }

            // Handle serialization error
            if (result.ExitCode == ExitCodes.SerializationError)
            {
                var ext = Path.GetExtension(notebook.FullName);
                Console.Error.WriteLine(Messages.Error(
                    string.Format(Strings.Run_UnsupportedFormat, ext)));
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
                    var renderer = new OutputRenderer(Console.Out, Console.Error, verbose, includeMarkdown, showParameters,
                        respectViewState: !ignoreViewState);
                    for (var i = 0; i < result.Cells.Count; i++)
                    {
                        var cell = result.Cells[i];
                        var cellResult = result.CellResults.FirstOrDefault(r => r.CellId == cell.Id);
                        if (cellResult is not null)
                        {
                            renderer.RenderCell(i, cell, cellResult, result.ResolvedParameters);
                        }
                        else if (showParameters && cell.Type is "parameters")
                        {
                            // Parameters cells may not have an execution result but
                            // should still be rendered when --show-parameters is set.
                            renderer.RenderCell(i, cell,
                                ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero),
                                result.ResolvedParameters);
                        }
                    }
                    renderer.WriteSummary(result.CellResults, result.Cells, result.TotalElapsed);
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
