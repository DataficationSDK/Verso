using Verso.JavaScript.MagicCommands;

namespace Verso.JavaScript.Tests.MagicCommands;

/// <summary>
/// Covers how a specifier is read back into the package it names. Getting this wrong is not a
/// cosmetic matter: the name is what decides whether the install is skipped as unnecessary.
/// </summary>
[TestClass]
public sealed class NpmManagerTests
{
    [TestMethod]
    [DataRow("lodash", "lodash", DisplayName = "plain name")]
    [DataRow("lodash@4.17.21", "lodash", DisplayName = "pinned version")]
    [DataRow("express@^5", "express", DisplayName = "range")]
    [DataRow("@types/node", "@types/node", DisplayName = "scoped")]
    [DataRow("@types/node@20", "@types/node", DisplayName = "scoped and pinned")]
    [DataRow("@sindresorhus/is@8.1.0", "@sindresorhus/is", DisplayName = "scoped, real")]
    [DataRow("  lodash  ", "lodash", DisplayName = "padded")]
    public void APackageNameIsReadPastAnyVersion(string specifier, string expected)
    {
        Assert.AreEqual(expected, NpmManager.PackageName(specifier));
    }

    [TestMethod]
    public void AScopedPackageIsNotReducedToNothing()
    {
        // Splitting on the first @ leaves an empty name for a scoped package, and an empty name
        // resolves to node_modules itself, which exists. The install was then skipped as already
        // done and the package never arrived.
        Assert.AreNotEqual(string.Empty, NpmManager.PackageName("@types/node"));
        Assert.IsFalse(NpmManager.IsPackageInstalled(string.Empty));
        Assert.IsFalse(NpmManager.IsPackageInstalled("   "));
    }

    [TestMethod]
    public void AnEmptySpecifierNamesNothing()
    {
        Assert.AreEqual(string.Empty, NpmManager.PackageName(""));
        Assert.AreEqual(string.Empty, NpmManager.PackageName("   "));
    }
}
