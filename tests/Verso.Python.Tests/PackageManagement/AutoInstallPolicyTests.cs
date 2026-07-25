using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Python.PackageManagement;
using Verso.Stubs;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers what each policy is allowed to do, and what it asks first. No install runs here: the
/// installer is pointed at an executable that does not exist, so the decisions are observable
/// without touching a package index. Whether an install works is the integration tests' subject.
/// </summary>
[TestClass]
public sealed class AutoInstallPolicyTests
{
    private const string Interpreter = "/verso/tests/no/such/python";
    private const string Environment = "Python 3.13.3 (virtual environment) at " + Interpreter;

    private StubExecutionContext _context = null!;
    private StubExtensionHostContext _host = null!;
    private List<IReadOnlyList<ExtensionConsentInfo>> _requests = null!;
    private bool _approve;

    [TestInitialize]
    public void Setup()
    {
        _requests = new List<IReadOnlyList<ExtensionConsentInfo>>();
        _approve = false;

        _host = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>())
        {
            ConsentHandler = (extensions, _) =>
            {
                _requests.Add(extensions);
                return Task.FromResult(_approve);
            },
        };

        _context = new StubExecutionContext { ExtensionHost = _host };
    }

    private static AutoInstallService Build(
        AutoInstallPolicy policy,
        IReadOnlyDictionary<string, string>? importMap = null,
        IReadOnlyList<string>? dependencies = null)
        => new(new PythonKernelOptions
        {
            AutoInstall = policy,
            ImportMap = importMap ?? new Dictionary<string, string>(),
            Dependencies = dependencies ?? Array.Empty<string>(),
        })
        {
            // Pinned to pip so an install attempt runs the interpreter above, which does not
            // exist and therefore cannot reach a package index. Left to probe for uv, a machine
            // that has it would resolve against the real index from a unit test.
            UseUvOverride = false,
        };

    private static ImportScan Missing(string module, bool optional = false)
        => new(new[] { new ScannedImport(module, optional) }, Array.Empty<string>());

    private Task<IReadOnlyList<string>> EnsureAsync(AutoInstallService service, ImportScan scan)
        => service.EnsureAsync(scan, Interpreter, Environment, _context);

    private string Output => string.Join("\n", _context.WrittenOutputs.Select(o => o.Content));

    // --- Off ---

    [TestMethod]
    public void Off_DoesNotEvenScan()
    {
        var service = Build(AutoInstallPolicy.Off);

        Assert.IsFalse(service.Enabled);
        Assert.IsFalse(service.ShouldScan("import pandas", Array.Empty<string>()));
    }

    [TestMethod]
    public async Task Off_InstallsNothingAndAsksNothing()
    {
        var service = Build(AutoInstallPolicy.Off);

        var installed = await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(0, installed.Count);
        Assert.AreEqual(0, _requests.Count);
    }

    // --- what is worth scanning ---

    [TestMethod]
    public void Scan_IsSkippedForACellWithNoImport()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        Assert.IsFalse(service.ShouldScan("x = 1 + 2", Array.Empty<string>()));
    }

    [TestMethod]
    public void Scan_RunsForACellWithAnImport()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        Assert.IsTrue(service.ShouldScan("import pandas as pd", Array.Empty<string>()));
    }

    [TestMethod]
    public void Scan_RunsForDeclaredRequirementsEvenWithoutAnImport()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        Assert.IsTrue(service.ShouldScan("x = 1", new[] { "httpx" }));
    }

    // --- Prompt ---

    [TestMethod]
    public async Task Prompt_AsksWithTheDistributionNameAndTheEnvironment()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(1, _requests.Count);
        var item = _requests[0].Single();
        Assert.AreEqual("opencv-python", item.PackageId);
        Assert.AreEqual(ConsentKind.Package, item.Kind);
        Assert.AreEqual(Environment, item.Target);
        Assert.AreEqual("import cv2", item.Source);
    }

    [TestMethod]
    public async Task Prompt_InstallsNothingWhenDeclined()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        var installed = await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(0, installed.Count);
    }

    [TestMethod]
    public async Task Prompt_DoesNotAskTwiceForAModuleAlreadyDeclined()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await EnsureAsync(service, Missing("cv2"));
        await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(1, _requests.Count,
            "Running the same cell again must not re-ask a question already answered.");
    }

    [TestMethod]
    public async Task Prompt_AsksForAGuessedNameToo()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await EnsureAsync(service, Missing("pandas"));

        Assert.AreEqual("pandas", _requests.Single().Single().PackageId);
    }

    [TestMethod]
    public async Task Prompt_AsksAboutEveryMissingModuleAtOnce()
    {
        var service = Build(AutoInstallPolicy.Prompt);
        var scan = new ImportScan(
            new[] { new ScannedImport("cv2", false), new ScannedImport("sklearn", false) },
            Array.Empty<string>());

        await EnsureAsync(service, scan);

        CollectionAssert.AreEquivalent(
            new[] { "opencv-python", "scikit-learn" },
            _requests.Single().Select(i => i.PackageId).ToArray());
    }

    [TestMethod]
    public async Task Prompt_TreatsAHostThatCannotAskAsRefusal()
    {
        _host.ConsentHandler = (_, _) => throw new NotSupportedException();
        var service = Build(AutoInstallPolicy.Prompt);

        var installed = await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(0, installed.Count);
    }

    // --- Auto ---

    [TestMethod]
    public async Task Auto_DoesNotAskAboutAKnownName()
    {
        var service = Build(AutoInstallPolicy.Auto);

        await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(0, _requests.Count);
        StringAssert.Contains(Output, "opencv-python");
    }

    [TestMethod]
    public async Task Auto_RefusesToInstallAGuessedName()
    {
        var service = Build(AutoInstallPolicy.Auto);

        var installed = await EnsureAsync(service, Missing("pandsa"));

        Assert.AreEqual(0, installed.Count);
        Assert.AreEqual(0, _requests.Count, "Auto never prompts, in either direction.");
        StringAssert.Contains(Output, "pandsa");
        StringAssert.Contains(Output, "#!pip");
    }

    [TestMethod]
    public async Task Auto_AcceptsAConfiguredNameAsKnown()
    {
        var service = Build(
            AutoInstallPolicy.Auto,
            importMap: new Dictionary<string, string> { ["pandsa"] = "acme-pandsa" });

        await EnsureAsync(service, Missing("pandsa"));

        StringAssert.Contains(Output, "acme-pandsa");
        Assert.IsFalse(Output.Contains("#!pip"), "A configured mapping is not a guess.");
    }

    [TestMethod]
    public async Task Auto_ReportsAGuessOnlyOncePerSession()
    {
        // Otherwise the import failure reaches the backstop moments later and repeats it.
        var service = Build(AutoInstallPolicy.Auto);

        await EnsureAsync(service, Missing("pandsa"));
        await service.EnsureModuleAsync("pandsa", Interpreter, Environment, _context);

        Assert.AreEqual(1, CountOccurrences(Output, "does not know which distribution"));
    }

    [TestMethod]
    public async Task Auto_StillInstallsTheKnownNamesAlongsideAGuess()
    {
        var service = Build(AutoInstallPolicy.Auto);
        var scan = new ImportScan(
            new[] { new ScannedImport("cv2", false), new ScannedImport("pandsa", false) },
            Array.Empty<string>());

        await EnsureAsync(service, scan);

        StringAssert.Contains(Output, "opencv-python");
        StringAssert.Contains(Output, "pandsa");
    }

    // --- guarded imports ---

    [TestMethod]
    public async Task AGuardedImportIsNeverInstalled()
    {
        // The cell has already said it can run without the module.
        var service = Build(AutoInstallPolicy.Prompt);

        var installed = await EnsureAsync(service, Missing("cv2", optional: true));

        Assert.AreEqual(0, installed.Count);
        Assert.AreEqual(0, _requests.Count);
    }

    // --- declared requirements ---

    [TestMethod]
    public async Task ADeclaredRequirementIsAskedAboutByItsOwnName()
    {
        var service = Build(AutoInstallPolicy.Prompt);
        var scan = new ImportScan(Array.Empty<ScannedImport>(), new[] { "pandas>=2" });

        await EnsureAsync(service, scan);

        var item = _requests.Single().Single();
        Assert.AreEqual("pandas>=2", item.PackageId);
        Assert.AreEqual("declared dependency", item.Source);
    }

    [TestMethod]
    public async Task ADeclaredRequirementIsNotAskedAboutTwiceAfterARefusal()
    {
        var service = Build(AutoInstallPolicy.Prompt);
        var scan = new ImportScan(Array.Empty<ScannedImport>(), new[] { "pandas>=2" });

        await EnsureAsync(service, scan);
        await EnsureAsync(service, scan);

        Assert.AreEqual(1, _requests.Count);
    }

    [TestMethod]
    public async Task NothingIsAskedWhenTheScanFoundNothing()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        var installed = await EnsureAsync(service, ImportScan.Empty);

        Assert.AreEqual(0, installed.Count);
        Assert.AreEqual(0, _requests.Count);
    }

    // --- settings changes ---

    [TestMethod]
    public void Dependencies_FollowASettingsChange()
    {
        var service = Build(AutoInstallPolicy.Prompt);
        Assert.AreEqual(0, service.Dependencies.Count);

        service.UpdateOptions(new PythonKernelOptions { Dependencies = new[] { "httpx" } });

        CollectionAssert.AreEqual(new[] { "httpx" }, service.Dependencies.ToArray());
    }

    [TestMethod]
    public void Policy_FollowsASettingsChange()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        service.UpdateOptions(new PythonKernelOptions { AutoInstall = AutoInstallPolicy.Off });

        Assert.IsFalse(service.Enabled);
    }

    // --- the dynamic-import backstop ---

    [TestMethod]
    public async Task ADynamicImportResolvesThroughTheSameTable()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await service.EnsureModuleAsync("cv2", Interpreter, Environment, _context);

        Assert.AreEqual("opencv-python", _requests.Single().Single().PackageId);
    }

    [TestMethod]
    public async Task ADynamicImportReportsTheRootOfADottedName()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await service.EnsureModuleAsync("PIL.Image", Interpreter, Environment, _context);

        Assert.AreEqual("Pillow", _requests.Single().Single().PackageId);
    }

    [TestMethod]
    public async Task ADynamicImportIsNotAskedAboutAfterARefusal()
    {
        var service = Build(AutoInstallPolicy.Prompt);

        await service.EnsureModuleAsync("cv2", Interpreter, Environment, _context);
        await service.EnsureModuleAsync("cv2", Interpreter, Environment, _context);

        Assert.AreEqual(1, _requests.Count);
    }

    [TestMethod]
    public async Task ADynamicImportInstallsNothingWhenThePolicyIsOff()
    {
        var service = Build(AutoInstallPolicy.Off);

        var installed = await service.EnsureModuleAsync("cv2", Interpreter, Environment, _context);

        Assert.IsNull(installed);
    }

    // --- a failed install ---

    [TestMethod]
    public async Task AFailedInstallIsNotRememberedAsARefusal()
    {
        // The installer cannot start, so this exercises the failure path without a network.
        _approve = true;
        var service = Build(AutoInstallPolicy.Prompt);

        var first = await EnsureAsync(service, Missing("cv2"));
        var second = await EnsureAsync(service, Missing("cv2"));

        Assert.AreEqual(0, first.Count);
        Assert.AreEqual(0, second.Count);
        Assert.AreEqual(2, _requests.Count, "A failed install is worth retrying, unlike a refusal.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
