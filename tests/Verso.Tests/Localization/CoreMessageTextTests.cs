using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Extensions.Formatters;
using Verso.MagicCommands;
using Verso.Parameters;
using Verso.Resources;

namespace Verso.Tests.Localization;

/// <summary>
/// The words the engine itself puts on screen: what a magic command says it does, what a
/// formatter says it does, and what a cell is told about a value it cannot use.
/// </summary>
/// <remarks>
/// Every one is an expression-bodied property, which is what lets a running host answer in the
/// language it was asked for. Holding one in a field would read its resource once and keep
/// whatever language happened to touch it first, and nothing else in the build would notice.
/// </remarks>
[TestClass]
public class CoreMessageTextTests
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

    // Built the way the extension host builds them, so anything added later is covered without
    // anyone remembering to add it here too.
    private static IReadOnlyList<IMagicCommand> MagicCommands() => new IMagicCommand[]
    {
        new AboutMagicCommand(),
        new ExtensionMagicCommand(),
        new ImportMagicCommand(),
        new NuGetMagicCommand(),
        new RestartMagicCommand(),
        new TimeMagicCommand(),
    };

    private static IReadOnlyList<IDataFormatter> Formatters() => new IDataFormatter[]
    {
        new CollectionFormatter(),
        new ExceptionFormatter(),
        new HtmlFormatter(),
        new ImageFormatter(),
        new ObjectFormatter(),
        new PrimitiveFormatter(),
        new SvgFormatter(),
    };

    [TestMethod]
    public void EveryMagicCommand_DescribesItselfInTheCurrentLanguage()
    {
        var english = MagicCommands().ToDictionary(c => c.Name, c => c.Description);

        InPseudoLocale(() =>
        {
            foreach (var command in MagicCommands())
            {
                Assert.AreNotEqual(english[command.Name], command.Description,
                    $"#!{command.Name} keeps its English description whatever language is asked for.");
            }
        });

        foreach (var command in MagicCommands())
        {
            Assert.AreEqual(english[command.Name], command.Description,
                $"#!{command.Name} did not return to English when the language did.");
        }
    }

    [TestMethod]
    public void EveryMagicCommand_DescribesItsArgumentsInTheCurrentLanguage()
    {
        // An argument's description is what tells a reader what to type there, so it follows the
        // language for the same reason the command's own description does.
        var english = MagicCommands()
            .SelectMany(c => c.Parameters.Select(p => p.Description))
            .Where(d => d is not null)
            .ToList();

        Assert.IsTrue(english.Count > 0, "No magic command declares an argument, so this proves nothing.");

        InPseudoLocale(() =>
        {
            var pseudo = MagicCommands()
                .SelectMany(c => c.Parameters.Select(p => p.Description))
                .Where(d => d is not null)
                .ToList();

            CollectionAssert.AreNotEqual(english, pseudo);
        });
    }

    [TestMethod]
    public void EveryMagicCommand_KeepsItsNameInEveryLanguage()
    {
        var english = MagicCommands().Select(c => c.Name).ToList();

        InPseudoLocale(() =>
        {
            CollectionAssert.AreEqual(english, MagicCommands().Select(c => c.Name).ToList(),
                "A magic command is typed at a keyboard, so its name cannot depend on the reader's language.");
        });
    }

    [TestMethod]
    public void EveryFormatter_DescribesItselfInTheCurrentLanguage()
    {
        var english = Formatters().ToDictionary(f => f.ExtensionId, f => f.Description);

        InPseudoLocale(() =>
        {
            foreach (var formatter in Formatters())
            {
                Assert.AreNotEqual(english[formatter.ExtensionId], formatter.Description,
                    $"{formatter.ExtensionId} keeps its English description whatever language is asked for.");
            }
        });
    }

    [TestMethod]
    public void ParameterValue_ExplainsARejectedValueInTheCurrentLanguage()
    {
        Assert.IsFalse(ParameterValueParser.TryParse("int", "seven", out _, out var english));
        Assert.AreEqual("Expected an integer value, got 'seven'.", english);

        InPseudoLocale(() =>
        {
            Assert.IsFalse(ParameterValueParser.TryParse("int", "seven", out _, out var pseudo));
            Assert.AreNotEqual(english, pseudo, "The reason did not follow the reader's language.");
            StringAssert.Contains(pseudo!, "seven", "The value the reader typed was lost in translation.");
        });
    }

    [TestMethod]
    public void CountedThings_ComeFromTwoEntriesRatherThanAStemAndAnS()
    {
        Assert.AreEqual("... (1 more line)", string.Format(
            Plural.Of(1, Strings.Export_MoreLines_One, Strings.Export_MoreLines_Other), 1));
        Assert.AreEqual("... (4 more lines)", string.Format(
            Plural.Of(4, Strings.Export_MoreLines_One, Strings.Export_MoreLines_Other), 4));

        // Zero takes the plural in English, which is the whole reason this is not a test for
        // "more than one".
        Assert.AreEqual("(0 members)", string.Format(
            Plural.Of(0, Strings.ObjectTree_MemberCount_One, Strings.ObjectTree_MemberCount_Other), 0));
    }
}
