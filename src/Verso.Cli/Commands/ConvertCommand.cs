using System.CommandLine;
using Verso.Cli.Utilities;
using Verso.Extensions;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso convert' command for notebook format conversion.
/// </summary>
public static class ConvertCommand
{
    public static Command Create()
    {
        var inputArg = new Argument<FileInfo>("input", "Path to the source notebook file.");

        var toOption = new Option<string>("--to", "Target format: verso, ipynb, or dib.")
        {
            IsRequired = true
        };
        toOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(toOption);
            if (value is not ("verso" or "ipynb" or "dib"))
                result.ErrorMessage = $"Unsupported format '{value}'. Supported: verso, ipynb, dib";
        });

        var outputOption = new Option<FileInfo?>("--output",
            "Output file path. Defaults to input filename with the new extension.");

        var stripOutputsOption = new Option<bool>("--strip-outputs", () => false,
            "Remove all cell outputs from the converted notebook.");

        var extensionsOption = new Option<DirectoryInfo?>("--extensions",
            "Directory to scan for additional extension assemblies.");

        var command = new Command("convert", "Convert between notebook formats.")
        {
            inputArg,
            toOption,
            outputOption,
            stripOutputsOption,
            extensionsOption
        };

        command.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArg);
            var to = context.ParseResult.GetValueForOption(toOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption);
            var stripOutputs = context.ParseResult.GetValueForOption(stripOutputsOption);
            var extensions = context.ParseResult.GetValueForOption(extensionsOption);

            var inputPath = Path.GetFullPath(input.FullName);
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: Input file not found: {inputPath}");
                context.ExitCode = ExitCodes.FileNotFound;
                return;
            }

            ExtensionHost? extensionHost = null;
            try
            {
                extensionHost = new ExtensionHost();
                extensionHost.ConsentHandler = (_, _) => Task.FromResult(true);
                await extensionHost.LoadBuiltInExtensionsAsync();

                if (extensions is not null)
                    await extensionHost.LoadFromDirectoryAsync(extensions.FullName);

                // Resolve input serializer
                Abstractions.INotebookSerializer inputSerializer;
                try
                {
                    inputSerializer = SerializerResolver.Resolve(extensionHost, inputPath);
                }
                catch (SerializerNotFoundException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Resolve output serializer
                Abstractions.INotebookSerializer outputSerializer;
                try
                {
                    outputSerializer = SerializerResolver.ResolveByFormat(extensionHost, to);
                }
                catch (SerializerNotFoundException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Deserialize
                var content = await File.ReadAllTextAsync(inputPath);
                Abstractions.NotebookModel notebook;
                try
                {
                    notebook = await inputSerializer.DeserializeAsync(content);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Failed to deserialize '{inputPath}': {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Strip outputs if requested
                if (stripOutputs)
                {
                    foreach (var cell in notebook.Cells)
                        cell.Outputs.Clear();
                }

                // Serialize
                string serialized;
                try
                {
                    serialized = await outputSerializer.SerializeAsync(notebook);
                }
                catch (NotSupportedException)
                {
                    Console.Error.WriteLine($"Error: Converting to '{to}' format is not yet supported.");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Serialization failed: {ex.Message}");
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Determine output path
                var outputPath = output is not null
                    ? Path.GetFullPath(output.FullName)
                    : Path.ChangeExtension(inputPath, outputSerializer.FileExtensions[0]);

                await File.WriteAllTextAsync(outputPath, serialized);
                Console.WriteLine($"Converted '{inputPath}' -> '{outputPath}'");
                context.ExitCode = ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                context.ExitCode = ExitCodes.CellFailure;
            }
            finally
            {
                if (extensionHost is not null)
                    await extensionHost.DisposeAsync();
            }
        });

        return command;
    }
}
