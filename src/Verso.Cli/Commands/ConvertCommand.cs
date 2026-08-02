using System.CommandLine;
using Verso.Cli.Resources;
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
        var inputArg = new Argument<FileInfo>("input", Strings.Arg_InputNotebook);

        var toOption = new Option<string>("--to", Strings.Convert_OptTo)
        {
            IsRequired = true
        };
        toOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(toOption);
            if (value is not ("verso" or "ipynb" or "md" or "dib"))
                result.ErrorMessage = string.Format(
                    Strings.Convert_InvalidTarget, value, "verso, ipynb, md, dib");
        });

        var outputOption = new Option<FileInfo?>("--output", Strings.Convert_OptOutput);

        var stripOutputsOption = new Option<bool>("--strip-outputs", () => false, Strings.Convert_OptStripOutputs);

        var extensionsOption = new Option<DirectoryInfo?>("--extensions", Strings.Option_Extensions);

        var command = new Command("convert", Strings.Convert_Description)
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
                Console.Error.WriteLine(Messages.Error(
                    string.Format(Strings.Error_InputNotFound, inputPath)));
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
                    Console.Error.WriteLine(Messages.Error(ex.Message));
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
                    Console.Error.WriteLine(Messages.Error(ex.Message));
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
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Error_DeserializeFailed, inputPath, ex.Message)));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Run post-processors after deserialization. The host open path does the
                // same; without this, format-specific transforms (e.g. Polyglot SQL import)
                // never run for CLI conversions, leaving SQL cells and #!connect commands
                // untranslated.
                try
                {
                    var postProcessors = extensionHost.GetPostProcessors()
                        .Where(pp => pp.CanProcess(inputPath, inputSerializer.FormatId))
                        .OrderBy(pp => pp.Priority);
                    foreach (var pp in postProcessors)
                        notebook = await pp.PostDeserializeAsync(notebook, inputPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Convert_PostProcessingFailed, ex.Message)));
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
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Convert_NotSupported, to)));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Messages.Error(
                        string.Format(Strings.Convert_SerializationFailed, ex.Message)));
                    context.ExitCode = ExitCodes.SerializationError;
                    return;
                }

                // Determine output path
                var outputPath = output is not null
                    ? Path.GetFullPath(output.FullName)
                    : Path.ChangeExtension(inputPath, outputSerializer.FileExtensions[0]);

                await File.WriteAllTextAsync(outputPath, serialized);
                Console.WriteLine(string.Format(Strings.Convert_Done, inputPath, outputPath));
                context.ExitCode = ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Messages.Error(ex.Message));
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
