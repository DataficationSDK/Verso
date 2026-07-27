namespace Verso.Python.PackageManagement;

/// <summary>Why a requirement string was not passed to the installer.</summary>
internal enum RequirementRefusal
{
    /// <summary>It names a distribution, which is the only thing a notebook may ask for.</summary>
    None,

    /// <summary>It would reach the installer as a switch rather than a package.</summary>
    InstallerOption,

    /// <summary>It names somewhere to install from rather than something to install.</summary>
    ExternalSource,
}

/// <summary>
/// Decides whether a requirement string is something a notebook is allowed to ask for.
/// <para>
/// Requirements travel to pip and uv as individual arguments, and they arrive from places the person
/// running the notebook did not necessarily write: a declared dependency list, or an inline script
/// metadata block in a downloaded file. A notebook may name a package. It may not decide where
/// packages come from, because that decision is the whole of the trust in an install.
/// </para>
/// <para>
/// Two shapes are refused. A string beginning with a hyphen is an installer switch, and switches
/// change where packages come from and what runs while they install. A string carrying a location,
/// meaning a direct reference of the form <c>name @ url</c>, a version control reference, a bare URL
/// or a filesystem path, points the installer at infrastructure rather than at an index, which is
/// the same decision by another route. What is left is a name with optional extras, version
/// specifiers and environment markers, which is what a dependency list is for.
/// </para>
/// <para>
/// This does not make installing safe, and it is not meant to: any package can run code while it
/// builds. It keeps a notebook from choosing the source, so what installs is what the configured
/// index serves under that name.
/// </para>
/// </summary>
internal static class RequirementGuard
{
    /// <summary>Version control schemes pip accepts as an install target.</summary>
    private static readonly string[] VersionControlPrefixes = ["git+", "hg+", "svn+", "bzr+"];

    /// <summary>Whether this string would reach the installer as an option rather than a package.</summary>
    public static bool IsInstallerOption(string? requirement)
    {
        var text = requirement?.TrimStart();
        return !string.IsNullOrEmpty(text) && text![0] == '-';
    }

    /// <summary>
    /// Whether this string is something a notebook may ask to have installed, and if not, why not.
    /// </summary>
    public static RequirementRefusal Classify(string? requirement)
    {
        if (IsInstallerOption(requirement))
            return RequirementRefusal.InstallerOption;

        var text = requirement?.Trim();
        if (string.IsNullOrEmpty(text))
            return RequirementRefusal.None;

        // Everything after the first semicolon is an environment marker, which decides whether the
        // requirement applies rather than what it points at.
        var marker = text!.IndexOf(';');
        var target = (marker >= 0 ? text[..marker] : text).Trim();
        if (target.Length == 0)
            return RequirementRefusal.None;

        // "name @ url" is the direct reference form. The name in front of it is decoration: the URL
        // is what gets fetched.
        if (target.Contains('@', StringComparison.Ordinal))
            return RequirementRefusal.ExternalSource;

        foreach (var prefix in VersionControlPrefixes)
        {
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return RequirementRefusal.ExternalSource;
        }

        if (target.Contains("://", StringComparison.Ordinal))
            return RequirementRefusal.ExternalSource;

        // A path, absolute or relative, on either platform. A distribution name cannot begin with
        // any of these, so nothing legitimate is caught by the test.
        if (target[0] is '.' or '/' or '\\' or '~')
            return RequirementRefusal.ExternalSource;

        if (target.Length >= 3 && target[1] == ':' && target[2] is '/' or '\\'
            && char.IsAsciiLetter(target[0]))
            return RequirementRefusal.ExternalSource;

        return RequirementRefusal.None;
    }
}
