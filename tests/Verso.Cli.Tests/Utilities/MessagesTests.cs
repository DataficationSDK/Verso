using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Tests.Utilities;

/// <summary>
/// Assembling a line out of a translated sentence, values, and the odd thing typed at a keyboard.
/// </summary>
[TestClass]
public class MessagesTests
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
    public void Say_FillsInTheValues()
    {
        Assert.AreEqual(
            "Active layout: report",
            Messages.Say(Strings.Meta_Layout_Active, "report"));
    }

    [TestMethod]
    public void Say_EscapesASquareBracketInAValue()
    {
        // A file path may contain one, and the terminal writer reads a lone bracket as the start
        // of a style, so an unescaped path would either vanish or throw.
        var line = Messages.Say(Strings.Meta_Load_FileNotFound, "/tmp/[draft]/notes.verso");

        StringAssert.Contains(line, "[[draft]]");
        Assert.IsFalse(line.Contains("/[draft]/"), "A bracket in the path reached the writer unescaped.");
    }

    [TestMethod]
    public void Say_EscapesASquareBracketInTheSentence()
    {
        // Not hypothetical: the sentences that name a cell by number are written with brackets
        // around it, and a translator keeps the punctuation the source used.
        var line = Messages.Say(Strings.Meta_Recall_OutOfRange, 99);

        StringAssert.Contains(line, "[[99]]");
    }

    [TestMethod]
    public void Typed_LeavesWhatIsTypedAsWritten()
    {
        var line = Messages.Typed(Strings.Repl_UnsavedHint, ".save", ".load");

        StringAssert.Contains(line, "[bold].save[/]");
        StringAssert.Contains(line, "[bold].load[/]");
    }

    [TestMethod]
    public void Typed_StillCarriesTheCommandsWhenTheSentenceIsTranslated()
    {
        // The whole point of naming them through placeholders: a language that puts the verb
        // last moves the words around the commands, and the commands go with them unchanged.
        InPseudoLocale(() =>
        {
            var line = Messages.Typed(Strings.Repl_UnsavedHint, ".save", ".load");

            StringAssert.Contains(line, "[bold].save[/]");
            StringAssert.Contains(line, "[bold].load[/]");
            Assert.AreNotEqual(
                Messages.Typed("Run {0} first, or {1} again to discard.", ".save", ".load"),
                line,
                "The sentence was resolved once and kept, so one reader's language would reach them all.");
        });
    }

    [TestMethod]
    public void Error_AndWarning_FollowTheCurrentLanguage()
    {
        var english = Messages.Error("something");

        InPseudoLocale(() =>
        {
            Assert.AreNotEqual(english, Messages.Error("something"));
            StringAssert.Contains(Messages.Error("something"), "something",
                "The prefix was translated but took the message with it.");
            StringAssert.Contains(Messages.Warning("something"), "something");
        });

        Assert.AreEqual(english, Messages.Error("something"),
            "The English wording did not come back after the language did.");
    }

    [TestMethod]
    public void In_WrapsTheWholeLine()
    {
        Assert.AreEqual("[green]done[/]", Messages.In("green", "done"));
    }
}
