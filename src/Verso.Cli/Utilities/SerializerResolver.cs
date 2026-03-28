using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Cli.Utilities;

/// <summary>
/// Resolves the appropriate <see cref="INotebookSerializer"/> for a given file path
/// based on file extension, using the serializers registered in the extension host.
/// </summary>
public static class SerializerResolver
{
    /// <summary>
    /// Finds a serializer that can import the given file path.
    /// </summary>
    /// <returns>The matching serializer.</returns>
    /// <exception cref="SerializerNotFoundException">Thrown when no serializer matches the file extension.</exception>
    public static INotebookSerializer Resolve(ExtensionHost extensionHost, string filePath)
    {
        var serializer = extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(filePath));

        if (serializer is null)
        {
            var extension = Path.GetExtension(filePath);
            throw new SerializerNotFoundException(
                $"Unsupported notebook format '{extension}'. Supported formats: .verso, .ipynb, .dib");
        }

        return serializer;
    }

    /// <summary>
    /// Finds a serializer by target format identifier (e.g. "verso", "ipynb", "dib").
    /// </summary>
    /// <exception cref="SerializerNotFoundException">Thrown when no serializer matches the format.</exception>
    public static INotebookSerializer ResolveByFormat(ExtensionHost extensionHost, string format)
    {
        // Normalize CLI format names to serializer FormatId values
        var formatId = format.ToLowerInvariant() switch
        {
            "ipynb" => "jupyter",
            _ => format.ToLowerInvariant()
        };

        var serializer = extensionHost.GetSerializers()
            .FirstOrDefault(s => string.Equals(s.FormatId, formatId, StringComparison.OrdinalIgnoreCase));

        if (serializer is null)
        {
            throw new SerializerNotFoundException(
                $"Unsupported output format '{format}'. Supported formats: verso, ipynb, dib");
        }

        return serializer;
    }
}

/// <summary>
/// Thrown when no serializer can handle the given file format.
/// </summary>
public sealed class SerializerNotFoundException : Exception
{
    public SerializerNotFoundException(string message) : base(message) { }
}
