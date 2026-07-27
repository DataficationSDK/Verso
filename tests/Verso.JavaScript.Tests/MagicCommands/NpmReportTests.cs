using Verso.JavaScript.MagicCommands;

namespace Verso.JavaScript.Tests.MagicCommands;

/// <summary>
/// Covers what a cell says about an npm install. The sample reports in here were taken from real
/// runs of npm rather than written by hand, because the whole point of the summary is that it
/// agrees with what npm actually reported.
/// </summary>
[TestClass]
public sealed class NpmReportTests
{
    // npm 11.7.0 installing express, abridged to four of the sixty-seven packages it added.
    private const string ExpressJson = @"{
        ""add"": [
            { ""name"": ""express"", ""version"": ""5.1.0"", ""path"": ""/n/express"" },
            { ""name"": ""accepts"", ""version"": ""2.0.0"", ""path"": ""/n/accepts"" },
            { ""name"": ""body-parser"", ""version"": ""2.2.0"", ""path"": ""/n/body-parser"" },
            { ""name"": ""cookie"", ""version"": ""0.7.2"", ""path"": ""/n/cookie"" }
        ],
        ""added"": 4, ""audited"": 5, ""change"": [], ""changed"": 0,
        ""funding"": 26, ""remove"": [], ""removed"": 0,
        ""audit"": {
            ""vulnerabilities"": {
                ""info"": 0, ""low"": 0, ""moderate"": 0, ""high"": 0, ""critical"": 0, ""total"": 0
            }
        }
    }";

    // The same command run a second time.
    private const string UpToDateJson = @"{
        ""add"": [], ""added"": 0, ""audited"": 2, ""change"": [], ""changed"": 0,
        ""funding"": 0, ""remove"": [], ""removed"": 0,
        ""audit"": {
            ""vulnerabilities"": {
                ""info"": 0, ""low"": 0, ""moderate"": 0, ""high"": 0, ""critical"": 0, ""total"": 0
            }
        }
    }";

    // Installing a package whose dependency tree carries known advisories.
    private const string VulnerableJson = @"{
        ""add"": [
            { ""name"": ""request"", ""version"": ""2.88.2"" },
            { ""name"": ""uuid"", ""version"": ""3.4.0"" }
        ],
        ""added"": 2, ""audited"": 116, ""funding"": 29, ""removed"": 0,
        ""audit"": {
            ""vulnerabilities"": {
                ""info"": 0, ""low"": 0, ""moderate"": 3, ""high"": 0, ""critical"": 2, ""total"": 5
            }
        }
    }";

    private const string DeprecationWarnings =
        "npm warn deprecated har-validator@5.1.5: this library is no longer supported\n" +
        "npm warn deprecated request@2.88.2: request has been deprecated, see " +
        "https://github.com/request/request/issues/3142";

    private static string Summary(
        string json, bool detail = false, string[]? requested = null, string stderr = "")
        => string.Join("\n", NpmReport.Parse(json, stderr).Summarize(requested ?? new[] { "express" }, detail));

    // --- what the cell says by default ---

    [TestMethod]
    public void AnInstallIsOneLineNamingWhatWasAskedForAndCountingTheRest()
    {
        Assert.AreEqual("Installed express 5.1.0 and 3 dependencies.", Summary(ExpressJson));
    }

    [TestMethod]
    public void ThePackageCountsAndFundingFooterAreGone()
    {
        var summary = Summary(ExpressJson);

        Assert.IsFalse(summary.Contains("audited"), "npm's counts say nothing the cell wanted to know");
        Assert.IsFalse(summary.Contains("funding"));
        Assert.IsFalse(summary.Contains("vulnerabilit"));
    }

    [TestMethod]
    public void ARunThatAddedNothingSaysSo()
    {
        Assert.AreEqual("Everything requested is already installed.", Summary(UpToDateJson));
    }

    [TestMethod]
    public void OneDependencyIsNotCalledOneDependencies()
    {
        var json = @"{ ""add"": [
            { ""name"": ""express"", ""version"": ""5.1.0"" },
            { ""name"": ""cookie"", ""version"": ""0.7.2"" }
        ] }";

        Assert.AreEqual("Installed express 5.1.0 and 1 dependency.", Summary(json));
    }

    [TestMethod]
    public void AScopedPackageIsNamedInFull()
    {
        var json = @"{ ""add"": [ { ""name"": ""@sindresorhus/is"", ""version"": ""8.1.0"" } ] }";

        Assert.AreEqual(
            "Installed @sindresorhus/is 8.1.0.",
            Summary(json, requested: new[] { "@sindresorhus/is" }));
    }

    // --- what showing the detail adds ---

    [TestMethod]
    public void ShowingTheDetailNamesTheDependenciesAndTheirVersions()
    {
        var summary = Summary(ExpressJson, detail: true);

        StringAssert.Contains(summary, "Installed express 5.1.0 and 3 dependencies.");
        StringAssert.Contains(summary, "Dependencies: accepts 2.0.0, body-parser 2.2.0, cookie 0.7.2.");
    }

    [TestMethod]
    public void ShowingTheDetailIsStillOnlyTwoLines()
    {
        var lines = NpmReport.Parse(ExpressJson, "").Summarize(new[] { "express" }, detail: true);

        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void ADeprecationNoticeIsKeptRatherThanDiscarded()
    {
        // These arrive on npm's warning stream, which was thrown away entirely on a run that
        // succeeded, so the notice never reached anybody.
        var report = NpmReport.Parse(ExpressJson, DeprecationWarnings);

        Assert.AreEqual(2, report.Deprecations.Count);
        Assert.IsFalse(Summary(ExpressJson, stderr: DeprecationWarnings).Contains("deprecated"));
        StringAssert.Contains(
            Summary(ExpressJson, detail: true, stderr: DeprecationWarnings), "request has been deprecated");
    }

    [TestMethod]
    public void AReplacedPackageIsNamedOnlyInTheDetail()
    {
        var json = @"{
            ""add"": [ { ""name"": ""express"", ""version"": ""5.1.0"" } ],
            ""remove"": [ { ""name"": ""express"", ""version"": ""4.19.2"" } ]
        }";

        Assert.IsFalse(Summary(json).Contains("4.19.2"));
        StringAssert.Contains(Summary(json, detail: true), "Replaced: express 4.19.2.");
    }

    [TestMethod]
    public void APackageWrittenTwiceIsCountedOnce()
    {
        // Taken from a real yargs install: npm writes string-width at two depths of the tree and
        // reports each place it landed, so the same name and version arrives more than once.
        var json = @"{ ""add"": [
            { ""name"": ""yargs"", ""version"": ""18.1.0"" },
            { ""name"": ""string-width"", ""version"": ""7.2.0"" },
            { ""name"": ""string-width"", ""version"": ""8.2.2"" },
            { ""name"": ""string-width"", ""version"": ""7.2.0"" }
        ] }";

        var summary = Summary(json, detail: true, requested: new[] { "yargs" });

        StringAssert.Contains(summary, "Installed yargs 18.1.0 and 2 dependencies.");
        StringAssert.Contains(summary, "Dependencies: string-width 7.2.0, string-width 8.2.2.");
    }

    // --- what is never hidden ---

    [TestMethod]
    public void AVulnerabilityIsReportedWhateverTheSettingSays()
    {
        // npm reports this on a run that succeeded, so the summary is the only place it can be
        // seen, and what just landed on the machine is worth hearing about.
        var summary = Summary(VulnerableJson, requested: new[] { "request" });

        StringAssert.Contains(
            summary, "npm audit found 5 vulnerabilities (2 critical, 3 moderate) in the installed packages.");
        StringAssert.Contains(summary, "Run npm audit for details.");
    }

    [TestMethod]
    public void AVulnerabilityIsNotBlamedOnWhatWasJustInstalled()
    {
        // npm audits the whole install directory, so an advisory usually predates this run.
        var summary = Summary(VulnerableJson, requested: new[] { "request" });

        StringAssert.Contains(summary, "in the installed packages");
        Assert.IsFalse(summary.Contains("request has"), "the count says nothing about which package it is");
    }

    [TestMethod]
    public void ACleanAuditSaysNothingAtAll()
    {
        Assert.IsNull(NpmReport.Parse(ExpressJson, "").Vulnerabilities);
    }

    [TestMethod]
    public void OneVulnerabilityIsNotCalledOneVulnerabilities()
    {
        var json = @"{
            ""add"": [ { ""name"": ""express"", ""version"": ""5.1.0"" } ],
            ""audit"": { ""vulnerabilities"": { ""low"": 1, ""total"": 1 } }
        }";

        StringAssert.Contains(Summary(json), "npm audit found 1 vulnerability (1 low) in the");
    }

    [TestMethod]
    public void AFailureCarriesNpmsOwnAccountOfIt()
    {
        var json = @"{
            ""error"": {
                ""code"": ""E404"",
                ""summary"": ""Not Found - GET https://registry.npmjs.org/nope - Not found"",
                ""detail"": ""The requested resource 'nope@*' could not be found.""
            }
        }";

        var report = NpmReport.Parse(json, "");

        StringAssert.Contains(report.Error!, "Not Found");
        StringAssert.Contains(report.Error!, "could not be found");
    }

    // --- output that says nothing this understands ---

    [TestMethod]
    public void AnNpmTooOldToReportAsJsonIsNotSummarized()
    {
        var report = NpmReport.Parse("added 67 packages, and audited 68 packages in 2s", "");

        Assert.IsFalse(report.Understood,
            "the caller relays what npm printed rather than inventing a summary of a run it cannot read");
    }

    [TestMethod]
    public void AWarningStreamIsStillReadWhenTheReportIsNot()
    {
        var report = NpmReport.Parse("not json at all", DeprecationWarnings);

        Assert.IsFalse(report.Understood);
        Assert.AreEqual(2, report.Deprecations.Count);
    }

    // --- telling what was wanted from what came with it ---

    [TestMethod]
    public void AnInstallNobodyNamedAPackageForIsStillCounted()
    {
        var json = @"{ ""add"": [
            { ""name"": ""express"", ""version"": ""5.1.0"" },
            { ""name"": ""cookie"", ""version"": ""0.7.2"" }
        ] }";

        Assert.AreEqual("Installed 2 packages.", Summary(json, requested: Array.Empty<string>()));
    }

    [TestMethod]
    public void APackageNpmRenamedIsStillReportedAgainstWhatWasAskedFor()
    {
        // Installing by tarball or alias means nothing npm adds carries the requested spelling.
        var json = @"{ ""add"": [ { ""name"": ""left-pad"", ""version"": ""1.3.0"" } ] }";

        Assert.AreEqual(
            "Installed ./left-pad.tgz and 1 dependency.",
            Summary(json, requested: new[] { "./left-pad.tgz" }));
    }
}
