using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.Localization;

/// <summary>
/// The words the Python kernel puts on screen: what it says it is, and what its settings are
/// called in the panel that shows them.
/// </summary>
[TestClass]
public class KernelTextTests
{
    private static void InPseudoLocale(Action assert)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-Ploc");
        try
        {
            assert();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [TestMethod]
    public void Kernel_DescribesItselfInTheCurrentLanguage()
    {
        var kernel = new PythonKernel();
        var english = kernel.Description;

        InPseudoLocale(() => Assert.AreNotEqual(english, kernel.Description));

        Assert.AreEqual(english, kernel.Description,
            "The description did not return to English when the language did.");
    }

    [TestMethod]
    public void Settings_AreNamedInTheCurrentLanguage()
    {
        // The list is built on each read for exactly this reason. Holding it in a field would
        // bake in whichever language happened to be set when the kernel first loaded, and the
        // settings panel would keep showing that one for the life of the process.
        var kernel = new PythonKernel();
        var english = kernel.SettingDefinitions
            .Select(s => (s.DisplayName, s.Description)).ToList();

        Assert.IsTrue(english.Count > 0, "The kernel declares no settings, so this proves nothing.");

        InPseudoLocale(() =>
        {
            var pseudo = kernel.SettingDefinitions
                .Select(s => (s.DisplayName, s.Description)).ToList();

            CollectionAssert.AreNotEqual(english, pseudo,
                "The settings panel would show English on a machine set to another language.");
        });
    }

    [TestMethod]
    public void SettingNames_AreTheSameInEveryLanguage()
    {
        // A setting's name is written into the notebook file, so a notebook saved on a German
        // machine has to be readable on an English one. Only what is shown beside it is translated.
        var kernel = new PythonKernel();
        var english = kernel.SettingDefinitions.Select(s => s.Name).ToList();

        InPseudoLocale(() =>
            CollectionAssert.AreEqual(english, kernel.SettingDefinitions.Select(s => s.Name).ToList()));
    }
}
