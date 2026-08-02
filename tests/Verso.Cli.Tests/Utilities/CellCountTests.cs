using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Utilities;

namespace Verso.Cli.Tests.Utilities;

/// <summary>
/// Writing out how many cells something holds, which is the one count the CLI says out loud.
/// </summary>
[TestClass]
public class CellCountTests
{
    [TestMethod]
    public void Describe_UsesTheSingularForExactlyOne()
    {
        Assert.AreEqual("1 cell", CellCount.Describe(1));
    }

    [TestMethod]
    public void Describe_UsesThePluralForEverythingElse()
    {
        // Zero takes the plural in English, which is the whole reason this is not a test for
        // "more than one".
        Assert.AreEqual("0 cells", CellCount.Describe(0));
        Assert.AreEqual("2 cells", CellCount.Describe(2));
        Assert.AreEqual("57 cells", CellCount.Describe(57));
    }

    [TestMethod]
    public void Describe_NeverBuildsAWordOutOfAStemAndAnS()
    {
        // The forms come from two separate entries, so a language whose plural is not its
        // singular with a letter on the end still gets a phrase it can use.
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-Ploc");
        try
        {
            var one = CellCount.Describe(1);
            var many = CellCount.Describe(3);

            Assert.AreNotEqual(one, many);
            Assert.AreNotEqual("1 cell", one, "The phrase did not follow the reader's language.");
            StringAssert.Contains(one, "1");
            StringAssert.Contains(many, "3");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
