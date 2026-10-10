namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class DiagnosticsSourceTests
{
    [TestMethod]
    public void NoMagic_ReturnsSourceUnchanged()
    {
        const string source = "var x = 1;\n#!time";

        Assert.AreSame(source, DiagnosticsSource.MaskLeadingMagicCommands(source));
    }

    [TestMethod]
    public void LeadingMagicLines_AreBlankedAndLineCountKept()
    {
        var masked = DiagnosticsSource.MaskLeadingMagicCommands(
            "  #!time\n\n#!nuget Foo\nvar x = 1;\n#!later");

        Assert.AreEqual("\n\n\nvar x = 1;\n#!later", masked);
    }

    [TestMethod]
    public void CrLfLineBreaks_ArePreserved()
    {
        var masked = DiagnosticsSource.MaskLeadingMagicCommands("#!time\r\nvar x = 1;");

        Assert.AreEqual("\r\nvar x = 1;", masked);
    }

    [TestMethod]
    public void BareShebang_IsNotAMagicCommand()
    {
        // "#!" with no command name is not something the pipeline strips, so neither is it here.
        const string source = "#!\nvar x = 1;";

        Assert.AreSame(source, DiagnosticsSource.MaskLeadingMagicCommands(source));
    }

    [TestMethod]
    public void AllMagic_ReturnsOnlyLineBreaks()
    {
        Assert.AreEqual("\n", DiagnosticsSource.MaskLeadingMagicCommands("#!sql-connect x\n#!time"));
    }
}
