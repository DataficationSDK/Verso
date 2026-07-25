using Verso.Abstractions;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Covers the consent dialog's wording for each kind of request. The same component serves both
/// front ends, so the extension wording has to keep reading correctly while the package wording
/// is added beside it.
/// </summary>
[TestClass]
public sealed class ExtensionConsentDialogTests : BunitTestContext
{
    private static ExtensionConsentInfo Extension(string packageId, string? version = null)
        => new(packageId, version);

    private static ExtensionConsentInfo Package(string distribution, string source, string target)
        => new(distribution, Version: null, source)
        {
            Kind = ConsentKind.Package,
            Target = target,
        };

    private IRenderedComponent<ExtensionConsentDialog> Render(
        IReadOnlyList<ExtensionConsentInfo> items, Action<bool>? result = null)
        => RenderComponent<ExtensionConsentDialog>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Extensions, items)
            .Add(p => p.OnConsentResult, approved => result?.Invoke(approved)));

    // --- extension requests ---

    [TestMethod]
    public void AnExtensionRequestKeepsItsOwnWording()
    {
        var cut = Render(new[] { Extension("Acme.Verso.Widgets", "1.2.0") });

        var markup = cut.Markup;
        StringAssert.Contains(markup, "Extension Consent Required");
        StringAssert.Contains(markup, "extension package");
        StringAssert.Contains(markup, "Acme.Verso.Widgets");
        StringAssert.Contains(markup, "(1.2.0)");
        StringAssert.Contains(markup, "Approve All");
    }

    [TestMethod]
    public void AnExtensionRequestShowsNoTargetLine()
    {
        var cut = Render(new[] { Extension("Acme.Verso.Widgets") });

        Assert.IsFalse(cut.Markup.Contains("Into "),
            "There is no environment to name for an extension.");
    }

    [TestMethod]
    public void OneExtensionReadsAsSingular()
    {
        var cut = Render(new[] { Extension("Acme.Verso.Widgets") });

        StringAssert.Contains(cut.Markup, "extension package:");
    }

    [TestMethod]
    public void SeveralExtensionsReadAsPlural()
    {
        var cut = Render(new[] { Extension("One"), Extension("Two") });

        StringAssert.Contains(cut.Markup, "extension packages:");
    }

    // --- package requests ---

    [TestMethod]
    public void APackageRequestNamesTheDistributionsAndTheEnvironment()
    {
        var cut = Render(new[]
        {
            Package("opencv-python", "import cv2", "Python 3.13.3 at /envs/demo/bin/python"),
            Package("scikit-learn", "import sklearn", "Python 3.13.3 at /envs/demo/bin/python"),
        });

        var markup = cut.Markup;
        StringAssert.Contains(markup, "Package Install Required");
        StringAssert.Contains(markup, "Python package");
        StringAssert.Contains(markup, "opencv-python");
        StringAssert.Contains(markup, "scikit-learn");
        StringAssert.Contains(markup, "import cv2");
        StringAssert.Contains(markup, "Install");
    }

    [TestMethod]
    public void APackageRequestShowsTheEnvironmentOnce()
    {
        var target = "Python 3.13.3 at /envs/demo/bin/python";
        var cut = Render(new[]
        {
            Package("opencv-python", "import cv2", target),
            Package("scikit-learn", "import sklearn", target),
        });

        var occurrences = cut.Markup.Split(target).Length - 1;
        Assert.AreEqual(1, occurrences, "The environment belongs to the request, not to each item.");
    }

    [TestMethod]
    public void APackageRequestWarnsAboutRunningCode()
    {
        var cut = Render(new[] { Package("opencv-python", "import cv2", "an environment") });

        StringAssert.Contains(cut.Markup, "Only install packages you trust.");
    }

    // --- the answer ---

    [TestMethod]
    public void TheApproveButtonReportsApproval()
    {
        bool? answer = null;
        var cut = Render(new[] { Package("opencv-python", "import cv2", "an environment") },
            approved => answer = approved);

        cut.Find("button.verso-modal-btn--primary").Click();

        Assert.IsTrue(answer);
    }

    [TestMethod]
    public void TheDenyButtonReportsRefusal()
    {
        bool? answer = null;
        var cut = Render(new[] { Package("opencv-python", "import cv2", "an environment") },
            approved => answer = approved);

        cut.Find("button.verso-modal-btn--secondary").Click();

        Assert.IsFalse(answer);
    }

    [TestMethod]
    public void NothingRendersWhenTheDialogIsHidden()
    {
        var cut = RenderComponent<ExtensionConsentDialog>(parameters => parameters
            .Add(p => p.IsVisible, false)
            .Add(p => p.Extensions, new[] { Extension("Acme.Verso.Widgets") }));

        Assert.AreEqual(string.Empty, cut.Markup.Trim());
    }
}
