using System.CommandLine;
using System.Reflection;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Extensions;

namespace Verso.Cli.Commands;

/// <summary>
/// Implements the 'verso info' command that displays CLI version, runtime,
/// engine version, and discovered extensions.
/// </summary>
public static class InfoCommand
{
    public static Command Create()
    {
        var command = new Command("info", Strings.Info_Description);
        command.SetHandler(ExecuteAsync);
        return command;
    }

    private static async Task ExecuteAsync()
    {
        var cliVersion = typeof(InfoCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(InfoCommand).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        var engineVersion = typeof(Scaffold).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Scaffold).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        var extensionHost = new ExtensionHost();
        try
        {
            await extensionHost.LoadBuiltInExtensionsAsync();

            // Padded from the labels themselves rather than by counting spaces into each line,
            // because a translated label is not the length the English one was.
            var labelWidth = Math.Max(
                DisplayWidth.Measure(Strings.Info_LabelRuntime),
                DisplayWidth.Measure(Strings.Info_LabelEngine)) + 4;

            Console.WriteLine($"Verso CLI {cliVersion}");
            Console.WriteLine($"{DisplayWidth.PadRight(Strings.Info_LabelRuntime, labelWidth)}.NET {Environment.Version}");
            Console.WriteLine($"{DisplayWidth.PadRight(Strings.Info_LabelEngine, labelWidth)}Verso {engineVersion}");

            var kernels = extensionHost.GetKernels();
            if (kernels.Count > 0)
            {
                Console.WriteLine(Strings.Info_HeadingExtensions);
                foreach (var kernel in kernels)
                {
                    Console.WriteLine($"  {kernel.ExtensionId,-28} {kernel.Name,-24} {kernel.Version}");
                }
            }

            var serializers = extensionHost.GetSerializers();
            if (serializers.Count > 0)
            {
                Console.WriteLine(Strings.Info_HeadingSerializers);
                foreach (var serializer in serializers)
                {
                    var extensions = string.Join(", ", serializer.FileExtensions);
                    Console.WriteLine($"  {serializer.FormatId,-28} {extensions}");
                }
            }

            var formatters = extensionHost.GetFormatters();
            if (formatters.Count > 0)
            {
                Console.WriteLine(Strings.Info_HeadingFormatters);
                foreach (var formatter in formatters)
                {
                    Console.WriteLine($"  {formatter.ExtensionId,-28} {formatter.Name,-24} {formatter.Version}");
                }
            }
        }
        finally
        {
            await extensionHost.DisposeAsync();
        }
    }
}
