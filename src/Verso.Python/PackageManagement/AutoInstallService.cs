using Verso.Abstractions;
using Verso.Python.Kernel;

namespace Verso.Python.PackageManagement;

/// <summary>
/// Decides what a cell is allowed to install, asks the user when the policy says to, and runs
/// the install. One instance per kernel session, because the record of what has already been
/// asked and answered is what keeps a cell from re-prompting on every run.
/// </summary>
internal sealed class AutoInstallService
{
    private readonly HashSet<string> _declined = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ensured = new(StringComparer.Ordinal);

    private AutoInstallPolicy _policy;
    private IReadOnlyDictionary<string, string> _userMap;
    private UvUsage _useUv;
    private bool _hideInstallOutput;

    public AutoInstallService(PythonKernelOptions options)
    {
        _policy = options.AutoInstall;
        _userMap = options.ImportMap;
        _useUv = options.UseUv;
        _hideInstallOutput = options.HideInstallOutput;
        Dependencies = options.Dependencies;
    }

    /// <summary>Whether anything is scanned or installed at all.</summary>
    public bool Enabled => _policy != AutoInstallPolicy.Off;

    /// <summary>
    /// Requirement strings the notebook declares. Held here rather than read from the options the
    /// session captured, because a notebook's saved settings can arrive after the kernel has
    /// already initialized.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; private set; }

    /// <summary>
    /// Installer arguments appended to every install. Exists so a test can point the installer
    /// at a local wheel directory instead of an index.
    /// </summary>
    internal IReadOnlyList<string> ExtraInstallArguments { get; set; } = Array.Empty<string>();

    /// <summary>Overrides tool selection for the tests; null probes for uv.</summary>
    internal bool? UseUvOverride { get; set; }

    /// <summary>Picks up a settings change without rebuilding the session.</summary>
    public void UpdateOptions(PythonKernelOptions options)
    {
        _policy = options.AutoInstall;
        _userMap = options.ImportMap;
        _useUv = options.UseUv;
        _hideInstallOutput = options.HideInstallOutput;
        Dependencies = options.Dependencies;
    }

    /// <summary>
    /// Whether the cell is worth scanning at all. A cell with no import statement and no
    /// declared requirements cannot need anything installed, and the round trip is not free.
    /// </summary>
    public bool ShouldScan(string code, IReadOnlyList<string> requirements)
    {
        if (!Enabled)
            return false;

        if (requirements.Count > 0)
            return true;

        return code is not null
            && code.Contains("import", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolve, ask, and install. Returns the distributions that were installed, empty when
    /// nothing was or when the user declined.
    /// </summary>
    /// <param name="scan">What the interpreter reported it cannot satisfy.</param>
    /// <param name="interpreter">The interpreter whose environment receives the packages.</param>
    /// <param name="environment">A description of that environment, shown in the prompt.</param>
    /// <param name="context">Where prompts and installer output go.</param>
    public async Task<IReadOnlyList<string>> EnsureAsync(
        ImportScan scan,
        string interpreter,
        string environment,
        IVersoContext context)
    {
        if (!Enabled || scan.IsEmpty)
            return Array.Empty<string>();

        var refused = new List<(string Requirement, RequirementRefusal Reason)>();

        var candidates = new List<ResolvedImport>();
        foreach (var missing in scan.Missing)
        {
            // A guarded import has already said it can cope with the module being absent, so
            // installing it would be answering a question the cell did not ask.
            if (missing.Optional)
                continue;

            if (_declined.Contains(missing.Module))
                continue;

            var resolved = ImportMap.Resolve(missing.Module, _userMap);

            // A configured mapping lands in the same argument position a requirement does, so it
            // is held to the same rule.
            var mappedRefusal = RequirementGuard.Classify(resolved.Distribution);
            if (mappedRefusal != RequirementRefusal.None)
            {
                Refuse(resolved.Distribution, missing.Module, mappedRefusal, refused);
                continue;
            }

            candidates.Add(resolved);
        }

        var declared = new List<string>();
        foreach (var requirement in scan.Unsatisfied)
        {
            if (_declined.Contains(requirement) || _ensured.Contains(requirement))
                continue;

            // The notebook is the one place these strings come from, and it is not necessarily
            // written by whoever is running it.
            var refusal = RequirementGuard.Classify(requirement);
            if (refusal != RequirementRefusal.None)
            {
                Refuse(requirement, null, refusal, refused);
                continue;
            }

            declared.Add(requirement);
        }

        await ReportRefusedAsync(refused, context).ConfigureAwait(false);

        if (candidates.Count == 0 && declared.Count == 0)
            return Array.Empty<string>();

        return _policy == AutoInstallPolicy.Auto
            ? await InstallAutomaticallyAsync(candidates, declared, interpreter, environment, context)
                .ConfigureAwait(false)
            : await InstallWithConsentAsync(candidates, declared, interpreter, environment, context)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolve and install a single module reached by a dynamic import, which no scan can see.
    /// Returns the distribution that was installed, or null.
    /// </summary>
    public async Task<string?> EnsureModuleAsync(
        string module,
        string interpreter,
        string environment,
        IVersoContext context)
    {
        if (!Enabled)
            return null;

        var root = ImportMap.RootOf(module);
        if (root.Length == 0 || _declined.Contains(root))
            return null;

        var scan = new ImportScan(new[] { new ScannedImport(root, Optional: false) }, Array.Empty<string>());
        var installed = await EnsureAsync(scan, interpreter, environment, context).ConfigureAwait(false);
        return installed.Count > 0 ? installed[0] : null;
    }

    // --- policy paths ---

    /// <summary>
    /// Under <c>Auto</c> a known mapping installs without asking, and a name that is only a
    /// guess does not. The guess is exactly the typosquatting case: a misspelled import
    /// resolves to a plausible distribution name that somebody may well have published.
    /// </summary>
    private async Task<IReadOnlyList<string>> InstallAutomaticallyAsync(
        List<ResolvedImport> candidates,
        List<string> declared,
        string interpreter,
        string environment,
        IVersoContext context)
    {
        var specs = new List<string>();
        specs.AddRange(candidates.Where(c => c.Vetted).Select(c => c.Distribution));
        specs.AddRange(declared);

        foreach (var guess in candidates.Where(c => !c.Vetted))
        {
            await WriteAsync(context,
                $"'{guess.Module}' is not installed and Verso does not know which distribution " +
                "provides it. Automatic installs only cover known packages; install it with " +
                $"#!pip {guess.Module} if that is the right name.")
                .ConfigureAwait(false);

            // The decision is made for this session. Without recording it the import failure
            // reaches the backstop moments later and says the same thing a second time.
            _declined.Add(guess.Module);
        }

        if (specs.Count == 0)
            return Array.Empty<string>();

        await WriteAsync(context, $"Installing {Join(specs)} into {environment}.").ConfigureAwait(false);
        return await RunAsync(specs, declared, interpreter, context).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> InstallWithConsentAsync(
        List<ResolvedImport> candidates,
        List<string> declared,
        string interpreter,
        string environment,
        IVersoContext context)
    {
        var request = new List<ExtensionConsentInfo>();

        foreach (var candidate in candidates)
        {
            request.Add(new ExtensionConsentInfo(
                candidate.Distribution,
                Version: null,
                Source: $"import {candidate.Module}")
            {
                Kind = ConsentKind.Package,
                Target = environment,
            });
        }

        foreach (var requirement in declared)
        {
            request.Add(new ExtensionConsentInfo(
                requirement,
                Version: null,
                Source: "declared dependency")
            {
                Kind = ConsentKind.Package,
                Target = environment,
            });
        }

        bool approved;
        try
        {
            approved = await context.ExtensionHost
                .RequestExtensionConsentAsync(request, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A host that cannot ask has not approved. The cell runs and fails at the import,
            // which is the behavior without this feature at all.
            approved = false;
        }

        if (!approved)
        {
            // Remembered for the session, so running the same cell again does not ask a
            // question the user has already answered.
            foreach (var candidate in candidates)
                _declined.Add(candidate.Module);
            foreach (var requirement in declared)
                _declined.Add(requirement);

            return Array.Empty<string>();
        }

        var specs = candidates.Select(c => c.Distribution).Concat(declared).ToList();
        await WriteAsync(context, $"Installing {Join(specs)} into {environment}.").ConfigureAwait(false);
        return await RunAsync(specs, declared, interpreter, context).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> RunAsync(
        List<string> specs,
        List<string> declared,
        string interpreter,
        IVersoContext context)
    {
        var arguments = specs.Concat(ExtraInstallArguments).ToList();

        // Off means pip outright; Auto leaves the choice to the uv probe.
        var useUv = UseUvOverride;
        if (useUv is null && _useUv == UvUsage.Off)
            useUv = false;

        var succeeded = await PackageInstaller
            .InstallAsync(interpreter, arguments, context, useUv, detail: !_hideInstallOutput)
            .ConfigureAwait(false);

        if (!succeeded)
        {
            // Not recorded as declined: a failed install is worth retrying, unlike a refusal.
            return Array.Empty<string>();
        }

        // A requirement that is satisfied now does not need checking again this session.
        foreach (var requirement in declared)
            _ensured.Add(requirement);

        // What was installed is reported by the installer, which knows the versions it settled on.
        return specs;
    }

    /// <summary>
    /// Record a requirement that named an installer option rather than a package. The decision is
    /// remembered so a cell that carries one does not say the same thing on every run.
    /// </summary>
    /// <param name="requirement">The string that was refused.</param>
    /// <param name="module">The import it was resolved from, when it came from a mapping.</param>
    /// <param name="reason">Which rule it fell foul of, which decides what the report says.</param>
    /// <param name="refused">Collects what to report once the whole request has been examined.</param>
    private void Refuse(
        string requirement,
        string? module,
        RequirementRefusal reason,
        List<(string Requirement, RequirementRefusal Reason)> refused)
    {
        refused.Add((requirement, reason));
        _declined.Add(module ?? requirement);
    }

    private static async Task ReportRefusedAsync(
        IReadOnlyList<(string Requirement, RequirementRefusal Reason)> refused, IVersoContext context)
    {
        if (refused.Count == 0)
            return;

        // Said plainly, because the person reading it did not necessarily write the notebook, and
        // separated by reason, because the two say different things about what the entry was doing.
        var options = Select(refused, RequirementRefusal.InstallerOption);
        if (options.Count > 0)
        {
            await WriteAsync(context,
                $"Not installed: {Join(options)}. A dependency has to name a package; these are " +
                "installer options, which a notebook is not allowed to pass. Remove them, or run " +
                "the install yourself with #!pip if you meant them.")
                .ConfigureAwait(false);
        }

        var sources = Select(refused, RequirementRefusal.ExternalSource);
        if (sources.Count > 0)
        {
            await WriteAsync(context,
                $"Not installed: {Join(sources)}. A dependency has to name a package; these name " +
                "somewhere to install from, which would have fetched code from a location the " +
                "notebook chose rather than from the configured index. Name the package instead, " +
                "or run the install yourself with #!pip if you meant them.")
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<string> Select(
        IReadOnlyList<(string Requirement, RequirementRefusal Reason)> refused, RequirementRefusal reason)
        => refused.Where(entry => entry.Reason == reason).Select(entry => entry.Requirement).ToList();

    private static string Join(IReadOnlyList<string> values) => string.Join(", ", values);

    private static Task WriteAsync(IVersoContext context, string message)
        => context.WriteOutputAsync(new CellOutput("text/plain", message));
}
