using System.Globalization;
using Microsoft.Data.Analysis;
using Verso.DataFrame.Formatters;
using Verso.Testing.Stubs;
using AnalysisDataFrame = Microsoft.Data.Analysis.DataFrame;

namespace Verso.DataFrame.Tests.Localization;

/// <summary>
/// The words the DataFrame table puts on screen: the line under the table saying what is
/// shown, and the notices for a frame with nothing in it.
/// </summary>
[TestClass]
public sealed class DataFrameTextTests
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
    public async Task Footer_SpeaksTheCurrentLanguage()
    {
        var formatter = new DataFrameFormatter();
        var context = new StubFormatterContext();
        var frame = new AnalysisDataFrame(
            new[] { new DataFrameColumn("value", typeof(string)) },
            new[] { new DataFrameRow("v") });

        var english = await formatter.FormatAsync(frame, context);
        StringAssert.Contains(english.Content, "Showing 1 of 1 rows");

        InPseudoLocale(() =>
        {
            var localized = formatter.FormatAsync(frame, context).GetAwaiter().GetResult();
            StringAssert.Contains(localized.Content, "[!!");
        });
    }

    [TestMethod]
    public async Task EmptyFrameNotice_SpeaksTheCurrentLanguage()
    {
        var formatter = new DataFrameFormatter();
        var context = new StubFormatterContext();
        var frame = new AnalysisDataFrame();

        var english = await formatter.FormatAsync(frame, context);
        StringAssert.Contains(english.Content, "DataFrame has no columns.");

        InPseudoLocale(() =>
        {
            var localized = formatter.FormatAsync(frame, context).GetAwaiter().GetResult();
            StringAssert.Contains(localized.Content, "[!!");
        });
    }
}
