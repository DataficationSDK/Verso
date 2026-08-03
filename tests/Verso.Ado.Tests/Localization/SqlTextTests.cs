using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Ado.Formatters;
using Verso.Ado.Kernel;
using Verso.Ado.Models;

namespace Verso.Ado.Tests.Localization;

/// <summary>
/// The words a SQL cell puts on screen: what the editor offers while typing, and the line under
/// a table saying what came back.
/// </summary>
[TestClass]
public class SqlTextTests
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
    public void Keyword_IsExplainedInTheCurrentLanguage()
    {
        var english = SqlKernel.DescribeKeyword("SELECT");
        Assert.IsNotNull(english);

        InPseudoLocale(() =>
        {
            // Looked up rather than held in a table, so it answers in whatever language was asked
            // for rather than the one the kernel first loaded in.
            Assert.AreNotEqual(english, SqlKernel.DescribeKeyword("SELECT"));
        });

        Assert.AreEqual(english, SqlKernel.DescribeKeyword("SELECT"),
            "The explanation did not return to English when the language did.");
    }

    [TestMethod]
    public void Keyword_IsFoundHoweverItIsTyped()
    {
        Assert.AreEqual(SqlKernel.DescribeKeyword("SELECT"), SqlKernel.DescribeKeyword("select"));
        Assert.IsNull(SqlKernel.DescribeKeyword("Bananas"));
    }

    [TestMethod]
    public void RowsAffected_ComesFromTwoEntriesRatherThanAStemAndAnS()
    {
        StringAssert.Contains(ResultSetFormatter.FormatNonQueryHtml(1, 3, null), "1 row affected");

        // Zero takes the plural in English, which is the whole reason this is not a test for
        // "more than one".
        StringAssert.Contains(ResultSetFormatter.FormatNonQueryHtml(0, 3, null), "0 rows affected");
        StringAssert.Contains(ResultSetFormatter.FormatNonQueryHtml(9, 3, null), "9 rows affected");
    }

    [TestMethod]
    public void RowsAffected_IsWrittenInTheCurrentLanguage()
    {
        var english = ResultSetFormatter.FormatNonQueryHtml(5, 42, null);

        InPseudoLocale(() =>
            Assert.AreNotEqual(english, ResultSetFormatter.FormatNonQueryHtml(5, 42, null)));
    }

    [TestMethod]
    public void PagingScript_CarriesItsSentenceRatherThanBuildingOneInTheBrowser()
    {
        // The line under a table is rewritten in the browser as the reader pages through, so the
        // sentence goes over as a template with its placeholders intact. Assembling it there from
        // words and numbers would put it beyond a translator's reach.
        var columns = new[] { new SqlColumnMetadata("X", "INTEGER", typeof(int), false) };
        var rows = Enumerable.Range(0, 120).Select(i => new object?[] { i }).ToList();
        var html = ResultSetFormatter.FormatResultSetHtml(
            new SqlResultSet(columns, rows, rows.Count, false), null, pageSize: 50);

        StringAssert.Contains(html, "var SHOWING=");
        StringAssert.Contains(html, "{0}");
        StringAssert.Contains(html, "{2}");
    }
}
