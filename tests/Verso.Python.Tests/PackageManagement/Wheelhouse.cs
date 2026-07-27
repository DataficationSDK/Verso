using System.IO.Compression;
using System.Text;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// A directory holding a wheel this suite builds itself, so an install can be exercised end to end
/// without reaching a package index. A wheel is a zip with a metadata directory beside the module,
/// which is little enough to fabricate and keeps the tests offline and deterministic.
/// </summary>
internal sealed class Wheelhouse : IDisposable
{
    private Wheelhouse(string directory, string distribution, string module)
    {
        Directory = directory;
        Distribution = distribution;
        Module = module;
    }

    /// <summary>The directory to hand the installer as its only source of packages.</summary>
    public string Directory { get; }

    /// <summary>The distribution name to install.</summary>
    public string Distribution { get; }

    /// <summary>The module name the distribution provides.</summary>
    public string Module { get; }

    /// <summary>Installer arguments that forbid any network access.</summary>
    public IReadOnlyList<string> OfflineArguments => new[]
    {
        "--no-index",
        "--find-links", Directory,
        "--no-deps",
        "--disable-pip-version-check",
    };

    /// <summary>
    /// Build a wheelhouse containing one trivial distribution. The name is unmistakably local, so
    /// a test that somehow reached an index would fail rather than install something real.
    /// </summary>
    public static Wheelhouse Create(string suffix = "")
    {
        var root = Path.Combine(
            Path.GetTempPath(), "verso-wheelhouse-" + Guid.NewGuid().ToString("n").Substring(0, 8));
        System.IO.Directory.CreateDirectory(root);

        var module = "verso_probe_pkg" + suffix;
        var distribution = module.Replace('_', '-');
        const string version = "1.0.0";

        var wheelPath = Path.Combine(root, $"{module}-{version}-py3-none-any.whl");
        var moduleFile = module + ".py";
        var distInfo = $"{module}-{version}.dist-info";

        var moduleSource = $"VALUE = \"{module} loaded\"\n\n\ndef greet():\n    return VALUE\n";

        var metadata = string.Join("\n", new[]
        {
            "Metadata-Version: 2.1",
            $"Name: {distribution}",
            $"Version: {version}",
            "Summary: A fabricated package used by the Verso test suite.",
            "",
        });

        var wheel = string.Join("\n", new[]
        {
            "Wheel-Version: 1.0",
            "Generator: verso-tests",
            "Root-Is-Purelib: true",
            "Tag: py3-none-any",
            "",
        });

        // RECORD lists the archive's files. Hashes are allowed to be absent, which keeps this
        // free of a digest step the installer does not require for a local wheel.
        var record = string.Join("\n", new[]
        {
            $"{moduleFile},,",
            $"{distInfo}/METADATA,,",
            $"{distInfo}/WHEEL,,",
            $"{distInfo}/RECORD,,",
            "",
        });

        using (var archive = ZipFile.Open(wheelPath, ZipArchiveMode.Create))
        {
            Write(archive, moduleFile, moduleSource);
            Write(archive, $"{distInfo}/METADATA", metadata);
            Write(archive, $"{distInfo}/WHEEL", wheel);
            Write(archive, $"{distInfo}/RECORD", record);
        }

        return new Wheelhouse(root, distribution, module);
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch { /* a leftover temp directory is not worth failing a test over */ }
    }
}
