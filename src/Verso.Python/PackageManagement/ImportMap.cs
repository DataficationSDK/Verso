namespace Verso.Python.PackageManagement;

/// <summary>
/// The distribution a module name resolves to, and whether that came from a known mapping or
/// from assuming the module and the distribution share a name.
/// </summary>
/// <param name="Module">The root module name the cell imported.</param>
/// <param name="Distribution">The distribution to install.</param>
/// <param name="Vetted">
/// Whether the name came from the curated table or from a mapping the user configured. A
/// guess is never installed without asking, because a misspelled import resolves to a
/// plausible-looking package name that nobody has ever published for a reason.
/// </param>
internal sealed record ResolvedImport(string Module, string Distribution, bool Vetted);

/// <summary>
/// Maps Python module names to the distributions that provide them. Most packages install
/// under the name they are imported by, so only the ones that do not are listed here.
/// </summary>
internal static class ImportMap
{
    /// <summary>
    /// Modules whose distribution name differs from their import name. Kept as code rather
    /// than data: it is compiled into the assembly either way, and a dictionary shows its
    /// changes in a review without a loading path to get wrong.
    /// </summary>
    private static readonly Dictionary<string, string> Curated = new(StringComparer.Ordinal)
    {
        ["attr"] = "attrs",
        ["bs4"] = "beautifulsoup4",
        ["cairo"] = "pycairo",
        ["cv2"] = "opencv-python",
        ["dateutil"] = "python-dateutil",
        ["docx"] = "python-docx",
        ["dotenv"] = "python-dotenv",
        ["fitz"] = "PyMuPDF",
        ["git"] = "GitPython",
        ["grpc"] = "grpcio",
        ["gi"] = "PyGObject",
        ["jwt"] = "PyJWT",
        ["Crypto"] = "pycryptodome",
        ["Cryptodome"] = "pycryptodomex",
        ["magic"] = "python-magic",
        ["mpl_toolkits"] = "matplotlib",
        ["OpenGL"] = "PyOpenGL",
        ["OpenSSL"] = "pyOpenSSL",
        ["PIL"] = "Pillow",
        ["pkg_resources"] = "setuptools",
        ["pptx"] = "python-pptx",
        ["psycopg2"] = "psycopg2-binary",
        ["pydantic_settings"] = "pydantic-settings",
        ["pythoncom"] = "pywin32",
        ["win32api"] = "pywin32",
        ["win32com"] = "pywin32",
        ["win32con"] = "pywin32",
        ["Quartz"] = "pyobjc-framework-Quartz",
        ["serial"] = "pyserial",
        ["skimage"] = "scikit-image",
        ["sklearn"] = "scikit-learn",
        ["slugify"] = "python-slugify",
        ["socks"] = "PySocks",
        ["speech_recognition"] = "SpeechRecognition",
        ["sqlalchemy"] = "SQLAlchemy",
        ["tables"] = "PyTables",
        ["usb"] = "pyusb",
        ["wx"] = "wxPython",
        ["yaml"] = "PyYAML",
        ["zmq"] = "pyzmq",
        ["zoneinfo"] = "backports.zoneinfo",
    };

    /// <summary>
    /// Resolve a root module name to the distribution that provides it. A configured mapping
    /// wins over the curated table, which wins over assuming the two names match.
    /// </summary>
    /// <param name="module">The root module name, as the import scan reported it.</param>
    /// <param name="userMap">Extra module-to-distribution entries from the kernel options.</param>
    public static ResolvedImport Resolve(string module, IReadOnlyDictionary<string, string>? userMap = null)
    {
        var root = RootOf(module);

        if (userMap is not null)
        {
            foreach (var entry in userMap)
            {
                if (!string.Equals(entry.Key, root, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                return new ResolvedImport(root, entry.Value.Trim(), Vetted: true);
            }
        }

        if (Curated.TryGetValue(root, out var distribution))
            return new ResolvedImport(root, distribution, Vetted: true);

        return new ResolvedImport(root, root, Vetted: false);
    }

    /// <summary>The first segment of a dotted module path.</summary>
    public static string RootOf(string module)
    {
        var text = (module ?? string.Empty).Trim();
        var dot = text.IndexOf('.');
        return dot < 0 ? text : text.Substring(0, dot);
    }

    /// <summary>Whether the curated table knows this module. Exposed for the tests.</summary>
    internal static bool IsCurated(string module) => Curated.ContainsKey(RootOf(module));
}
