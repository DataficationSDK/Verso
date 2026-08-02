using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl;
using Verso.Cli.Repl.Meta;

namespace Verso.Cli.Tests.Repl;

/// <summary>
/// The words <c>.help</c> prints: the one-line summaries and the detailed help behind each name.
/// </summary>
/// <remarks>
/// A meta-command exposes both as expression-bodied properties, which is what lets a running
/// process answer in the language it was asked for. Holding either in a field would read the
/// resource once and keep whatever language happened to touch it first, and nothing else in the
/// build would notice.
/// </remarks>
[TestClass]
public class MetaCommandTextTests
{
    // Built the way a session builds it, so a command added later is covered without anyone
    // remembering to add it here too.
    private static IReadOnlyList<IMetaCommand> AllCommands()
        => ReplLoop.CreateDefaultRegistry().AllOrdered;

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
    public void EveryCommand_HasSomethingToSayForItself()
    {
        foreach (var command in AllCommands())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Summary),
                $".{command.Name} has no summary, so .help would print a blank row for it.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.DetailedHelp),
                $".{command.Name} has no detailed help, so '.help {command.Name}' would print nothing.");
        }
    }

    [TestMethod]
    public void EveryCommand_FollowsTheCurrentLanguage()
    {
        var english = AllCommands().ToDictionary(c => c.Name, c => (c.Summary, c.DetailedHelp));

        InPseudoLocale(() =>
        {
            foreach (var command in AllCommands())
            {
                var (summary, detailed) = english[command.Name];
                Assert.AreNotEqual(summary, command.Summary,
                    $".{command.Name} keeps its English summary whatever language is asked for.");
                Assert.AreNotEqual(detailed, command.DetailedHelp,
                    $".{command.Name} keeps its English help whatever language is asked for.");
            }
        });

        foreach (var command in AllCommands())
        {
            Assert.AreEqual(english[command.Name].Summary, command.Summary,
                $".{command.Name} did not return to English when the language did.");
        }
    }

    [TestMethod]
    public void EveryCommand_LeadsItsHelpWithWhatToType()
    {
        // The first line is the shape of the command, so it stays out of the resource file and
        // reads the same in every language. Everything after it is translated.
        InPseudoLocale(() =>
        {
            foreach (var command in AllCommands())
            {
                var firstLine = command.DetailedHelp.Split('\n')[0];
                StringAssert.StartsWith(firstLine, "." + command.Name,
                    $"'.help {command.Name}' does not open with the command as it is typed.");
            }
        });
    }

    [TestMethod]
    public void NamesAndAliases_AreTheSameInEveryLanguage()
    {
        var english = AllCommands()
            .Select(c => (c.Name, Aliases: c.Aliases.ToArray()))
            .ToList();

        InPseudoLocale(() =>
        {
            var pseudo = AllCommands()
                .Select(c => (c.Name, Aliases: c.Aliases.ToArray()))
                .ToList();

            CollectionAssert.AreEqual(
                english.Select(c => c.Name).ToList(),
                pseudo.Select(c => c.Name).ToList(),
                "A command is typed at a keyboard, so its name cannot depend on the reader's language.");

            for (var i = 0; i < english.Count; i++)
                CollectionAssert.AreEqual(english[i].Aliases, pseudo[i].Aliases);
        });
    }
}
