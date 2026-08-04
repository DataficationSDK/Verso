using System.Globalization;
using Verso.Python.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.MagicCommands;

/// <summary>
/// Covers what <c>#!bind</c> makes of what an author types, and what it says when there is no
/// interpreter behind it. What happens when there is one is covered against a real subprocess.
/// </summary>
[TestClass]
public sealed class BindMagicCommandTests
{
    /// <summary>
    /// A context whose extension host loads no kernels, which is what a notebook with no
    /// Python kernel looks like from inside the command.
    /// </summary>
    private static StubMagicCommandContext Context() => new();

    private static string TextOf(StubMagicCommandContext context)
        => string.Concat(context.WrittenOutputs.Select(o => o.Content));

    // --- reading the argument ---

    [TestMethod]
    public void TheTraitIsTakenFromTheEndOfTheTarget()
    {
        Assert.IsTrue(BindMagicCommand.TryParse(
            "slider.value", out var expression, out var trait, out var name, out _));

        Assert.AreEqual("slider", expression);
        Assert.AreEqual("value", trait);
        Assert.IsNull(name);
    }

    [TestMethod]
    public void TheNameAfterAsIsTheVariable()
    {
        Assert.IsTrue(BindMagicCommand.TryParse(
            "slider.value as threshold", out var expression, out var trait, out var name, out _));

        Assert.AreEqual("slider", expression);
        Assert.AreEqual("value", trait);
        Assert.AreEqual("threshold", name);
    }

    [TestMethod]
    public void TheObjectIsSplitFromTheTraitAtTheLastDot()
    {
        // Otherwise this looks for a trait called 'layout.width' on the panel, which is not a
        // thing, rather than the width of the layout, which is.
        Assert.IsTrue(BindMagicCommand.TryParse(
            "panel.layout.width", out var expression, out var trait, out _, out _));

        Assert.AreEqual("panel.layout", expression);
        Assert.AreEqual("width", trait);
    }

    [TestMethod]
    public void AnyExpressionNamingTheObjectIsCarriedThrough()
    {
        Assert.IsTrue(BindMagicCommand.TryParse(
            "controls[0].value as threshold", out var expression, out var trait, out var name, out _));

        Assert.AreEqual("controls[0]", expression);
        Assert.AreEqual("value", trait);
        Assert.AreEqual("threshold", name);
    }

    [TestMethod]
    public void TheLettersOfAsInsideAnExpressionAreNotTheKeyword()
    {
        Assert.IsTrue(BindMagicCommand.TryParse(
            "readings[\"gas\"].value", out var expression, out var trait, out var name, out _));

        Assert.AreEqual("readings[\"gas\"]", expression);
        Assert.AreEqual("value", trait);
        Assert.IsNull(name);
    }

    [TestMethod]
    public void ATargetWithNoTraitIsRefusedWithTheReason()
    {
        Assert.IsFalse(BindMagicCommand.TryParse("slider", out _, out _, out _, out var problem));

        StringAssert.Contains(problem, "slider");
        StringAssert.Contains(problem, "slider.value");
    }

    [TestMethod]
    public void ATrailingDotNamesNoTrait()
    {
        Assert.IsFalse(BindMagicCommand.TryParse("slider.", out _, out _, out _, out var problem));
        Assert.IsNotNull(problem);
    }

    [TestMethod]
    public void ANameNoKernelCouldReadIsRefused()
    {
        // A shared variable is read as an identifier in languages that have no way to spell
        // anything else, so a name with a space in it would be written and never readable.
        Assert.IsFalse(BindMagicCommand.TryParse(
            "slider.value as my threshold", out _, out _, out _, out var problem));

        Assert.IsNotNull(problem);
    }

    [TestMethod]
    public void ANameStartingWithADigitIsRefused()
    {
        Assert.IsFalse(BindMagicCommand.TryParse(
            "slider.value as 2nd", out _, out _, out _, out var problem));

        StringAssert.Contains(problem, "2nd");
    }

    [TestMethod]
    public void AnUnderscoreIsAUsableName()
    {
        Assert.IsTrue(BindMagicCommand.TryParse(
            "slider.value as _threshold", out _, out _, out var name, out _));

        Assert.AreEqual("_threshold", name);
    }

    // --- what it says with nothing behind it ---

    [TestMethod]
    public async Task NoArgumentShowsHowToUseIt()
    {
        var context = Context();

        await new BindMagicCommand().ExecuteAsync("", context);

        Assert.IsTrue(context.WrittenOutputs.Single().IsError);
        StringAssert.Contains(TextOf(context), "#!bind");
        Assert.IsFalse(context.SuppressExecution, "The rest of the cell should still run.");
    }

    [TestMethod]
    public async Task AnUnreadableTargetIsReportedWithoutReachingTheKernel()
    {
        var context = Context();

        await new BindMagicCommand().ExecuteAsync("slider", context);

        Assert.IsTrue(context.WrittenOutputs.Single().IsError);
        StringAssert.Contains(TextOf(context), "slider.value");
    }

    [TestMethod]
    public async Task RemovingWithNoNameShowsHowToUseIt()
    {
        var context = Context();

        await new BindMagicCommand().ExecuteAsync("--remove", context);

        Assert.IsTrue(context.WrittenOutputs.Single().IsError);
        StringAssert.Contains(TextOf(context), "--remove");
    }

    [TestMethod]
    public async Task ListingWithNoKernelReportsNothingRatherThanFailing()
    {
        var context = Context();

        await new BindMagicCommand().ExecuteAsync("--list", context);

        Assert.IsFalse(context.WrittenOutputs.Single().IsError);
        Assert.IsFalse(context.SuppressExecution);
    }

    [TestMethod]
    public async Task BindingWithNoKernelSaysThereIsNothingToBindTo()
    {
        var context = Context();

        await new BindMagicCommand().ExecuteAsync("slider.value as threshold", context);

        Assert.IsTrue(context.WrittenOutputs.Single().IsError);
    }

    // --- the words it uses ---

    [TestMethod]
    public void ItDescribesItselfInTheCurrentLanguage()
    {
        var command = new BindMagicCommand();
        var english = command.Description;

        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-Ploc");
        try
        {
            Assert.AreNotEqual(english, command.Description);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        Assert.AreEqual(english, command.Description,
            "The description did not return to English when the language did.");
    }
}
