using System.Globalization;
using Verso.Abstractions;
using Verso.Extensions.CellTypes;
using Verso.Extensions.Layouts;
using Verso.Extensions.Themes;
using Verso.Extensions.ToolbarActions;

namespace Verso.Tests.Localization;

/// <summary>
/// The built-in extensions answer in whatever language is current when they are asked,
/// rather than in whichever one happened to be current the first time.
/// </summary>
/// <remarks>
/// A server draws notebooks for several readers from one process, and each of them may have
/// asked for a different language. Anything that resolves its name once and keeps it would
/// serve the first reader's language to everybody after them, and the failure is invisible in
/// a single-language test run. The pseudo-locale is used here because it exists whether or not
/// a translator has reached these strings yet.
/// </remarks>
[TestClass]
public sealed class BuiltInExtensionCultureTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-Ploc");

    private static void InPseudoLocale(Action assert)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Pseudo;
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
    public void ToolbarAction_FollowsTheCurrentLanguage()
    {
        var action = new RunAllAction();
        var english = action.DisplayName;

        InPseudoLocale(() => Assert.AreNotEqual(
            english, action.DisplayName,
            "Run All resolved its label once and kept it, so a second reader would be served the first one's language."));

        Assert.AreEqual(english, action.DisplayName,
            "The label did not come back after the language did.");
    }

    [TestMethod]
    public void ConfirmationPrompt_FollowsTheCurrentLanguage()
    {
        var action = new RestartKernelAction();
        var english = action.ConfirmationPrompt;

        InPseudoLocale(() => Assert.AreNotEqual(english, action.ConfirmationPrompt));
    }

    [TestMethod]
    public void ExtensionDescription_FollowsTheCurrentLanguage()
    {
        IExtension extension = new MarkdownCellType();
        var english = extension.Description;

        InPseudoLocale(() => Assert.AreNotEqual(english, extension.Description));
    }

    [TestMethod]
    public void LayoutName_FollowsTheCurrentLanguage()
    {
        var layout = new NotebookLayout();
        var english = layout.DisplayName;

        InPseudoLocale(() => Assert.AreNotEqual(english, layout.DisplayName));
    }

    [TestMethod]
    public void ThemeName_FollowsTheCurrentLanguage()
    {
        // Only the word describing the theme is a translator's to change; the product's name
        // stays as written. That is a rule for whoever writes the translation, carried by the
        // note on the resource, and there is nothing in the code here to hold it to.
        var theme = new VersoDarkTheme();
        var english = theme.DisplayName;

        InPseudoLocale(() => Assert.AreNotEqual(english, theme.DisplayName));
    }

    [TestMethod]
    public void ExportMenuEntries_NameFormatsAndStayAsWritten()
    {
        // These read "HTML" and "Markdown" rather than a sentence, and a format's name is the
        // same in every language. Their entries in the Extensions panel are translated; these
        // are not, and a translation appearing here would be the mistake.
        var html = new ExportHtmlAction();
        var markdown = new ExportMarkdownAction();

        InPseudoLocale(() =>
        {
            Assert.AreEqual("HTML", html.DisplayName);
            Assert.AreEqual("Markdown", markdown.DisplayName);
        });
    }
}
