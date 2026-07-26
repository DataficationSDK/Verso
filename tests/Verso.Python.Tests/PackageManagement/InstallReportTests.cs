using Verso.Python.PackageManagement;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers what a cell says about an install. The sample output in here was taken from real runs
/// of pip and of uv rather than written by hand, because the whole point of the summary is that
/// it agrees with what the tool actually printed.
/// </summary>
[TestClass]
public sealed class InstallReportTests
{
    // pip 25.1.1 installing requests into an empty virtual environment.
    private static readonly string[] PipFresh =
    {
        "Collecting requests",
        "  Downloading requests-2.34.2-py3-none-any.whl.metadata (4.8 kB)",
        "Collecting charset_normalizer<4,>=2 (from requests)",
        "  Downloading charset_normalizer-3.4.9-cp313-cp313-macosx_10_13_universal2.whl.metadata (41 kB)",
        "Collecting idna<4,>=2.5 (from requests)",
        "  Downloading idna-3.18-py3-none-any.whl.metadata (6.1 kB)",
        "Collecting urllib3<3,>=1.26 (from requests)",
        "  Downloading urllib3-2.7.0-py3-none-any.whl.metadata (6.9 kB)",
        "Collecting certifi>=2023.5.7 (from requests)",
        "  Downloading certifi-2026.7.22-py3-none-any.whl.metadata (2.5 kB)",
        "Downloading requests-2.34.2-py3-none-any.whl (73 kB)",
        "Downloading charset_normalizer-3.4.9-cp313-cp313-macosx_10_13_universal2.whl (317 kB)",
        "Downloading idna-3.18-py3-none-any.whl (65 kB)",
        "Downloading urllib3-2.7.0-py3-none-any.whl (131 kB)",
        "Downloading certifi-2026.7.22-py3-none-any.whl (136 kB)",
        "Installing collected packages: urllib3, idna, charset_normalizer, certifi, requests",
        "Successfully installed certifi-2026.7.22 charset_normalizer-3.4.9 idna-3.18 requests-2.34.2 urllib3-2.7.0",
        "[notice] A new release of pip is available: 25.1.1 -> 26.1.2",
        "[notice] To update, run: /env/bin/python -m pip install --upgrade pip",
    };

    // The same command run a second time.
    private static readonly string[] PipSatisfied =
    {
        "Requirement already satisfied: requests in ./env/lib/python3.13/site-packages (2.34.2)",
        "Requirement already satisfied: charset_normalizer<4,>=2 in ./env/lib/python3.13/site-packages (from requests) (3.4.9)",
        "Requirement already satisfied: idna<4,>=2.5 in ./env/lib/python3.13/site-packages (from requests) (3.18)",
        "Requirement already satisfied: urllib3<3,>=1.26 in ./env/lib/python3.13/site-packages (from requests) (2.7.0)",
        "Requirement already satisfied: certifi>=2023.5.7 in ./env/lib/python3.13/site-packages (from requests) (2026.7.22)",
    };

    // uv 0.9 installing the same package.
    private static readonly string[] UvFresh =
    {
        "Using Python 3.13.3 environment at: target",
        "Resolved 5 packages in 187ms",
        "Prepared 5 packages in 77ms",
        "Installed 5 packages in 6ms",
        " + certifi==2026.7.22",
        " + charset-normalizer==3.4.9",
        " + idna==3.18",
        " + requests==2.34.2",
        " + urllib3==2.7.0",
    };

    private static readonly string[] Requested = { "requests" };

    private static string Summary(string[] output, bool detail = false, string[]? requested = null)
        => string.Join("\n", InstallReport.Parse(output).Summarize(requested ?? Requested, detail));

    // --- what the cell says by default ---

    [TestMethod]
    public void APipInstallIsOneLineNamingWhatWasAskedForAndCountingTheRest()
    {
        Assert.AreEqual("Installed requests 2.34.2 and 4 dependencies.", Summary(PipFresh));
    }

    [TestMethod]
    public void AUvInstallSaysTheSameThing()
    {
        // Which tool ran is not something the person reading the cell chose or should have to see.
        Assert.AreEqual("Installed requests 2.34.2 and 4 dependencies.", Summary(UvFresh));
    }

    [TestMethod]
    public void ASinglePackageWithNoDependenciesCountsNothing()
    {
        var output = new[] { "Successfully installed six-1.17.0" };

        Assert.AreEqual("Installed six 1.17.0.", Summary(output, requested: new[] { "six" }));
    }

    [TestMethod]
    public void OneDependencyIsNotCalledOneDependencies()
    {
        var output = new[] { "Successfully installed requests-2.34.2 idna-3.18" };

        Assert.AreEqual("Installed requests 2.34.2 and 1 dependency.", Summary(output));
    }

    [TestMethod]
    public void AnInstallThatHadNothingToDoSaysSo()
    {
        Assert.AreEqual("requests 2.34.2 is already installed.", Summary(PipSatisfied));
    }

    [TestMethod]
    public void UvSayingItCheckedTheEnvironmentIsUnderstoodToo()
    {
        // uv names nothing when the environment already satisfies the request.
        var output = new[] { "Using Python 3.13.3 environment at: target", "Checked 1 package in 2ms" };

        Assert.AreEqual("Everything requested is already installed.", Summary(output));
    }

    [TestMethod]
    public void TheNarrationIsGone()
    {
        var summary = Summary(PipFresh);

        Assert.IsFalse(summary.Contains("Collecting"), "the per-package narration is the thing being removed");
        Assert.IsFalse(summary.Contains("Downloading"));
        Assert.IsFalse(summary.Contains("whl"));
    }

    [TestMethod]
    public void PipsOwnUpgradeNoticeIsNotTheNotebooksBusiness()
    {
        var report = InstallReport.Parse(PipFresh);

        Assert.IsFalse(report.Output.Any(line => line.Contains("[notice]")));
    }

    // --- what showing the detail adds ---

    [TestMethod]
    public void ShowingTheDetailNamesTheDependenciesAndTheirVersions()
    {
        var summary = Summary(PipFresh, detail: true);

        StringAssert.Contains(summary, "Installed requests 2.34.2 and 4 dependencies.");
        StringAssert.Contains(summary,
            "Dependencies: certifi 2026.7.22, charset_normalizer 3.4.9, idna 3.18, urllib3 2.7.0.");
    }

    [TestMethod]
    public void ShowingTheDetailIsStillOnlyTwoLines()
    {
        // Twenty-four packages is twenty-four names, not the hundred-odd lines it took to get them.
        var lines = InstallReport.Parse(PipFresh).Summarize(Requested, detail: true);

        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void ShowingTheDetailNamesWhatWasAlreadyThere()
    {
        var output = new[]
        {
            "Requirement already satisfied: numpy in /env/lib/python3.13/site-packages (from k3d) (2.5.1)",
            "Successfully installed k3d-2.17.0 msgpack-1.2.1",
        };

        var summary = Summary(output, detail: true, requested: new[] { "k3d" });

        StringAssert.Contains(summary, "Installed k3d 2.17.0 and 1 dependency.");
        StringAssert.Contains(summary, "Already present: numpy 2.5.1.");
    }

    [TestMethod]
    public void AWarningIsHeldBackUntilTheDetailIsAskedFor()
    {
        var output = new[]
        {
            "WARNING: The script f2py is installed in '/env/bin' which is not on PATH.",
            "Successfully installed numpy-2.5.1",
        };

        Assert.IsFalse(Summary(output, requested: new[] { "numpy" }).Contains("WARNING"));
        StringAssert.Contains(Summary(output, detail: true, requested: new[] { "numpy" }), "WARNING");
    }

    // --- what is never hidden ---

    [TestMethod]
    public void ADependencyConflictIsReportedWhateverTheSettingSays()
    {
        // pip reports a broken environment this way and still exits successfully, so the summary
        // is the only place it can be seen.
        var output = new[]
        {
            "Successfully installed urllib3-2.7.0",
            "ERROR: pip's dependency resolver does not currently take into account all the packages that are installed.",
        };

        StringAssert.Contains(Summary(output, requested: new[] { "urllib3" }), "dependency resolver");
    }

    // --- output that says nothing this understands ---

    [TestMethod]
    public void AnInstallerThatReportsNothingRecognizableIsNotSummarized()
    {
        var report = InstallReport.Parse(new[] { "some future installer did something" });

        Assert.IsFalse(report.Recognized,
            "the caller relays the output rather than inventing a summary of a run it cannot read");
    }

    [TestMethod]
    public void AnUnreadableRunStillNamesWhatWasAskedFor()
    {
        var summary = Summary(new[] { "some future installer did something" });

        Assert.AreEqual("Installed requests.", summary);
    }

    // --- telling what was wanted from what came with it ---

    [TestMethod]
    public void AVersionConstraintStillMatchesTheDistributionItNames()
    {
        var output = new[] { "Successfully installed pandas-2.3.1 numpy-2.5.1" };

        Assert.AreEqual(
            "Installed pandas 2.3.1 and 1 dependency.",
            Summary(output, requested: new[] { "pandas>=2,<3" }));
    }

    [TestMethod]
    public void AnUnderscoreAndAHyphenNameTheSameDistribution()
    {
        // pip reports charset_normalizer for a package published as charset-normalizer.
        var output = new[] { "Successfully installed charset_normalizer-3.4.9" };

        Assert.AreEqual(
            "Installed charset_normalizer 3.4.9.",
            Summary(output, requested: new[] { "charset-normalizer" }));
    }

    [TestMethod]
    public void AnInstallerOptionIsNotMistakenForAPackage()
    {
        var output = new[] { "Successfully installed requests-2.34.2" };

        Assert.AreEqual(
            "Installed requests 2.34.2.",
            Summary(output, requested: new[] { "requests", "--upgrade" }));
    }

    [TestMethod]
    public void AnInstallNobodyNamedAPackageForIsStillCounted()
    {
        // What a requirements file passed through with -r looks like from here.
        var output = new[] { "Successfully installed requests-2.34.2 idna-3.18" };

        Assert.AreEqual("Installed 2 packages.", Summary(output, requested: new[] { "-r", "req.txt" }));
    }

    [TestMethod]
    public void ADistributionWhoseNameContainsAHyphenIsNotSplitInTheWrongPlace()
    {
        var output = new[] { "Successfully installed backports.zoneinfo-0.2.1 typing-extensions-4.15.0" };

        Assert.AreEqual(
            "Installed typing-extensions 4.15.0 and 1 dependency.",
            Summary(output, requested: new[] { "typing-extensions" }));
    }

    // --- reading the parts back out ---

    [TestMethod]
    public void EveryDistributionAndVersionIsRead()
    {
        var report = InstallReport.Parse(PipFresh);

        CollectionAssert.AreEqual(
            new[] { "certifi 2026.7.22", "charset_normalizer 3.4.9", "idna 3.18", "requests 2.34.2", "urllib3 2.7.0" },
            report.Installed.Select(p => p.ToString()).ToArray());
    }

    [TestMethod]
    public void AVersionInUseIsReadPastTheNameThatAskedForIt()
    {
        // The line carries both "(from requests)" and the version, in that order.
        var report = InstallReport.Parse(PipSatisfied);

        CollectionAssert.Contains(report.AlreadyPresent.Select(p => p.ToString()).ToArray(), "idna 3.18");
    }

    [TestMethod]
    public void AReplacedDistributionIsReportedAtTheVersionItEndedUpAt()
    {
        // uv prints the removal and the addition as a pair.
        var output = new[] { "Installed 1 package in 4ms", " - requests==2.30.0", " + requests==2.34.2" };

        Assert.AreEqual("Installed requests 2.34.2.", Summary(output));
    }
}
