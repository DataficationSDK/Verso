using System.Reflection;
using System.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Verso.Tests.Extensions;

/// <summary>
/// Builds a tiny assembly that ships translations the standard way: a neutral resource in the
/// assembly itself and a German satellite under a <c>de</c> folder beside it, which is how
/// <c>dotnet build</c> and <c>dotnet pack</c> lay an extension out.
/// </summary>
internal static class SatelliteProbe
{
    public const string AssemblyName = "Probe";

    /// <summary>Writes <c>Probe.dll</c> and <c>de/Probe.resources.dll</c> into <paramref name="dir"/>.</summary>
    /// <returns>The path of the main assembly.</returns>
    public static string Build(string dir)
    {
        var mainPath = Path.Combine(dir, AssemblyName + ".dll");
        Compile(
            AssemblyName,
            """
            using System.Globalization;
            using System.Resources;
            public static class Probe
            {
                public static string Greeting(string culture) =>
                    new ResourceManager("Probe.Strings", typeof(Probe).Assembly)
                        .GetString("Greeting", CultureInfo.GetCultureInfo(culture))!;
            }
            """,
            mainPath,
            Resource("Probe.Strings.resources", ("Greeting", "Hello")));

        Directory.CreateDirectory(Path.Combine(dir, "de"));
        Compile(
            AssemblyName + ".resources",
            "[assembly: System.Reflection.AssemblyCulture(\"de\")]",
            SatellitePath(dir),
            Resource("Probe.Strings.de.resources", ("Greeting", "Hallo")));

        return mainPath;
    }

    public static string SatellitePath(string dir) => Path.Combine(dir, "de", AssemblyName + ".resources.dll");

    /// <summary>Asks the loaded probe for its greeting in one language.</summary>
    public static string Greeting(Assembly probe, string culture) =>
        (string)probe.GetType("Probe")!.GetMethod("Greeting")!.Invoke(null, new object[] { culture })!;

    private static ResourceDescription Resource(string name, params (string Key, string Value)[] entries)
    {
        var stream = new MemoryStream();
        using (var writer = new ResourceWriter(stream))
        {
            foreach (var (key, value) in entries)
                writer.AddResource(key, value);
            writer.Generate();
        }
        var bytes = stream.ToArray();
        return new ResourceDescription(name, () => new MemoryStream(bytes), isPublic: true);
    }

    private static void Compile(string assemblyName, string source, string outputPath, params ResourceDescription[] resources)
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = tpa.Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = File.Create(outputPath);
        var result = compilation.Emit(output, manifestResources: resources);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    }
}
