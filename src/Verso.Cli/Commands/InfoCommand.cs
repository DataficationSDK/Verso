using System.CommandLine;
using System.Reflection;
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
        var command = new Command("info", "Display Verso CLI version, runtime, and extension information.");
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

            Console.WriteLine($"Verso CLI {cliVersion}");
            Console.WriteLine($"Runtime:    .NET {Environment.Version}");
            Console.WriteLine($"Engine:     Verso {engineVersion}");

            var kernels = extensionHost.GetKernels();
            if (kernels.Count > 0)
            {
                Console.WriteLine("Extensions:");
                foreach (var kernel in kernels)
                {
                    Console.WriteLine($"  {kernel.ExtensionId,-28} {kernel.Name,-24} {kernel.Version}");
                }
            }

            var serializers = extensionHost.GetSerializers();
            if (serializers.Count > 0)
            {
                Console.WriteLine("Serializers:");
                foreach (var serializer in serializers)
                {
                    var extensions = string.Join(", ", serializer.FileExtensions);
                    Console.WriteLine($"  {serializer.FormatId,-28} {extensions}");
                }
            }

            var formatters = extensionHost.GetFormatters();
            if (formatters.Count > 0)
            {
                Console.WriteLine("Formatters:");
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
